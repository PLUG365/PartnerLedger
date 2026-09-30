<#
.SYNOPSIS
PartnerLedgerのPlugin Stepに残っている実行ユーザー（Run in User's Context）を外す。

.DESCRIPTION
2026-09-27から、Pluginの内部処理はSYSTEMの権限で行い、Stepの実行ユーザーを使わない
（開発記録（Git管理外）の実装計画「サーバー処理をSYSTEMの権限で行い、実行ユーザーの設定を無くす」）。
実行ユーザーが残っていると、そのユーザーが無効化されたときに処理が止まるため外す。
新しくインポートした環境では何もすることはない（確認すると0個になる）。以前の版で実行ユーザーを設定した環境だけが対象。
対象は、リポジトリのSolutionに入っているStepすべて。既定は確認だけ（書き込まない）。`-Apply`を付けたときだけ外す。
ユーザーIDや氏名は表示しない。

.EXAMPLE
pwsh ./scripts/clear-plugin-run-as.ps1 -OrgUrl https://<org>.crm.dynamics.com/
pwsh ./scripts/clear-plugin-run-as.ps1 -OrgUrl https://<org>.crm.dynamics.com/ -Apply
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OrgUrl,

    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
$OrgUrl = $OrgUrl.TrimEnd('/') + '/'
$api = $OrgUrl + 'api/data/v9.2/'

$token = (az account get-access-token --resource $OrgUrl --query accessToken -o tsv)
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Azure CLIからDataverseアクセストークンを取得できませんでした（az login を確認してください）。'
}
$headers = @{
    Authorization = "Bearer $token"
    Accept = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

function Invoke-Dv([string] $Method, [string] $Path) {
    Invoke-RestMethod -Method $Method -Uri ($api + $Path) -Headers $headers
}

$stepDirectory = Join-Path $PSScriptRoot '..\solutions\PartnerLedger\SdkMessageProcessingSteps'
$steps = @(Get-ChildItem -LiteralPath $stepDirectory -Filter '*.xml' -File | ForEach-Object {
    $step = ([xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName)).SdkMessageProcessingStep
    [pscustomobject]@{ Id = ([string]$step.SdkMessageProcessingStepId).Trim('{}'); Name = [string]$step.Name }
} | Sort-Object Name)
if ($steps.Count -eq 0) {
    throw 'SolutionにStepが見つかりません。リポジトリの場所を確認してください。'
}

$remaining = 0
foreach ($step in $steps) {
    $current = Invoke-Dv GET "sdkmessageprocessingsteps($($step.Id))?`$select=_impersonatinguserid_value"
    if ([string]::IsNullOrWhiteSpace([string]$current._impersonatinguserid_value)) {
        continue
    }
    $remaining++
    if (-not $Apply) {
        Write-Output ("[外す予定] {0}" -f $step.Name)
        continue
    }
    Invoke-Dv DELETE "sdkmessageprocessingsteps($($step.Id))/impersonatinguserid/`$ref" | Out-Null
    $after = Invoke-Dv GET "sdkmessageprocessingsteps($($step.Id))?`$select=_impersonatinguserid_value"
    if (-not [string]::IsNullOrWhiteSpace([string]$after._impersonatinguserid_value)) {
        throw "外した後の読み戻しで実行ユーザーが残っています: $($step.Name)"
    }
    Write-Output ("[外した] {0}" -f $step.Name)
}

if ($remaining -eq 0) {
    Write-Output ("{0}個のStepすべて、実行ユーザーは設定されていません。" -f $steps.Count)
}
elseif (-not $Apply) {
    Write-Output "確認のみ：$remaining 個のStepに実行ユーザーが残っています。外すには -Apply を付けて再実行してください。"
}
else {
    Write-Output "$remaining 個のStepから実行ユーザーを外しました。"
}
