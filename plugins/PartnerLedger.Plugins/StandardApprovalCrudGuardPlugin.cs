using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準 pl_Request／pl_SubmissionVersion CRUD の同期ガード。
    ///
    /// Custom APIの代わりに生成された標準Create／Updateを入口にするが、画面の
    /// allow-listだけでは直接Web API呼出しを防げないため、申請者・下書き状態・
    /// 関連申請を呼出し時点のDataverseから再確認する。承認結果、版固定、外部連携、
    /// 対象行への反映は別の同期処理へ分離し、このPluginでは扱わない。
    /// </summary>
    public sealed class StandardApprovalCrudGuardPlugin : PluginBase
    {
        public const string RequestPreImageAlias = "StandardApprovalRequestPreImage";
        public const string SubmissionVersionPreImageAlias = "StandardApprovalSubmissionVersionPreImage";
        public const string TrustedInternalWriteSharedVariable = "PartnerLedger.StandardApprovalCrudGuard.TrustedInternalWrite";

        private static readonly ISet<string> TrustedSubmissionVersionUpdateAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "pl_submissionstatuscode",
            "pl_submittedbylookup",
            "pl_submittedat",
            "pl_policyversion",
            "pl_rowversiontoken",
            ApprovalChangeSummary.AttributeName,
            ApprovalChangeSummary.TitleAttributeName,
            "pl_versionnumber",
        };

        public StandardApprovalCrudGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(StandardApprovalCrudGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            var service = localPluginContext.InitiatingUserService;

            if (string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase))
            {
                ValidateAndApplyCreate(target, context.PrimaryEntityName, context.InitiatingUserId, service);
                return;
            }

            if (string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase))
            {
                if (HasTrustedDecisionInternalWrite(context))
                {
                    ValidateTrustedDecisionInternalUpdate(target, context.PrimaryEntityName);
                    return;
                }
                if (HasTrustedInternalWrite(context))
                {
                    ValidateTrustedInternalUpdate(target, context.PrimaryEntityName);
                    return;
                }

                var preImage = ResolvePreImage(context, target);
                ValidateUpdate(target, context.PrimaryEntityName, preImage, context.InitiatingUserId, service);
                return;
            }

            throw new InvalidPluginExecutionException("承認基盤の標準CRUDガードへ未対応のMessageが渡されました。");
        }

        /// <summary>
        /// 標準提出Pluginが同一パイプライン内で作成する提出版の権威更新だけを許可する。
        /// SharedVariablesはWeb API入力では設定できない。ネスト実行でこのマーカーが
        /// 見えない場合は、専用ユーザーやDepthによるフォールバックを行わず拒否する。
        /// </summary>
        public static void ValidateTrustedInternalUpdate(Entity? target, string primaryEntityName)
        {
            EnsurePrimaryEntity(target, primaryEntityName);
            if (!string.Equals(primaryEntityName, StandardApprovalCrudContract.SubmissionVersionEntityName, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("承認基盤の内部Update対象テーブルが不正です。");
            }

            foreach (var attributeName in target!.Attributes.Keys)
            {
                if (attributeName == StandardApprovalCrudContract.SubmissionVersionPrimaryIdAttribute)
                    continue;
                if (!TrustedSubmissionVersionUpdateAttributes.Contains(attributeName))
                {
                    throw new InvalidPluginExecutionException(
                        "標準提出の内部Updateに許可されていない提出版列があります: " + attributeName);
                }
            }
        }

        /// <summary>
        /// 標準判定Pluginが確知済み結果から申請／提出版の状態を更新するための
        /// 専用ネスト境界。提出Pluginの内部更新とは経路を分け、反映経路では
        /// 反映待ち／反映済み／反映失敗と固定日時（反映失敗は理由）だけを許可する。
        /// </summary>
        public static void ValidateTrustedDecisionInternalUpdate(Entity? target, string primaryEntityName)
        {
            EnsurePrimaryEntity(target, primaryEntityName);

            if (string.Equals(primaryEntityName, StandardApprovalCrudContract.RequestEntityName, StringComparison.Ordinal))
            {
                var status = target!.GetAttributeValue<string>("pl_requeststatuscode");
                if (IsDecisionRequestStatus(status))
                {
                    EnsureOnlyDecisionRequestStatusUpdate(target);
                    return;
                }
                if (IsReflectionRequestStatus(status))
                {
                    EnsureOnlyReflectionRequestStatusUpdate(target);
                    return;
                }
                throw new InvalidPluginExecutionException("標準判定の申請状態が不正です。");
            }

            if (string.Equals(primaryEntityName, StandardApprovalCrudContract.SubmissionVersionEntityName, StringComparison.Ordinal))
            {
                var status = target!.GetAttributeValue<string>("pl_submissionstatuscode");
                if (IsDecisionSubmissionVersionStatus(status))
                {
                    EnsureOnlyDecisionSubmissionVersionStatusUpdate(target);
                    return;
                }
                if (IsReflectionSubmissionVersionStatus(status))
                {
                    EnsureOnlyReflectionSubmissionVersionStatusUpdate(target);
                    return;
                }
                throw new InvalidPluginExecutionException("標準判定の提出版状態が不正です。");
            }

            throw new InvalidPluginExecutionException("標準判定の内部Update対象テーブルが不正です。");
        }

        private static bool IsReflectionRequestStatus(string? status)
            => string.Equals(status, ApprovalRequestStatus.反映待ち.ToString(), StringComparison.Ordinal)
               || string.Equals(status, ApprovalRequestStatus.反映済み.ToString(), StringComparison.Ordinal)
               || string.Equals(status, ApprovalRequestStatus.反映失敗.ToString(), StringComparison.Ordinal);

        private static bool IsReflectionSubmissionVersionStatus(string? status)
            => string.Equals(status, ApprovalSubmissionVersionStatus.反映待ち.ToString(), StringComparison.Ordinal)
               || string.Equals(status, ApprovalSubmissionVersionStatus.反映済み.ToString(), StringComparison.Ordinal)
               || string.Equals(status, ApprovalSubmissionVersionStatus.反映失敗.ToString(), StringComparison.Ordinal);

        public static bool HasTrustedDecisionInternalWrite(IPluginExecutionContext context)
        {
            for (var current = context; current != null; current = current.ParentContext)
            {
                if (current.SharedVariables != null
                    && current.SharedVariables.TryGetValue(
                        StandardApprovalDecisionPlugin.TrustedDecisionInternalWriteSharedVariable,
                        out var value)
                    && value is bool marked
                    && marked)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasTrustedInternalWrite(IPluginExecutionContext context)
        {
            for (var current = context; current != null; current = current.ParentContext)
            {
                if (current.SharedVariables != null
                    && current.SharedVariables.TryGetValue(TrustedInternalWriteSharedVariable, out var value)
                    && value is bool trusted
                    && trusted)
                {
                    return true;
                }
            }

            return false;
        }

        public static void ValidateAndApplyCreate(
            Entity? target,
            string primaryEntityName,
            Guid initiatingUserId,
            IOrganizationService initiatingUserService)
        {
            if (initiatingUserService == null) throw new ArgumentNullException(nameof(initiatingUserService));
            EnsurePrimaryEntity(target, primaryEntityName);
            EnsureInitiatingUser(initiatingUserId);

            if (string.Equals(primaryEntityName, StandardApprovalCrudContract.RequestEntityName, StringComparison.Ordinal))
            {
                var result = StandardApprovalCrudContract.ValidateRequestCreate(target);
                EnsureValid(result, "申請Create");
                var partnerReference = target!.GetAttributeValue<EntityReference>("pl_partnerlookup");
                EnsureActivePartner(initiatingUserService, partnerReference);
                var requestTypeCode = target.GetAttributeValue<string>("pl_requesttypecode");
                var contractReference = target.GetAttributeValue<EntityReference>("pl_contractlookup");
                var targetEntityName = contractReference == null
                    ? StandardApprovalCrudContract.PartnerEntityName
                    : StandardApprovalCrudContract.ContractEntityName;
                var targetId = contractReference?.Id ?? partnerReference!.Id;
                var activeKey = ApprovalActiveRequestKey.Build(targetEntityName, targetId, requestTypeCode!);
                EnsureNoActiveRequest(
                    initiatingUserService,
                    activeKey,
                    requestTypeCode!,
                    partnerReference!,
                    contractReference);
                target[ApprovalActiveRequestKey.AttributeName] = activeKey;

                // 申請者と初期状態はクライアントから受けず、Createの同期処理で固定する。
                target["pl_requestinguserlookup"] = new EntityReference("systemuser", initiatingUserId);
                target["pl_requeststatuscode"] = ApprovalRequestStatus.下書き.ToString();
                return;
            }

            if (string.Equals(primaryEntityName, StandardApprovalCrudContract.SubmissionVersionEntityName, StringComparison.Ordinal))
            {
                var result = StandardApprovalCrudContract.ValidateSubmissionVersionCreate(target);
                EnsureValid(result, "提出版Create");
                EnsureDraftRequestForUser(
                    initiatingUserService,
                    target!.GetAttributeValue<EntityReference>("pl_requestlookup"),
                    initiatingUserId);

                // 提出版もCreate時点では下書きだけを保存する。版番号・提出者・時刻・
                // 対象row versionは提出トランザクションでサーバーが確定する。
                target["pl_submissionstatuscode"] = ApprovalSubmissionVersionStatus.下書き.ToString();
                return;
            }

            throw new InvalidPluginExecutionException("承認基盤の標準Create対象テーブルが不正です。");
        }

        public static void ValidateUpdate(
            Entity? target,
            string primaryEntityName,
            Entity? preImage,
            Guid initiatingUserId,
            IOrganizationService initiatingUserService)
        {
            if (initiatingUserService == null) throw new ArgumentNullException(nameof(initiatingUserService));
            EnsurePrimaryEntity(target, primaryEntityName);
            EnsureInitiatingUser(initiatingUserId);
            if (preImage == null)
            {
                throw new InvalidPluginExecutionException(
                    "承認基盤の現在状態を確認できるPre Imageがないため、標準Updateを実行できません。");
            }

            if (string.Equals(primaryEntityName, StandardApprovalCrudContract.RequestEntityName, StringComparison.Ordinal))
            {
                var result = StandardApprovalCrudContract.ValidateRequestUpdate(target);
                EnsureValid(result, "申請Update");
                ValidateRequestUpdateState(target!, preImage, initiatingUserId);
                return;
            }

            if (string.Equals(primaryEntityName, StandardApprovalCrudContract.SubmissionVersionEntityName, StringComparison.Ordinal))
            {
                var result = StandardApprovalCrudContract.ValidateSubmissionVersionUpdate(target);
                EnsureValid(result, "提出版Update");
                ValidateSubmissionVersionUpdateState(
                    initiatingUserService,
                    target!,
                    preImage,
                    initiatingUserId);
                return;
            }

            throw new InvalidPluginExecutionException("承認基盤の標準Update対象テーブルが不正です。");
        }

        private static void ValidateRequestUpdateState(Entity target, Entity preImage, Guid initiatingUserId)
        {
            var currentStatus = ParseRequestStatus(preImage);

            var hasStatusTransition = target.Attributes.Contains("pl_requeststatuscode");
            if (hasStatusTransition)
            {
                var requestedStatus = target.GetAttributeValue<string>("pl_requeststatuscode");
                if (requestedStatus == ApprovalRequestStatus.取消.ToString())
                {
                    EnsureOnlyRequestCancellationUpdate(target);
                    // The PreOperation StandardApprovalSubmitPlugin re-reads the request and
                    // decides the path with ApprovalCancellationContract.Authorize: the
                    // requester withdraws a 下書き/差戻し request, an administrator cancels
                    // 提出中 (or withdraws an absent requester's request). Do not require the
                    // requester here, and do not judge the reason here either.
                    return;
                }

                EnsureRequestingUser(preImage, initiatingUserId);
                EnsureOnlyRequestStatusUpdate(target);
                if (requestedStatus == ApprovalRequestStatus.提出中.ToString()
                    && (currentStatus == ApprovalRequestStatus.下書き
                        || currentStatus == ApprovalRequestStatus.差戻し
                        || currentStatus == ApprovalRequestStatus.提出中))
                {
                    // 提出中へのUpdateは、同一の標準Updateパイプラインに登録した
                    // StandardApprovalSubmitPluginが版固定・Link・監査を完了する場合だけ
                    // 許可する。提出中からの再送はPluginの確定済み結果検査へ渡し、
                    // 取消やそれ以外の状態遷移は引き続きここで閉じる。
                    return;
                }

                throw new InvalidPluginExecutionException(
                    "申請の取消または許可されていない状態遷移は利用できません。"
                    + " 提出は標準提出Pluginが登録された同期経路だけで実行できます。");
            }

            if (target.Attributes.Contains("pl_cancellationreason"))
            {
                throw new InvalidPluginExecutionException(
                    "承認取消理由は状態を取消へ変更するときだけ指定できます。");
            }

            EnsureRequestingUser(preImage, initiatingUserId);

            if (currentStatus != ApprovalRequestStatus.下書き)
            {
                throw new InvalidPluginExecutionException(
                    "申請の内容は下書き状態でだけ編集できます。差戻し後は新しい提出版を作成してください。");
            }
        }

        private static void ValidateSubmissionVersionUpdateState(
            IOrganizationService service,
            Entity target,
            Entity preImage,
            Guid initiatingUserId)
        {
            var currentStatus = ParseSubmissionVersionStatus(preImage);
            if (currentStatus != ApprovalSubmissionVersionStatus.下書き)
            {
                throw new InvalidPluginExecutionException(
                    "提出済みまたは判定済みの提出版は変更できません。");
            }

            var requestReference = preImage.GetAttributeValue<EntityReference>("pl_requestlookup");
            EnsureDraftRequestForUser(service, requestReference, initiatingUserId);
        }

        private static Entity ResolvePreImage(IPluginExecutionContext context, Entity? target)
        {
            var alias = string.Equals(
                target?.LogicalName,
                StandardApprovalCrudContract.RequestEntityName,
                StringComparison.Ordinal)
                ? RequestPreImageAlias
                : SubmissionVersionPreImageAlias;

            if (context.PreEntityImages != null && context.PreEntityImages.Contains(alias))
            {
                return context.PreEntityImages[alias];
            }

            throw new InvalidPluginExecutionException(
                "承認基盤の標準Updateに必要なPre Imageが登録されていません: " + alias);
        }

        private static void EnsurePrimaryEntity(Entity? target, string primaryEntityName)
        {
            if (target == null)
                throw new InvalidPluginExecutionException("承認基盤の標準CRUD Targetがありません。");
            if (!string.Equals(target.LogicalName, primaryEntityName, StringComparison.Ordinal))
                throw new InvalidPluginExecutionException("標準CRUDのTargetとPrimaryEntityNameが一致しません。");
        }

        private static void EnsureInitiatingUser(Guid initiatingUserId)
        {
            if (initiatingUserId == Guid.Empty)
                throw new InvalidPluginExecutionException("標準CRUDの呼出し元ユーザーを解決できません。");
        }

        private static void EnsureValid(StandardApprovalCrudResult result, string operation)
        {
            if (!result.IsValid)
            {
                throw new InvalidPluginExecutionException(
                    operation + "の入力が不正です: " + result.Error);
            }
        }

        private static void EnsureActivePartner(IOrganizationService service, EntityReference? partnerReference)
        {
            if (partnerReference == null
                || partnerReference.Id == Guid.Empty
                || !string.Equals(partnerReference.LogicalName, StandardApprovalCrudContract.PartnerEntityName, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("申請対象の取引先Lookupが不正です。");
            }

            Entity partner;
            try
            {
                partner = service.Retrieve(
                    StandardApprovalCrudContract.PartnerEntityName,
                    partnerReference.Id,
                    new ColumnSet("statecode"));
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(
                    "申請対象の取引先を読み取れないため、申請を作成できません。",
                    exception);
            }

            if (GetOptionValue(partner, "statecode") != 0)
            {
                throw new InvalidPluginExecutionException("非アクティブな取引先へ申請できません。");
            }
        }

        private static void EnsureNoActiveRequest(
            IOrganizationService service,
            string activeKey,
            string requestTypeCode,
            EntityReference partnerReference,
            EntityReference? contractReference)
        {
            var query = new QueryExpression(StandardApprovalCrudContract.RequestEntityName)
            {
                ColumnSet = new ColumnSet(
                    StandardApprovalCrudContract.RequestPrimaryIdAttribute,
                    "pl_requeststatuscode",
                    ApprovalActiveRequestKey.AttributeName),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("pl_requesttypecode", ConditionOperator.Equal, requestTypeCode.Trim());

            var targetFilter = new FilterExpression(LogicalOperator.Or);
            targetFilter.AddCondition(ApprovalActiveRequestKey.AttributeName, ConditionOperator.Equal, activeKey);

            // The legacy fallback must keep the target predicates together.  Adding
            // partner and contract-null conditions directly to this OR filter would
            // match an unrelated request whenever its contract lookup is empty.
            var legacyTargetFilter = new FilterExpression(LogicalOperator.And);
            if (contractReference != null)
            {
                legacyTargetFilter.AddCondition("pl_contractlookup", ConditionOperator.Equal, contractReference.Id);
            }
            else
            {
                legacyTargetFilter.AddCondition("pl_partnerlookup", ConditionOperator.Equal, partnerReference.Id);
                legacyTargetFilter.AddCondition("pl_contractlookup", ConditionOperator.Null);
            }
            targetFilter.AddFilter(legacyTargetFilter);
            query.Criteria.AddFilter(targetFilter);

            EntityCollection existing;
            try
            {
                existing = service.RetrieveMultiple(query);
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(
                    "同一対象の承認中申請を確認できないため、申請を作成できません。",
                    exception);
            }

            foreach (var row in existing.Entities)
            {
                var statusText = row.GetAttributeValue<string>("pl_requeststatuscode");
                if (!Enum.TryParse(statusText, false, out ApprovalRequestStatus status)
                    || !Enum.IsDefined(typeof(ApprovalRequestStatus), status))
                {
                    throw new InvalidPluginExecutionException(
                        "同一対象の既存申請の状態を確認できないため、申請を作成できません。");
                }

                if (ApprovalActiveRequestKey.HoldsSlot(status))
                {
                    throw new InvalidPluginExecutionException(
                        "同じ対象・申請種別の承認中申請が既にあります。既存申請を確認してください。");
                }
            }
        }

        private static void EnsureDraftRequestForUser(
            IOrganizationService service,
            EntityReference? requestReference,
            Guid initiatingUserId)
        {
            if (requestReference == null
                || requestReference.Id == Guid.Empty
                || !string.Equals(requestReference.LogicalName, StandardApprovalCrudContract.RequestEntityName, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("提出版の申請Lookupが不正です。");
            }

            Entity request;
            try
            {
                request = service.Retrieve(
                    StandardApprovalCrudContract.RequestEntityName,
                    requestReference.Id,
                    new ColumnSet("pl_requestinguserlookup", "pl_requeststatuscode", "statecode"));
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(
                    "提出版に紐づく申請を読み取れないため、提出版を保存できません。",
                    exception);
            }

            if (GetOptionValue(request, "statecode") != 0)
                throw new InvalidPluginExecutionException("非アクティブな申請へ提出版を保存できません。");

            EnsureRequestingUser(request, initiatingUserId);
            var status = ParseRequestStatus(request);
            if (status != ApprovalRequestStatus.下書き && status != ApprovalRequestStatus.差戻し)
            {
                throw new InvalidPluginExecutionException(
                    "提出版を作成できるのは下書きまたは差戻し状態の申請だけです。");
            }
        }

        private static void EnsureRequestingUser(Entity request, Guid initiatingUserId)
        {
            var requester = request.GetAttributeValue<EntityReference>("pl_requestinguserlookup");
            if (requester == null
                || requester.Id == Guid.Empty
                || !string.Equals(requester.LogicalName, "systemuser", StringComparison.Ordinal)
                || requester.Id != initiatingUserId)
            {
                throw new InvalidPluginExecutionException(
                    "申請者本人以外は申請または提出版を変更できません。");
            }
        }

        private static void EnsureOnlyRequestStatusUpdate(Entity target)
        {
            foreach (var attributeName in target.Attributes.Keys)
            {
                if (attributeName == StandardApprovalCrudContract.RequestPrimaryIdAttribute
                    || attributeName == "pl_requeststatuscode")
                {
                    continue;
                }

                throw new InvalidPluginExecutionException(
                    "提出／取消Updateでは状態以外の申請列を同時に変更できません: " + attributeName);
            }
        }

        private static void EnsureOnlyRequestCancellationUpdate(Entity target)
        {
            foreach (var attributeName in target.Attributes.Keys)
            {
                if (attributeName == StandardApprovalCrudContract.RequestPrimaryIdAttribute
                    || attributeName == "pl_requeststatuscode"
                    || attributeName == "pl_cancellationreason")
                {
                    continue;
                }

                throw new InvalidPluginExecutionException(
                    "承認取消では状態と理由以外の申請列を同時に変更できません: " + attributeName);
            }
        }

        private static void EnsureOnlyDecisionRequestStatusUpdate(Entity target)
        {
            foreach (var attributeName in target.Attributes.Keys)
            {
                if (attributeName == StandardApprovalCrudContract.RequestPrimaryIdAttribute
                    || attributeName == "pl_requeststatuscode"
                    || IsActiveApprovalKeyClear(target, attributeName))
                {
                    continue;
                }

                throw new InvalidPluginExecutionException(
                    "標準判定の申請内部Updateでは状態以外の列を変更できません: " + attributeName);
            }
        }

        private static void EnsureOnlyDecisionSubmissionVersionStatusUpdate(Entity target)
        {
            foreach (var attributeName in target.Attributes.Keys)
            {
                if (attributeName == StandardApprovalCrudContract.SubmissionVersionPrimaryIdAttribute
                    || attributeName == "pl_submissionstatuscode")
                {
                    continue;
                }

                throw new InvalidPluginExecutionException(
                    "標準判定の提出版内部Updateでは状態以外の列を変更できません: " + attributeName);
            }
        }

        private static void EnsureOnlyReflectionRequestStatusUpdate(Entity target)
        {
            var isFailure = string.Equals(
                target.GetAttributeValue<string>("pl_requeststatuscode"),
                ApprovalRequestStatus.反映失敗.ToString(),
                StringComparison.Ordinal);
            foreach (var attributeName in target.Attributes.Keys)
            {
                if (attributeName == StandardApprovalCrudContract.RequestPrimaryIdAttribute
                    || attributeName == "pl_requeststatuscode"
                    || IsActiveApprovalKeyClear(target, attributeName))
                {
                    continue;
                }
                // 反映失敗だけは、申請者に見せる理由を同じ更新で保存する。
                if (isFailure
                    && attributeName == "pl_cancellationreason"
                    && target.Attributes[attributeName] is string reason
                    && reason.Trim().Length > 0
                    && reason.Length <= ApprovalCancellationContract.ReasonMaxLength)
                {
                    continue;
                }

                throw new InvalidPluginExecutionException(
                    "標準反映の申請内部Updateでは状態以外の列を変更できません: " + attributeName);
            }
        }

        private static bool IsActiveApprovalKeyClear(Entity target, string attributeName)
            => attributeName == ApprovalActiveRequestKey.AttributeName
               && target.Attributes[attributeName] == null;

        private static void EnsureOnlyReflectionSubmissionVersionStatusUpdate(Entity target)
        {
            var status = target.GetAttributeValue<string>("pl_submissionstatuscode");
            foreach (var attributeName in target.Attributes.Keys)
            {
                if (attributeName == StandardApprovalCrudContract.SubmissionVersionPrimaryIdAttribute
                    || attributeName == "pl_submissionstatuscode"
                    || attributeName == "pl_fixedat")
                {
                    continue;
                }

                throw new InvalidPluginExecutionException(
                    "標準反映の提出版内部Updateで許可されていない列があります: " + attributeName);
            }

            if ((string.Equals(status, ApprovalSubmissionVersionStatus.反映待ち.ToString(), StringComparison.Ordinal)
                    || string.Equals(status, ApprovalSubmissionVersionStatus.反映失敗.ToString(), StringComparison.Ordinal))
                && target.Attributes.Contains("pl_fixedat"))
            {
                throw new InvalidPluginExecutionException(
                    "反映待ち・反映失敗への提出版内部Updateで固定日時は設定できません。");
            }
            if (string.Equals(status, ApprovalSubmissionVersionStatus.反映済み.ToString(), StringComparison.Ordinal)
                && !target.Attributes.Contains("pl_fixedat"))
            {
                throw new InvalidPluginExecutionException(
                    "反映済みへの提出版内部Updateには固定日時が必要です。");
            }
        }

        private static bool IsDecisionRequestStatus(string? status)
            => string.Equals(status, ApprovalRequestStatus.承認済み.ToString(), StringComparison.Ordinal)
               || string.Equals(status, ApprovalRequestStatus.差戻し.ToString(), StringComparison.Ordinal)
               || string.Equals(status, ApprovalRequestStatus.却下.ToString(), StringComparison.Ordinal);

        private static bool IsDecisionSubmissionVersionStatus(string? status)
            => string.Equals(status, ApprovalSubmissionVersionStatus.承認済み.ToString(), StringComparison.Ordinal)
               || string.Equals(status, ApprovalSubmissionVersionStatus.差戻し.ToString(), StringComparison.Ordinal)
               || string.Equals(status, ApprovalSubmissionVersionStatus.却下.ToString(), StringComparison.Ordinal);

        private static ApprovalRequestStatus ParseRequestStatus(Entity entity)
        {
            var value = entity.GetAttributeValue<string>("pl_requeststatuscode");
            if (Enum.TryParse(value, false, out ApprovalRequestStatus status)
                && Enum.IsDefined(typeof(ApprovalRequestStatus), status))
            {
                return status;
            }

            throw new InvalidPluginExecutionException("申請の現在状態が不正です。");
        }

        private static ApprovalSubmissionVersionStatus ParseSubmissionVersionStatus(Entity entity)
        {
            var value = entity.GetAttributeValue<string>("pl_submissionstatuscode");
            if (Enum.TryParse(value, false, out ApprovalSubmissionVersionStatus status)
                && Enum.IsDefined(typeof(ApprovalSubmissionVersionStatus), status))
            {
                return status;
            }

            throw new InvalidPluginExecutionException("提出版の現在状態が不正です。");
        }

        private static int GetOptionValue(Entity entity, string attributeName)
        {
            var raw = entity.Attributes.Contains(attributeName) ? entity[attributeName] : null;
            if (raw is OptionSetValue optionSet) return optionSet.Value;
            if (raw is int integer) return integer;
            return -1;
        }
    }
}
