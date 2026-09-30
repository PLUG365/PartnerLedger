$ErrorActionPreference = 'Stop'

$solutionRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\solutions\PartnerLedger')).Path
$definitionRoot = Join-Path $solutionRoot 'environmentvariabledefinitions'
$solutionXml = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $solutionRoot 'Other\Solution.xml'))

$expectedNames = @(
    'pl_ApprovalGroupAddress',
    'pl_ApproverTeamId'
)
$retiredNames = @(
    'pl_AccessExecutionUserId',
    'pl_ApprovalExecutionUserId',
    'pl_OcrExecutionUserId',
    # 2026-09-26 モバイルCanvasアプリ廃止に伴い不要（CanvasからPC Code Appを開くためだけに使っていた）。
    'pl_PcCodeAppUrl'
)
$actualNames = @(Get-ChildItem -LiteralPath $definitionRoot -Directory | Where-Object {
    Test-Path -LiteralPath (Join-Path $_.FullName 'environmentvariabledefinition.xml')
} | ForEach-Object Name | Sort-Object)
$sortedExpectedNames = @($expectedNames | Sort-Object)
if (($actualNames -join '|') -cne ($sortedExpectedNames -join '|')) {
    throw "Unexpected environment-variable definition set. Expected=[$($sortedExpectedNames -join ', ')]; actual=[$($actualNames -join ', ')]"
}
foreach ($retiredName in $retiredNames) {
    if (Test-Path -LiteralPath (Join-Path (Join-Path $definitionRoot $retiredName) 'environmentvariabledefinition.xml')) {
        throw "Retired environment variable remains in the portable source: $retiredName"
    }
}

$rootComponents = @($solutionXml.ImportExportXml.SolutionManifest.RootComponents.RootComponent)
$offlineEntityDirectory = Join-Path $solutionRoot 'Entities\pl_OfflineSelection'
if (Test-Path -LiteralPath $offlineEntityDirectory) {
    throw 'The retired offline-selection table must not remain as an empty entity directory in the portable source.'
}
if (@($rootComponents | Where-Object { $_.type -eq '1' -and $_.schemaName -ieq 'pl_offlineselection' }).Count -gt 0) {
    throw 'The retired offline-selection table must not be a Solution root component.'
}
foreach ($schemaName in $expectedNames) {
    $definitionPath = Join-Path (Join-Path $definitionRoot $schemaName) 'environmentvariabledefinition.xml'
    $definition = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $definitionPath)
    $node = $definition.environmentvariabledefinition
    if ($node.schemaname -cne $schemaName) { throw "Schema name mismatch in $definitionPath" }
    if ($node.type -cne '100000000') { throw "Environment variable $schemaName must remain Text/String." }
    if ([string]::IsNullOrWhiteSpace($node.isrequired) -or $node.isrequired -cne '1') {
        throw "Environment variable $schemaName must require an import-time value."
    }
    if ($null -ne $node.defaultvalue -or $null -ne $node.currentvalue) {
        throw "Environment variable $schemaName must not embed a default/current value."
    }
}

$valueFiles = @(Get-ChildItem -LiteralPath $solutionRoot -Recurse -File | Where-Object {
    $_.Name -ieq 'environment_variable_values.json' -or $_.Name -ieq 'environmentvariablevalues.json'
})
if ($valueFiles.Count -gt 0) {
    throw "Environment variable values must not be committed in the portable solution: $($valueFiles.FullName -join ', ')"
}

$roleDirectory = Join-Path $solutionRoot 'Roles'
$readerRoleFiles = @(Get-ChildItem -LiteralPath $roleDirectory -Filter '*.xml' -File | Where-Object {
    $role = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName)
    [string]$role.Role.id -ieq '{0752d465-fe90-47c3-aa95-f5d4bf33fa35}'
})
$readerRoleRootCount = @($rootComponents | Where-Object {
    $_.type -eq '20' -and $_.id -ieq '{0752d465-fe90-47c3-aa95-f5d4bf33fa35}'
}).Count
if ($readerRoleFiles.Count -gt 0 -or $readerRoleRootCount -gt 0) {
    throw 'The retired environment-variable Reader Role must not remain in the portable source.'
}

$stepDirectory = Join-Path $solutionRoot 'SdkMessageProcessingSteps'
$stepFiles = @(Get-ChildItem -LiteralPath $stepDirectory -Filter '*.xml' -File)
$expectedOcrStepNames = @(
    'pl_cardinputversion Create PostOperation: BusinessCardOcrJobCreate',
    'pl_cardinputversion Update PreValidation: BusinessCardInputVersionUpdateGuard',
    'pl_businesscardreview Create PostOperation: BusinessCardReviewOwner',
    'pl_cardcapture Assign PreOperation: BusinessCardCaptureOwner'
)
$actualOcrStepNames = @($stepFiles | ForEach-Object {
    ([xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName)).SdkMessageProcessingStep.Name
} | Where-Object { $_ -in $expectedOcrStepNames } | Sort-Object)
if (($actualOcrStepNames -join '|') -cne (@($expectedOcrStepNames | Sort-Object) -join '|')) {
    throw 'Solution source must include all four read-back OCR Job/InputVersion/Owner synchronization Steps.'
}
$pluginAssemblyDataFiles = @(Get-ChildItem -LiteralPath (Join-Path $solutionRoot 'PluginAssemblies') -Filter '*.dll.data.xml' -File -Recurse)
if ($pluginAssemblyDataFiles.Count -ne 1) {
    throw "Expected exactly one Plugin Assembly metadata file; found $($pluginAssemblyDataFiles.Count)."
}
$pluginAssemblyDocument = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $pluginAssemblyDataFiles[0].FullName)
$pluginTypeMetadata = @($pluginAssemblyDocument.PluginAssembly.PluginTypes.PluginType)
$pluginAssemblyDllPath = Join-Path $solutionRoot ([string]$pluginAssemblyDocument.PluginAssembly.FileName).TrimStart('/').Replace('/', '\')
if (-not (Test-Path -LiteralPath $pluginAssemblyDllPath -PathType Leaf)) {
    throw "Plugin Assembly metadata references a missing DLL: $pluginAssemblyDllPath"
}
$ocrSteps = @($stepFiles | ForEach-Object { [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName) } | Where-Object {
    $_.SdkMessageProcessingStep.Name -in $expectedOcrStepNames
})
foreach ($stepDocument in $ocrSteps) {
    $step = $stepDocument.SdkMessageProcessingStep
    $pluginType = @($pluginTypeMetadata | Where-Object { $_.PluginTypeId -ieq $step.PluginTypeId })
    $expectedPluginTypeName = ([string]$step.PluginTypeName -split ',', 2)[0]
    if ($pluginType.Count -ne 1 -or $pluginType[0].Name -cne $expectedPluginTypeName) {
        throw "OCR/Owner Step PluginTypeId does not resolve to its Assembly metadata: $($step.Name)"
    }
}
foreach ($stepFile in $stepFiles) {
    $stepDocument = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $stepFile.FullName)
    $step = $stepDocument.SdkMessageProcessingStep
    $configuration = [string]$step.Configuration
    if (-not [string]::IsNullOrWhiteSpace($configuration)) {
        throw "Plugin Step contains an unreviewed configuration value: $($stepFile.Name)"
    }
    if ($step.Name -in $expectedOcrStepNames -and
        -not [string]::IsNullOrWhiteSpace([string]$step.ImpersonatingUserIdName)) {
        throw "OCR Job/Owner Step source must not bind a tenant-specific Run-as principal: $($stepFile.Name)"
    }
}

# 一度きりの構築スクリプト（register-*／apply-*など）は2026-09-27に削除した。移送はソリューションのインポートと手順書だけで行う。

# 2026-09-26 Role方針③：移送するRoleは人向け4つだけ。技術Role（Application User時代）は戻さない。
$expectedRoleNames = @('PL システム保守', 'PL システム管理', 'PL 利用者', 'PL 承認')
$actualRoleNames = @(Get-ChildItem -LiteralPath $roleDirectory -Filter '*.xml' -File | ForEach-Object {
    [string]([xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName)).Role.name
} | Sort-Object)
if (($actualRoleNames -join '|') -cne (@($expectedRoleNames | Sort-Object) -join '|')) {
    throw "The portable solution must carry only the four people-facing roles. Actual=[$($actualRoleNames -join ', ')]"
}
if (@($rootComponents | Where-Object { $_.type -eq '20' }).Count -ne $expectedRoleNames.Count) {
    throw 'Solution.xml must list exactly the four people-facing roles as role root components.'
}
function Get-RolePrivilegeLevel([string] $roleName, [string] $privilegeName) {
    $path = Join-Path $roleDirectory "$roleName.xml"
    $privileges = @(([xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $path)).Role.RolePrivileges.RolePrivilege)
    $match = @($privileges | Where-Object { $_.name -ceq $privilegeName })
    if ($match.Count -eq 0) { return $null }
    return [string]$match[0].level
}
# PL-033：PL システム管理は全申請を読み、提出中の申請を理由付きで取り消す（取消以外の更新はサーバー側ガードが拒否）。
foreach ($privilegeName in @('prvReadpl_Request', 'prvWritepl_Request')) {
    if ((Get-RolePrivilegeLevel 'PL システム管理' $privilegeName) -cne 'Global') {
        throw "PL システム管理 must hold $privilegeName at Global depth for approval cancellation."
    }
}
# 提出版の内容は管理者にも読ませない（承認申請画面は変更内容を表示しない側に倒れる）。
if ($null -ne (Get-RolePrivilegeLevel 'PL システム管理' 'prvReadpl_SubmissionVersion')) {
    throw 'PL システム管理 must not read submission versions.'
}
# PluginはSYSTEMのサービスで環境変数を読む（2026-09-27）ので、人向けRoleに環境変数Readは要らない。
foreach ($roleName in $expectedRoleNames) {
    if ($null -ne (Get-RolePrivilegeLevel $roleName 'prvReadEnvironmentVariableDefinition')) {
        throw "$roleName must not carry environment-variable read; plug-ins read settings through the SYSTEM service."
    }
}
$approvalFlowPath = Join-Path $solutionRoot 'Workflows\PartnerLedger_ApprovalGroupBridge_v1-01015B03-54B1-F111-AAAC-E4FB1EFF79C7.json'
$approvalFlow = Get-Content -Raw -Encoding UTF8 -LiteralPath $approvalFlowPath | ConvertFrom-Json
$approvalFlowParameters = $approvalFlow.properties.definition.parameters
$approvalAddressParameter = $approvalFlowParameters.PSObject.Properties['pl_ApprovalGroupAddress']
if ($null -eq $approvalAddressParameter -or
    $approvalAddressParameter.Value.type -cne 'String' -or
    $null -ne $approvalAddressParameter.Value.defaultValue -or
    $approvalAddressParameter.Value.metadata.schemaName -cne 'pl_ApprovalGroupAddress') {
    throw 'Approval Bridge must declare pl_ApprovalGroupAddress as a String flow parameter without an embedded default value.'
}
$bridgeActions = $approvalFlow.properties.definition.actions.Check_pending_approval_link.actions
$addressGuard = $bridgeActions.Check_approval_group_address
$guardTerms = @($addressGuard.expression.not.equals)
if ($addressGuard.type -cne 'If' -or
    $guardTerms.Count -ne 2 -or
    $guardTerms[0] -cne "@trim(coalesce(parameters('pl_ApprovalGroupAddress'),''))" -or
    $guardTerms[1] -cne '' -or
    $addressGuard.else.actions.Fail_missing_approval_group_address.type -cne 'Terminate' -or
    $addressGuard.else.actions.Fail_missing_approval_group_address.inputs.runStatus -cne 'Failed' -or
    $addressGuard.else.actions.Fail_missing_approval_group_address.inputs.runError.code -cne 'PartnerLedger.ApprovalGroupAddressMissing') {
    throw 'Approval Bridge must fail with a specific Run-history error before creating an Approval when the address is null, empty, or whitespace.'
}
$startApproval = $bridgeActions.Start_group_approval
$requestAfterResponse = $bridgeActions.Get_request_after_response
$cancelledGuard = $bridgeActions.If_request_cancelled
# 2026-09-30（ユーザー決定「Bで進めよう」、独立監査後の再計画）：承認依頼は、提出時にサーバーが申請に記録した承認者チーム
# （pl_approverteamlookup）に紐づくEntraグループのメンバー個人（ゲストを除く）へ割り当てる。アクション可能メッセージは個人の
# メールボックスだけに対応するため。宛先は最上位の変数に持ち、グループのアドレスで初期化して、読めたときだけ上書きする。
function Get-RunAfterStatuses($action, [string]$name) {
    if ($null -eq $action -or $null -eq $action.runAfter) { return '' }
    $property = $action.runAfter.PSObject.Properties[$name]
    if ($null -eq $property) { return '' }
    return (@($property.Value | Sort-Object) -join '|')
}
function Get-RunAfterNames($action) {
    if ($null -eq $action -or $null -eq $action.runAfter) { return '' }
    return (@($action.runAfter.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object) -join '|')
}
$allStatuses = 'Failed|Skipped|Succeeded|TimedOut'
$topActions = $approvalFlow.properties.definition.actions
$initAssignees = $topActions.Init_approval_assignees
$approverTeam = $bridgeActions.Get_approver_team
$approverMembers = $bridgeActions.List_approver_members
$filterMembers = $bridgeActions.Filter_approver_members
$selectAddresses = $bridgeActions.Select_approver_addresses
$membersFound = $bridgeActions.If_members_found
$setAssignees = $membersFound.actions.Set_individual_assignees
$fallbackCheck = $bridgeActions.Check_assignee_fallback
$groupAddress = "trim(coalesce(parameters('pl_ApprovalGroupAddress'),''))"
$operatorFetch = "<fetch top='1'><entity name='systemuser'><attribute name='internalemailaddress'/><filter><condition attribute='systemuserid' operator='eq-userid'/></filter></entity></fetch>"
if ($null -eq $initAssignees -or $null -eq $approverTeam -or $null -eq $approverMembers -or $null -eq $filterMembers -or
    $null -eq $selectAddresses -or $null -eq $membersFound -or $null -eq $setAssignees -or $null -eq $fallbackCheck -or
    $null -ne $approvalFlow.properties.definition.parameters.PSObject.Properties['pl_ApproverTeamId'] -or
    $initAssignees.type -cne 'InitializeVariable' -or
    @($initAssignees.inputs.variables).Count -ne 1 -or
    $initAssignees.inputs.variables[0].name -cne 'approval_assignees' -or
    $initAssignees.inputs.variables[0].type -cne 'string' -or
    $initAssignees.inputs.variables[0].value -cne "@{$groupAddress}" -or
    (Get-RunAfterNames $initAssignees) -cne '' -or
    (Get-RunAfterNames $topActions.Check_pending_approval_link) -cne 'Get_approval_link|Init_approval_assignees' -or
    (Get-RunAfterStatuses $topActions.Check_pending_approval_link 'Init_approval_assignees') -cne 'Succeeded' -or
    $bridgeActions.Get_request.inputs.parameters.'$select' -notmatch '_pl_approverteamlookup_value' -or
    $approverTeam.inputs.host.operationId -cne 'GetItem' -or
    $approverTeam.inputs.parameters.entityName -cne 'teams' -or
    $approverTeam.inputs.parameters.recordId -cne "@body('Get_request')?['_pl_approverteamlookup_value']" -or
    $approverTeam.inputs.parameters.'$select' -cne 'azureactivedirectoryobjectid' -or
    (Get-RunAfterNames $approverTeam) -cne 'Get_request' -or
    (Get-RunAfterStatuses $approverTeam 'Get_request') -cne 'Succeeded' -or
    $approverMembers.inputs.host.connectionName -cne 'shared_office365groups' -or
    $approverMembers.inputs.host.operationId -cne 'ListGroupMembers' -or
    $approverMembers.inputs.parameters.groupId -cne "@body('Get_approver_team')?['azureactivedirectoryobjectid']" -or
    [int]$approverMembers.inputs.parameters.'$top' -ne 999 -or
    (Get-RunAfterNames $approverMembers) -cne 'Get_approver_team' -or
    (Get-RunAfterStatuses $approverMembers 'Get_approver_team') -cne 'Succeeded' -or
    $filterMembers.type -cne 'Query' -or
    $filterMembers.inputs.from -cne "@coalesce(body('List_approver_members')?['value'], json('[]'))" -or
    # ゲスト（UPNに#EXT#）はDataverseのチーム（メンバーシップの種類「メンバー」）に入らず、申請を開けないので除く。
    $filterMembers.inputs.where -cne "@and(not(contains(toLower(coalesce(item()?['userPrincipalName'], '')), '#ext#')), not(empty(coalesce(item()?['mail'], item()?['userPrincipalName'], ''))))" -or
    (Get-RunAfterNames $filterMembers) -cne 'List_approver_members' -or
    (Get-RunAfterStatuses $filterMembers 'List_approver_members') -cne 'Succeeded' -or
    $selectAddresses.type -cne 'Select' -or
    $selectAddresses.inputs.from -cne "@body('Filter_approver_members')" -or
    $selectAddresses.inputs.select -cne "@toLower(coalesce(item()?['mail'], item()?['userPrincipalName']))" -or
    (Get-RunAfterStatuses $selectAddresses 'Filter_approver_members') -cne 'Succeeded' -or
    # 変数の上書きの中で同じ変数は読めないので、1人以上いるときだけ条件の中で上書きする。
    $membersFound.type -cne 'If' -or
    $membersFound.expression.greater[0] -cne "@length(body('Select_approver_addresses'))" -or
    [int]$membersFound.expression.greater[1] -ne 0 -or
    (Get-RunAfterNames $membersFound) -cne 'Select_approver_addresses' -or
    (Get-RunAfterStatuses $membersFound 'Select_approver_addresses') -cne 'Succeeded' -or
    ($null -ne $membersFound.PSObject.Properties['else'] -and @($membersFound.else.actions.PSObject.Properties).Count -ne 0) -or
    $setAssignees.type -cne 'SetVariable' -or
    $setAssignees.inputs.name -cne 'approval_assignees' -or
    $setAssignees.inputs.value -cne "@join(union(body('Select_approver_addresses'), body('Select_approver_addresses')), ';')" -or
    $approvalFlow.properties.connectionReferences.shared_office365groups.connection.connectionReferenceLogicalName -cne 'pl_office365groups' -or
    $approvalFlow.properties.connectionReferences.shared_office365groups.api.name -cne 'shared_office365groups') {
    throw 'Approval Bridge must assign the approval to the non-guest members of the request''s approver team group and keep the group address when the members cannot be read.'
}
# グループあてに戻ったときは、運用管理者（Dataverse接続のアカウント）へ既存のOutlook接続で知らせる。失敗しても承認依頼は止めない。
$fallbackTerms = @($fallbackCheck.expression.equals)
$fallbackOperator = $fallbackCheck.actions.Get_operator_for_fallback
$fallbackNotify = $fallbackCheck.actions.Notify_operator_group_fallback
if ($fallbackCheck.type -cne 'If' -or
    (Get-RunAfterNames $fallbackCheck) -cne 'If_members_found' -or
    (Get-RunAfterStatuses $fallbackCheck 'If_members_found') -cne $allStatuses -or
    $fallbackTerms.Count -ne 2 -or
    $fallbackTerms[0] -cne "@variables('approval_assignees')" -or
    $fallbackTerms[1] -cne "@$groupAddress" -or
    $null -eq $fallbackOperator -or $null -eq $fallbackNotify -or
    $fallbackOperator.inputs.host.operationId -cne 'ListRecords' -or
    $fallbackOperator.inputs.parameters.entityName -cne 'systemusers' -or
    $fallbackOperator.inputs.parameters.fetchXml -cne $operatorFetch -or
    $fallbackNotify.inputs.host.connectionName -cne 'shared_office365' -or
    $fallbackNotify.inputs.host.operationId -cne 'SendEmailV2' -or
    $fallbackNotify.inputs.parameters.'emailMessage/To' -cne "@{first(body('Get_operator_for_fallback')?['value'])?['internalemailaddress']}" -or
    (Get-RunAfterStatuses $fallbackNotify 'Get_operator_for_fallback') -cne 'Succeeded' -or
    ($null -ne $fallbackCheck.PSObject.Properties['else'] -and @($fallbackCheck.else.actions.PSObject.Properties).Count -ne 0)) {
    throw 'Approval Bridge must email the operator when the approval falls back to the group address, without blocking the approval.'
}
if ($startApproval.inputs.parameters.'WebhookApprovalCreationInput/assignedTo' -cne "@variables('approval_assignees')" -or
    (Get-RunAfterNames $startApproval) -cne 'Check_approval_group_address|Check_assignee_fallback|Compose_app_link|Get_request|Get_submission_version' -or
    (Get-RunAfterStatuses $startApproval 'Check_assignee_fallback') -cne $allStatuses -or
    (Get-RunAfterStatuses $startApproval 'Check_approval_group_address') -cne 'Succeeded' -or
    (Get-RunAfterStatuses $startApproval 'Compose_app_link') -cne 'Succeeded' -or
    (Get-RunAfterStatuses $startApproval 'Get_request') -cne 'Succeeded' -or
    (Get-RunAfterStatuses $startApproval 'Get_submission_version') -cne 'Succeeded' -or
    (Get-RunAfterStatuses $requestAfterResponse 'Start_group_approval') -cne 'Succeeded' -or
    (Get-RunAfterStatuses $cancelledGuard 'Get_request_after_response') -cne 'Succeeded' -or
    $null -ne $bridgeActions.PSObject.Properties['If_approved']) {
    throw 'Approval Bridge must use the resolved assignees and keep result processing behind successful approval creation and a fresh request read.'
}
# 承認依頼を作れなかった（承認の作成・待機が失敗した）ら、申請がまだ提出中なら理由付きで取り消し、運用管理者へ知らせて実行を失敗で終える。
# 失敗時の処理はスコープにまとめ、作成・待機が失敗したときだけ入る（成功の経路でスキップが連鎖して動かないように）。
$failureScope = $bridgeActions.Handle_approval_not_created
$requestAfterFailure = $failureScope.actions.Get_request_after_failure
$failureGuard = $failureScope.actions.If_request_submitted_after_failure
$failureCancel = $failureGuard.actions.Cancel_request_approval_not_created
$failureOperator = $failureScope.actions.Get_operator_for_failure
$failureNotify = $failureScope.actions.Notify_operator_approval_not_created
$failureEnd = $failureScope.actions.End_after_approval_not_created
if ($null -eq $failureScope -or $null -eq $requestAfterFailure -or $null -eq $failureGuard -or $null -eq $failureCancel -or $null -eq $failureOperator -or $null -eq $failureNotify -or $null -eq $failureEnd -or
    $failureScope.type -cne 'Scope' -or
    (Get-RunAfterNames $failureScope) -cne 'Start_group_approval' -or
    (Get-RunAfterStatuses $failureScope 'Start_group_approval') -cne 'Failed' -or
    (Get-RunAfterNames $requestAfterFailure) -cne '' -or
    $requestAfterFailure.inputs.parameters.entityName -cne 'pl_requests' -or
    $requestAfterFailure.inputs.parameters.recordId -cne "@body('Get_approval_link')?['_pl_requestlookup_value']" -or
    (Get-RunAfterStatuses $failureGuard 'Get_request_after_failure') -cne 'Succeeded' -or
    $failureGuard.expression.equals[0] -cne "@body('Get_request_after_failure')?['pl_requeststatuscode']" -or
    $failureGuard.expression.equals[1] -cne '提出中' -or
    $failureCancel.inputs.host.operationId -cne 'UpdateOnlyRecord' -or
    $failureCancel.inputs.parameters.entityName -cne 'pl_requests' -or
    $failureCancel.inputs.parameters.recordId -cne "@body('Get_approval_link')?['_pl_requestlookup_value']" -or
    $failureCancel.inputs.parameters.item.pl_requeststatuscode -cne '取消' -or
    $failureCancel.inputs.parameters.item.pl_cancellationreason -cne '承認依頼を作れなかったため、自動で取り消しました。もう一度申請してください。続くときは管理者に連絡してください。' -or
    @($failureCancel.inputs.parameters.item.PSObject.Properties).Count -ne 2 -or
    (Get-RunAfterStatuses $failureOperator 'If_request_submitted_after_failure') -cne $allStatuses -or
    $failureOperator.inputs.parameters.entityName -cne 'systemusers' -or
    $failureOperator.inputs.parameters.fetchXml -cne $operatorFetch -or
    $failureNotify.inputs.host.operationId -cne 'SendEmailV2' -or
    $failureNotify.inputs.parameters.'emailMessage/To' -cne "@{first(body('Get_operator_for_failure')?['value'])?['internalemailaddress']}" -or
    (Get-RunAfterStatuses $failureNotify 'Get_operator_for_failure') -cne 'Succeeded' -or
    $failureEnd.type -cne 'Terminate' -or
    $failureEnd.inputs.runStatus -cne 'Failed' -or
    $failureEnd.inputs.runError.code -cne 'PartnerLedger.ApprovalNotCreated' -or
    (Get-RunAfterStatuses $failureEnd 'Notify_operator_approval_not_created') -cne $allStatuses) {
    throw 'Approval Bridge must cancel a still-submitted request with a reason, email the operator and fail the run when the approval cannot be created.'
}
# 2026-09-26：承認依頼に、サーバーが作った申請内容の概要（提出版のpl_changesummary）・申請者・アプリへのリンクを載せる。
# アプリのURLは環境ごとのcanvasapps.appopenuriから作り、読めなくても承認依頼は止めない（空にせずPower Appsの入口を載せる）。
$startParams = $startApproval.inputs.parameters
$appList = $bridgeActions.List_partnerledger_app
$appUri = $bridgeActions.Compose_app_uri
$appLink = $bridgeActions.Compose_app_link
if ($null -eq $appList -or $null -eq $appUri -or $null -eq $appLink -or
    $null -eq $startApproval.runAfter.PSObject.Properties['Compose_app_link']) {
    throw 'Approval Bridge must show the server-built change summary, the requester and a link to the request, and must not block the approval when the app link cannot be read.'
}
$appUriRunAfter = @($appUri.runAfter.PSObject.Properties['List_partnerledger_app'].Value | Sort-Object)
if ($appList.inputs.parameters.entityName -cne 'canvasapps' -or
    $appList.inputs.parameters.'$filter' -cne "name eq 'pl_partnerledger_b51b4'" -or
    ($appUriRunAfter -join '|') -cne 'Failed|Skipped|Succeeded|TimedOut' -or
    $appLink.inputs -notmatch "requestId=" -or
    $appLink.inputs -notmatch "&sourcetime=" -or
    $startApproval.runAfter.PSObject.Properties['Compose_app_link'].Value[0] -cne 'Succeeded' -or
    $startParams.'WebhookApprovalCreationInput/itemLink' -cne "@if(empty(outputs('Compose_app_link')), 'https://apps.powerapps.com/', outputs('Compose_app_link'))" -or
    [string]::IsNullOrWhiteSpace([string]$startParams.'WebhookApprovalCreationInput/itemLinkDescription') -or
    $startParams.'WebhookApprovalCreationInput/details' -notmatch [regex]::Escape("body('Get_submission_version')?['pl_changesummary']") -or
    $startParams.'WebhookApprovalCreationInput/details' -notmatch [regex]::Escape("_pl_requestinguserlookup_value@OData.Community.Display.V1.FormattedValue") -or
    # 2026-09-30（ユーザー決定「照合キーは消そう」）：照合は同じ実行の中で行うので、本文に内部のキーを載せない。
    $startParams.'WebhookApprovalCreationInput/details' -match '照合キー' -or
    $startParams.'WebhookApprovalCreationInput/details' -match 'pl_externalrequestkey' -or
    $bridgeActions.Get_submission_version.inputs.parameters.'$select' -notmatch 'pl_changesummary' -or
    # 件名は、サーバーが申請の種類と対象の名前から作った提出版のpl_approvaltitle（旧データは申請名）。
    $bridgeActions.Get_submission_version.inputs.parameters.'$select' -notmatch 'pl_approvaltitle' -or
    $startParams.'WebhookApprovalCreationInput/title' -cne "PartnerLedger 承認: @{if(empty(body('Get_submission_version')?['pl_approvaltitle']), body('Get_request')?['pl_name'], body('Get_submission_version')?['pl_approvaltitle'])}" -or
    $bridgeActions.Get_request.inputs.parameters.'$select' -notmatch '_pl_requestinguserlookup_value') {
    throw 'Approval Bridge must show the server-built change summary, the requester and a link to the request, and must not block the approval when the app link cannot be read.'
}
# PL-034：応答後に申請を読み直し、PartnerLedgerで取消済みなら結果を書かずに成功で終える。
$cancelledTerms = @($cancelledGuard.expression.equals)
if ($cancelledGuard.type -cne 'If' -or
    $requestAfterResponse.inputs.parameters.entityName -cne 'pl_requests' -or
    $requestAfterResponse.inputs.parameters.recordId -cne "@body('Get_approval_link')?['_pl_requestlookup_value']" -or
    $cancelledTerms[0] -cne "@body('Get_request_after_response')?['pl_requeststatuscode']" -or
    $cancelledTerms[1] -cne '取消' -or
    ((ConvertTo-Json -Depth 20 $cancelledGuard.actions) -match 'UpdateOnlyRecord')) {
    throw 'Approval Bridge must re-read the request after the response and must not write a result for a cancelled request.'
}
# PL-035：承認は21日でタイムアウトし、タイムアウトした場合だけ、提出中の申請を理由付きで取消へ更新する。
$requestAfterTimeout = $bridgeActions.Get_request_after_timeout
$expiryGuard = $bridgeActions.If_request_still_submitted
if ($null -eq $requestAfterTimeout -or $null -eq $expiryGuard -or $null -eq $expiryGuard.actions.Cancel_expired_request) {
    throw 'Approval Bridge must time out after 21 days and cancel only a still-submitted request, with a reason, through the standard request update.'
}
$expiryTerms = @($expiryGuard.expression.equals)
$expiryCancel = $expiryGuard.actions.Cancel_expired_request
$afterTimeoutStatuses = @($requestAfterTimeout.runAfter.PSObject.Properties['Start_group_approval'].Value)
if ($startApproval.limit.timeout -cne 'P21D' -or
    $afterTimeoutStatuses.Count -ne 1 -or $afterTimeoutStatuses[0] -cne 'TimedOut' -or
    $requestAfterTimeout.inputs.parameters.entityName -cne 'pl_requests' -or
    $requestAfterTimeout.inputs.parameters.recordId -cne "@body('Get_approval_link')?['_pl_requestlookup_value']" -or
    $expiryGuard.runAfter.PSObject.Properties['Get_request_after_timeout'].Value[0] -cne 'Succeeded' -or
    $expiryTerms[0] -cne "@body('Get_request_after_timeout')?['pl_requeststatuscode']" -or
    $expiryTerms[1] -cne '提出中' -or
    $expiryCancel.inputs.host.operationId -cne 'UpdateOnlyRecord' -or
    $expiryCancel.inputs.parameters.entityName -cne 'pl_requests' -or
    $expiryCancel.inputs.parameters.recordId -cne "@body('Get_approval_link')?['_pl_requestlookup_value']" -or
    $expiryCancel.inputs.parameters.item.pl_requeststatuscode -cne '取消' -or
    [string]::IsNullOrWhiteSpace([string]$expiryCancel.inputs.parameters.item.pl_cancellationreason) -or
    @($expiryCancel.inputs.parameters.item.PSObject.Properties).Count -ne 2 -or
    $expiryGuard.actions.End_after_expiry_cancel.type -cne 'Terminate' -or
    $expiryGuard.actions.End_after_expiry_cancel.inputs.runStatus -cne 'Succeeded' -or
    $expiryGuard.actions.End_after_expiry_cancel.runAfter.PSObject.Properties['Cancel_expired_request'].Value[0] -cne 'Succeeded' -or
    ((ConvertTo-Json -Depth 20 $expiryGuard.else) -match 'UpdateOnlyRecord')) {
    throw 'Approval Bridge must time out after 21 days and cancel only a still-submitted request, with a reason, through the standard request update.'
}
$ifApproved = $cancelledGuard.else.actions.If_approved
if ($ifApproved.expression.equals[0] -cne "@body('Start_group_approval')?['outcome']" -or
    $ifApproved.expression.equals[1] -cne 'Approve' -or
    $ifApproved.else.actions.If_rejected.expression.equals[0] -cne "@body('Start_group_approval')?['outcome']" -or
    $ifApproved.else.actions.If_rejected.expression.equals[1] -cne 'Reject') {
    throw 'Approval Bridge Approve and Reject branches must continue to consume the same single group Approval response.'
}
# 2026-09-26（A＋C）：承認結果の書込みが成功した後に申請を読み直し、反映失敗なら申請者へメールする。
# 書込み（初回または再試行）が成功したときだけ読み直し、それ以外の申請状態では何も送らない。
$requestAfterApproval = $ifApproved.actions.Get_request_after_approval
$reflectionFailedGuard = $ifApproved.actions.If_reflection_failed
if ($null -eq $requestAfterApproval -or $null -eq $reflectionFailedGuard -or
    $null -eq $reflectionFailedGuard.actions.PSObject.Properties['Get_requester'] -or
    $null -eq $reflectionFailedGuard.actions.PSObject.Properties['Notify_requester_reflection_failed']) {
    throw 'Approval Bridge must re-read the request after the approval result is written and email the requester only when the reflection failed.'
}
$requester = $reflectionFailedGuard.actions.Get_requester
$notify = $reflectionFailedGuard.actions.Notify_requester_reflection_failed
$afterApprovalStatuses = @($requestAfterApproval.runAfter.PSObject.Properties['Retry_update_link_as_approved'].Value | Sort-Object)
$reflectionTerms = @($reflectionFailedGuard.expression.equals)
$outlookRef = $approvalFlow.properties.connectionReferences.shared_office365
if (@($requestAfterApproval.runAfter.PSObject.Properties).Count -ne 1 -or
    ($afterApprovalStatuses -join '|') -cne 'Skipped|Succeeded' -or
    $requestAfterApproval.inputs.host.operationId -cne 'GetItem' -or
    $requestAfterApproval.inputs.parameters.entityName -cne 'pl_requests' -or
    $requestAfterApproval.inputs.parameters.recordId -cne "@body('Get_approval_link')?['_pl_requestlookup_value']" -or
    $reflectionFailedGuard.runAfter.PSObject.Properties['Get_request_after_approval'].Value[0] -cne 'Succeeded' -or
    $reflectionTerms[0] -cne "@body('Get_request_after_approval')?['pl_requeststatuscode']" -or
    $reflectionTerms[1] -cne '反映失敗' -or
    $requester.inputs.parameters.entityName -cne 'systemusers' -or
    $requester.inputs.parameters.recordId -cne "@body('Get_request_after_approval')?['_pl_requestinguserlookup_value']" -or
    $notify.inputs.host.connectionName -cne 'shared_office365' -or
    $notify.inputs.host.operationId -cne 'SendEmailV2' -or
    $notify.inputs.parameters.'emailMessage/To' -cne "@body('Get_requester')?['internalemailaddress']" -or
    # 本文はHTML（コネクタ定義でformat: html）。改行は<br>で表し、申請名など利用者の入力は&・<・>を置き換えて埋め込む。
    $notify.inputs.parameters.'emailMessage/Body' -notmatch '<br>' -or
    $notify.inputs.parameters.'emailMessage/Body' -match '%0D%0A' -or
    $notify.inputs.parameters.'emailMessage/Body' -notmatch "replace\(replace\(replace\(coalesce\(body\('Get_request_after_approval'\)\?\['pl_name'\]" -or
    $notify.inputs.parameters.'emailMessage/Body' -notmatch [regex]::Escape("outputs('Compose_app_link')") -or
    $notify.runAfter.PSObject.Properties['Get_requester'].Value[0] -cne 'Succeeded' -or
    ($null -ne $reflectionFailedGuard.PSObject.Properties['else'] -and @($reflectionFailedGuard.else.actions.PSObject.Properties).Count -ne 0) -or
    ((ConvertTo-Json -Depth 20 $reflectionFailedGuard) -match 'UpdateOnlyRecord') -or
    $outlookRef.connection.connectionReferenceLogicalName -cne 'pl_office365outlook' -or
    $outlookRef.api.name -cne 'shared_office365') {
    throw 'Approval Bridge must re-read the request after the approval result is written and email the requester only when the reflection failed.'
}
# 移送元の環境のメールアドレスをフローの定義に残さない（宛先は環境変数・接続・Dataverseから読む）。
# odata.bind・注釈（@OData.Community...）はメールアドレスではないので除く。見つかった値そのものは出さない。
foreach ($flowFile in Get-ChildItem -LiteralPath (Join-Path $solutionRoot 'Workflows') -Filter '*.json' -File) {
    $flowText = Get-Content -Raw -Encoding UTF8 -LiteralPath $flowFile.FullName
    if ($flowText -match '(?i)[a-z0-9._%+-]+@(?!odata\.)[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}') {
        throw "Flow $($flowFile.Name) must not contain a literal email address."
    }
}

$canvasMetaPath = Join-Path $solutionRoot 'CanvasApps\pl_partnerledger_b51b4.meta.xml'
$canvasMeta = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $canvasMetaPath)
$canvasPackageRoot = Join-Path $solutionRoot 'CanvasApps\pl_partnerledger_b51b4_CodeAppPackages'
foreach ($uri in $canvasMeta.CanvasApp.CodeAppPackageUris.CodeAppPackageUri) {
    $relativePath = ($uri -split '_ContentType_', 2)[0].TrimStart('/').Replace('/', '\')
    $packagePath = Join-Path $solutionRoot $relativePath
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Code App package URI does not resolve to a file: $uri"
    }
}

$indexText = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $canvasPackageRoot 'index.html')
foreach ($asset in [regex]::Matches($indexText, '(?:src|href)="\./([^\"]+)"')) {
    $assetPath = Join-Path $canvasPackageRoot $asset.Groups[1].Value.Replace('/', '\')
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        throw "Code App index.html references a missing asset: $($asset.Groups[1].Value)"
    }
}

$stepIds = @($stepFiles | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name).Trim('{}').ToLowerInvariant() })
$rootStepIds = @($rootComponents | Where-Object { $_.type -eq '92' } | ForEach-Object {
    ([string]$_.id).Trim('{}').ToLowerInvariant()
})
$unresolvedStepIds = @($rootComponents | Where-Object {
    $_.type -eq '92' -and $stepIds -notcontains ([string]$_.id).Trim('{}').ToLowerInvariant()
} | ForEach-Object { ([string]$_.id).Trim('{}').ToLowerInvariant() })
if ($unresolvedStepIds.Count -gt 0) {
    throw "SDK Step root components lack source XML: $($unresolvedStepIds -join ', ')"
}
$unreferencedOcrStepFiles = @(
    foreach ($stepFile in $stepFiles) {
        $stepDocument = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $stepFile.FullName)
        $stepId = [IO.Path]::GetFileNameWithoutExtension($stepFile.Name).Trim('{}').ToLowerInvariant()
        if ($stepDocument.SdkMessageProcessingStep.Name -in $expectedOcrStepNames -and
            $rootStepIds -notcontains $stepId) {
            $stepFile
        }
    }
)
if ($unreferencedOcrStepFiles.Count -gt 0) {
    throw "OCR Job/Owner Step source files are not Solution root components: $($unreferencedOcrStepFiles.Name -join ', ')"
}
$deliveryFlowPath = Join-Path $solutionRoot 'Workflows\PartnerLedger_NotificationDaily_v1_Delivery_v2-ED0B072C-CDB5-F111-AAAD-E4FB1EFF79C7.json'
$deliveryFlow = Get-Content -Raw -Encoding UTF8 -LiteralPath $deliveryFlowPath | ConvertFrom-Json
# 2026-09-27：日次通知メールは、内部の管理番号ではなく業務の言葉で書く。本文はHTML（コネクタ定義でformat: html）なので
# <br>で改行し、契約名など差し込む値は&・<・>を置き換える。名前の読込みは宛先の確保（Claim）より前に行い、
# 読めなくても送信の流れ（確保→送信→送信済み／結果不明）を壊さない。
$deliveryActions = $deliveryFlow.properties.definition.actions
$deliveryLoop = $deliveryActions.ForEach_NotificationCandidate.actions
$deliverySend = $deliveryLoop.Send_Email
$noticeBody = $deliveryLoop.Compose_NoticeBody
$noticeSubject = $deliveryLoop.Compose_NoticeSubject
$noticeFacts = $deliveryLoop.Compose_NoticeFacts
$noticeContract = $deliveryLoop.If_Notice_Contract
if ($null -eq $noticeBody -or $null -eq $noticeSubject -or $null -eq $noticeFacts -or $null -eq $noticeContract -or
    $null -eq $deliveryActions.List_partnerledger_app -or $null -eq $deliveryLoop.Compose_LeaseToken.runAfter.PSObject.Properties['Compose_NoticeBody']) {
    throw 'PL-010 email must be built from business facts before the notification is claimed.'
}
$factsRunAfter = @($noticeFacts.runAfter.PSObject.Properties['If_Notice_Contract'].Value | Sort-Object)
$noticeBodyText = [string]$noticeBody.inputs
if ($deliverySend.inputs.parameters.'emailMessage/Body' -cne "@outputs('Compose_NoticeBody')" -or
    $deliverySend.inputs.parameters.'emailMessage/Subject' -cne "@outputs('Compose_NoticeSubject')" -or
    $deliverySend.runAfter.PSObject.Properties['Claim_Notification'].Value[0] -cne 'Succeeded' -or
    $noticeBodyText -notmatch '<br>' -or
    $noticeBodyText -match '%0D%0A' -or
    $noticeBodyText -match 'PL-010' -or
    $noticeBodyText -notmatch [regex]::Escape("replace(replace(replace(coalesce(outputs('Compose_NoticeFacts')?['contractName'], ''), '&', '&amp;'), '<', '&lt;'), '>', '&gt;')") -or
    ([string]$noticeSubject.inputs) -match 'pl_name' -or
    ($factsRunAfter -join '|') -cne 'Failed|Skipped|Succeeded|TimedOut' -or
    $noticeContract.actions.Get_Notice_Contract.inputs.parameters.entityName -cne 'pl_contracts' -or
    $deliveryActions.List_NotificationCandidates.inputs.parameters.'$select' -notmatch 'pl_targetdueat' -or
    # 契約の通知は取引先の「契約・期限」タブ、名刺の通知はその名刺の確認画面へ直接開くリンクにする。
    ([string]$noticeFacts.inputs.link) -notmatch 'partnerId=' -or
    ([string]$noticeFacts.inputs.link) -notmatch 'partnerTab=contracts' -or
    ([string]$noticeFacts.inputs.link) -notmatch 'captureId=') {
    throw 'PL-010 email must use plain business wording with HTML line breaks and escaped values, and must not block the claim-send-mark sequence.'
}

# 2026-09-26 モバイルCanvasアプリは廃止し、スマホもPC Code Appをブラウザで使う。
# 旧アプリ・その画像アップロードFlow・ソースが移送物や正本へ戻らないようにする。
$retiredMobileSource = Join-Path $PSScriptRoot '..\apps\mobile'
if (Test-Path -LiteralPath $retiredMobileSource) {
    throw 'The retired mobile Canvas app source (apps/mobile) must not remain in the repository.'
}
if (@(Get-ChildItem -LiteralPath (Join-Path $solutionRoot 'CanvasApps') -File | Where-Object { $_.Name -like 'cr46d_partnerledgermobile_*' }).Count -gt 0 -or
    @($rootComponents | Where-Object { $_.type -eq '300' -and $_.schemaName -ieq 'cr46d_partnerledgermobile_793a8' }).Count -gt 0) {
    throw 'The retired mobile Canvas app must not remain in the portable solution.'
}
if (@(Get-ChildItem -LiteralPath (Join-Path $solutionRoot 'Workflows') -File | Where-Object { $_.Name -like 'PartnerLedger_MobileCardImageUpload_*' }).Count -gt 0 -or
    @($rootComponents | Where-Object { $_.type -eq '29' -and $_.id -ieq '{576adf4c-35b4-f111-aaad-e4fb1eff79c7}' }).Count -gt 0) {
    throw 'The retired mobile image upload flow must not remain in the portable solution.'
}

# 2026-09-26：DecisionFlowとの共有を断つ。フローが使う接続参照はPartnerLedger専用のものだけ（2026-09-30にOffice 365 Groupsを追加して4つ）。
$customizations = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $solutionRoot 'Other\Customizations.xml'))
$connectionReferenceNames = @($customizations.ImportExportXml.connectionreferences.connectionreference | ForEach-Object { [string]$_.connectionreferencelogicalname } | Sort-Object)
if (($connectionReferenceNames -join '|') -cne 'pl_approvals|pl_dataverse|pl_office365groups|pl_office365outlook') {
    throw "The solution must carry only the PartnerLedger connection references. Actual=[$($connectionReferenceNames -join ', ')]"
}
$groupsReference = @($customizations.ImportExportXml.connectionreferences.connectionreference | Where-Object { [string]$_.connectionreferencelogicalname -ceq 'pl_office365groups' })
if ($groupsReference.Count -ne 1 -or [string]$groupsReference[0].connectorid -cne '/providers/Microsoft.PowerApps/apis/shared_office365groups') {
    throw 'The pl_office365groups connection reference must point to the Office 365 Groups connector.'
}
foreach ($flowFile in Get-ChildItem -LiteralPath (Join-Path $solutionRoot 'Workflows') -Filter '*.json' -File) {
    $flowRefs = (Get-Content -Raw -Encoding UTF8 -LiteralPath $flowFile.FullName | ConvertFrom-Json).properties.connectionReferences
    foreach ($ref in $flowRefs.PSObject.Properties) {
        $logicalName = [string]$ref.Value.connection.connectionReferenceLogicalName
        if ($connectionReferenceNames -notcontains $logicalName) {
            throw "Flow $($flowFile.Name) uses a connection reference outside the solution: $logicalName"
        }
    }
}
$decisionFlowHits = @(Get-ChildItem -LiteralPath $solutionRoot -Recurse -File -Include '*.xml', '*.json' |
    Select-String -Pattern 'DecisionFlow', 'ds_shared_', 'ds_application' -SimpleMatch -List)
if ($decisionFlowHits.Count -gt 0) {
    throw "The solution must not reference DecisionFlow: $(@($decisionFlowHits | ForEach-Object { $_.Path }) -join ', ')"
}

# 2026-09-27（監査指摘1）：承認設定の版番号に一意キーが無いと、アプリの同時保存で同じ版の有効な行が2つでき、
# 全員の提出が止まる。キーはSolutionに含めて運ぶ。
$settingsEntity = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $solutionRoot 'Entities\pl_Settings\Entity.xml'))
$settingsKeys = @($settingsEntity.SelectNodes('//EntityKey') | Where-Object {
    @($_.EntityKeyAttributes.AttributeName) -contains 'pl_settingsversion' -and @($_.EntityKeyAttributes.AttributeName).Count -eq 1
})
if ($settingsKeys.Count -ne 1) {
    throw 'pl_Settings must carry exactly one alternate key on pl_settingsversion.'
}

Write-Output "PASS: $($expectedNames.Count) required text definitions, no embedded values, no retired execution-user definitions/Reader Role, OCR legacy registration guard, Approval Bridge environment recipient and fail-closed guard, PL-010 email line breaks, neutral Plugin Step configuration, retired mobile Canvas app/image flow absent, all $($stepFiles.Count) SDK Step roots resolve, four people-facing roles with the approval-cancel contract, four OCR/Owner Step-to-PluginType links resolve, PartnerLedger-only connection references with no DecisionFlow reference, the approval settings version key, and Code App assets resolve."
