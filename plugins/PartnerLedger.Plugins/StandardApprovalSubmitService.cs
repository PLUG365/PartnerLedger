using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    public enum StandardApprovalSubmitError
    {
        None,
        InvalidInput,
        TargetRequired,
        TargetEntityMismatch,
        TargetIdRequired,
        TargetIdMismatch,
        UnexpectedAttribute,
        AuthorityAttributeForbidden,
        InvalidStatus,
        RequestNotFound,
        RequestStateRequired,
        RequestInactive,
        NotRequester,
        StateCannotSubmit,
        SubmissionVersionRequired,
        SubmissionVersionAmbiguous,
        SubmissionVersionNotFound,
        SubmissionVersionStateRequired,
        SubmissionVersionInactive,
        SubmissionVersionPairMismatch,
        SubmissionVersionStateInvalid,
        SubmissionVersionAuthorityPresent,
        SettingsInvalid,
        TargetInvalid,
        PolicyInvalid,
        ChangeSetInvalid,
        ApproverTeamRequired,
        ApproverTeamInvalid,
        AuthorityConflict,
        IdempotencyKeyConflict,
        ReplayDataInvalid,
        VersionNumberInvalid,
        /// <summary>新しい主担当が通常の有効な利用者でない（2026-09-29）。</summary>
        MainOwnerUnavailable,
        /// <summary>新しい主担当が今の主担当と同じ（2026-09-29）。</summary>
        MainOwnerUnchanged,
    }

    public sealed class StandardApprovalSubmitResult
    {
        private StandardApprovalSubmitResult(
            bool success,
            bool isReplay,
            StandardApprovalSubmitError errorCode)
        {
            Success = success;
            IsReplay = isReplay;
            ErrorCode = errorCode;
        }

        public bool Success { get; }
        public bool IsReplay { get; }
        public StandardApprovalSubmitError ErrorCode { get; }
        public Guid RequestId { get; internal set; }
        public Guid SubmissionVersionId { get; internal set; }
        public Guid ApprovalLinkId { get; internal set; }
        public ApprovalRequestStatus RequestStatus { get; internal set; }
        public ApprovalSubmissionVersionStatus SubmissionVersionStatus { get; internal set; }
        public ApprovalLinkStatus LinkStatus { get; internal set; }
        public string PolicyVersion { get; internal set; } = string.Empty;
        public string TargetRowVersion { get; internal set; } = string.Empty;
        public int VersionNumber { get; internal set; }
        public DateTime? SubmittedAtUtc { get; internal set; }
        public string IdempotencyKey { get; internal set; } = string.Empty;

        public static StandardApprovalSubmitResult Failure(
            Guid requestId,
            StandardApprovalSubmitError errorCode)
            => new StandardApprovalSubmitResult(false, false, errorCode)
            {
                RequestId = requestId,
            };

        private static StandardApprovalSubmitResult SuccessResult(
            Guid requestId,
            Guid submissionVersionId,
            Guid approvalLinkId,
            bool isReplay,
            ApprovalRequestStatus requestStatus,
            ApprovalSubmissionVersionStatus submissionVersionStatus,
            ApprovalLinkStatus linkStatus,
            string policyVersion,
            string targetRowVersion,
            int versionNumber,
            DateTime? submittedAtUtc,
            string idempotencyKey)
            => new StandardApprovalSubmitResult(true, isReplay, StandardApprovalSubmitError.None)
            {
                RequestId = requestId,
                SubmissionVersionId = submissionVersionId,
                ApprovalLinkId = approvalLinkId,
                RequestStatus = requestStatus,
                SubmissionVersionStatus = submissionVersionStatus,
                LinkStatus = linkStatus,
                PolicyVersion = policyVersion,
                TargetRowVersion = targetRowVersion,
                VersionNumber = versionNumber,
                SubmittedAtUtc = submittedAtUtc,
                IdempotencyKey = idempotencyKey,
            };

        internal static StandardApprovalSubmitResult Succeeded(
            Guid requestId,
            Guid submissionVersionId,
            Guid approvalLinkId,
            bool isReplay,
            ApprovalRequestStatus requestStatus,
            ApprovalSubmissionVersionStatus submissionVersionStatus,
            ApprovalLinkStatus linkStatus,
            string policyVersion,
            string targetRowVersion,
            int versionNumber,
            DateTime? submittedAtUtc,
            string idempotencyKey)
            => SuccessResult(
                requestId,
                submissionVersionId,
                approvalLinkId,
                isReplay,
                requestStatus,
                submissionVersionStatus,
                linkStatus,
                policyVersion,
                targetRowVersion,
                versionNumber,
                submittedAtUtc,
                idempotencyKey);
    }

    /// <summary>
    /// 標準 pl_Request Update（提出中）から呼び出す提出処理。
    ///
    /// このサービスはRequest自身を別Updateしない。PreOperationのTargetへ、サーバーが
    /// 解決した承認者Team・設定版・提出時刻を追加し、同じパイプラインの主Updateで保存する。
    /// そのため、子版・ApprovalLink・操作ログの作成に失敗した場合は、主Requestの保存も含めて
    /// Dataverseの同期トランザクションでロールバックできる。旧SubmitApproval Custom APIの
    /// 入力契約・指定ID・指定理由は再利用しない。
    /// </summary>
    public static class StandardApprovalSubmitService
    {
        public const string OperationCode = "SubmitApproval";
        public const string ApprovalLinkEntityName = "pl_approvallink";

        private const string RequestEntityName = "pl_request";
        private const string SubmissionVersionEntityName = "pl_submissionversion";
        private const string TeamEntityName = "team";

        public static StandardApprovalSubmitResult Submit(
            IOrganizationService service,
            Entity requestTarget,
            Guid initiatingUserId,
            Guid approverTeamId,
            DateTime submittedAtUtc)
            => Submit(
                service,
                requestTarget,
                requestTarget,
                initiatingUserId,
                approverTeamId,
                submittedAtUtc);

        /// <summary>
        /// 入力検証用のTargetと、PreOperationで実際に保存されるTargetを分離して提出する。
        /// DataverseのパイプラインTargetを検証用に複製した場合でも、サーバー権威列は
        /// 必ず保存対象へスタンプする。
        /// </summary>
        public static StandardApprovalSubmitResult Submit(
            IOrganizationService service,
            Entity requestTarget,
            Entity requestWriteTarget,
            Guid initiatingUserId,
            Guid approverTeamId,
            DateTime submittedAtUtc)
        {
            var requestId = requestTarget?.Id ?? Guid.Empty;
            var targetError = ValidateRequestTarget(requestTarget);
            if (targetError != StandardApprovalSubmitError.None)
                return StandardApprovalSubmitResult.Failure(requestId, targetError);
            if (requestTarget == null)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.TargetRequired);
            if (requestWriteTarget == null
                || !string.Equals(requestWriteTarget.LogicalName, RequestEntityName, StringComparison.Ordinal)
                || requestWriteTarget.Id != requestTarget.Id)
            {
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.InvalidInput);
            }
            if (service == null || initiatingUserId == Guid.Empty || submittedAtUtc.Kind != DateTimeKind.Utc)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.InvalidInput);
            if (approverTeamId == Guid.Empty)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ApproverTeamRequired);

            ApprovalSubmissionRepository.RequestRecord request;
            try
            {
                request = ApprovalSubmissionRepository.RetrieveRequest(service, requestId);
            }
            catch (InvalidPluginExecutionException)
            {
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.RequestNotFound);
            }

            if (!request.StateCode.HasValue)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.RequestStateRequired);
            if (request.StateCode.Value != 0)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.RequestInactive);
            if (!request.RequestingUserId.HasValue || request.RequestingUserId.Value != initiatingUserId)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.NotRequester);

            // 同じ標準Updateが再送された場合は、既存のLink・版・成功監査が揃う場合だけ
            // 無更新の確定結果を返す。申請者検査より前に短絡しないことが重要である。
            if (request.Status == ApprovalRequestStatus.提出中)
            {
                return Replay(
                    service,
                    request,
                    requestWriteTarget,
                    initiatingUserId,
                    approverTeamId);
            }

            if (request.Status != ApprovalRequestStatus.下書き
                && request.Status != ApprovalRequestStatus.差戻し)
            {
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.StateCannotSubmit);
            }

            var draftLookup = FindSingleDraftVersion(service, requestId);
            if (draftLookup.ErrorCode != StandardApprovalSubmitError.None)
            {
                return StandardApprovalSubmitResult.Failure(requestId, draftLookup.ErrorCode);
            }

            ApprovalSubmissionRepository.SubmissionVersionRecord version;
            try
            {
                version = ApprovalSubmissionRepository.RetrieveSubmissionVersion(
                    service,
                    draftLookup.SubmissionVersionId);
            }
            catch (InvalidPluginExecutionException)
            {
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.SubmissionVersionNotFound);
            }

            if (!version.StateCode.HasValue)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.SubmissionVersionStateRequired);
            if (version.StateCode.Value != 0)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.SubmissionVersionInactive);
            if (!version.RequestId.HasValue || version.RequestId.Value != requestId)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.SubmissionVersionPairMismatch);
            if (version.Status != ApprovalSubmissionVersionStatus.下書き)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.SubmissionVersionStateInvalid);
            if (version.SubmittedById.HasValue || version.SubmittedAt.HasValue)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.SubmissionVersionAuthorityPresent);
            if (string.IsNullOrWhiteSpace(version.ChangeSetJson))
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ChangeSetInvalid);

            var settingsResult = ApprovalSettingsRepository.RetrieveActive(service);
            if (!settingsResult.IsValid || settingsResult.Settings == null)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.SettingsInvalid);

            var targetResult = ApprovalTargetRepository.RetrieveForRequest(service, requestId);
            if (!targetResult.IsValid || targetResult.Target == null)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.TargetInvalid);

            var policyResult = ApprovalPolicyContract.Resolve(
                settingsResult.Settings.Policy,
                request.RequestTypeCode,
                targetResult.Target.EntityName);
            if (!policyResult.IsValid || policyResult.Resolution == null)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.PolicyInvalid);

            var changeSetResult = ApprovalChangeSetContract.Validate(new ApprovalChangeSetInput
            {
                Json = version.ChangeSetJson,
                ExpectedEntityName = targetResult.Target.EntityName,
                ExpectedTargetId = targetResult.Target.Id,
                AllowedAttributes = policyResult.Resolution.AllowedAttributes,
            });
            if (!changeSetResult.IsValid || changeSetResult.ChangeSet == null
                || changeSetResult.ChangeSet.Operations == null
                || changeSetResult.ChangeSet.Operations.Count == 0
                || changeSetResult.ChangeSet.Operations.Any(operation => operation == null))
            {
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ChangeSetInvalid);
            }
            // 主担当の変更は、通常の有効な利用者で、今と違う人のときだけ提出できる（2026-09-29）。
            var mainOwnerOperation = changeSetResult.ChangeSet.Operations
                .FirstOrDefault(operation => operation.AttributeName == PartnerStandardCreateContract.MainOwnerAttribute);
            if (mainOwnerOperation != null)
            {
                var newMainOwnerId = mainOwnerOperation.Value.UserIdValue ?? Guid.Empty;
                var currentMainOwner = targetResult.Target.CurrentRow?.GetAttributeValue<EntityReference>(PartnerStandardCreateContract.MainOwnerAttribute);
                if (currentMainOwner != null && currentMainOwner.Id == newMainOwnerId)
                    return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.MainOwnerUnchanged);
                if (!PartnerMainOwnerChange.IsShareableUser(service, newMainOwnerId))
                    return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.MainOwnerUnavailable);
            }
            // 反映時の衝突判定に使う、変更する項目だけの提出時点の値の指紋（設計A）。
            var targetBaseline = ApprovalTargetBaseline.Compute(
                targetResult.Target.CurrentRow,
                changeSetResult.ChangeSet.Operations.Select(operation => operation.AttributeName));
            // 承認依頼に載せる申請内容の概要（承認者が判断に使うため、サーバーが作る）。
            var changeSummary = ApprovalChangeSummary.Build(
                targetResult.Target,
                changeSetResult.ChangeSet,
                userId => ApprovalChangeSummary.RetrieveUserName(service, userId));
            var approvalTitle = ApprovalChangeSummary.BuildTitle(targetResult.Target, request.RequestTypeCode);

            if (!IsActiveTeam(service, approverTeamId))
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ApproverTeamInvalid);
            if (request.ApproverTeamId.HasValue && request.ApproverTeamId.Value != approverTeamId)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.AuthorityConflict);
            if (!string.IsNullOrWhiteSpace(request.PolicyVersion)
                && !string.Equals(request.PolicyVersion, settingsResult.Settings.PolicyVersion, StringComparison.Ordinal))
            {
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.AuthorityConflict);
            }
            if (request.Status == ApprovalRequestStatus.下書き && request.RequestedAt.HasValue)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.AuthorityConflict);

            var versionNumberResult = ResolveNextVersionNumber(service, requestId);
            if (!versionNumberResult.IsValid)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.VersionNumberInvalid);

            var idempotencyKey = BuildIdempotencyKey(requestId, version.Id);
            var previousSuccess = OperationLogRepository.FindSuccessApproval(
                service,
                OperationCode,
                idempotencyKey);
            if (previousSuccess != null)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.IdempotencyKeyConflict);

            // ここから先は、呼出し元Targetへ権威値を追加し、子行と監査を同一同期処理で確定する。
            // Request本体のUpdateはこのPluginを呼び出した主操作が最後に保存する。
            StampRequestTarget(
                requestWriteTarget,
                approverTeamId,
                settingsResult.Settings.PolicyVersion,
                submittedAtUtc);

            ApprovalSubmissionRepository.MarkSubmissionVersionSubmitted(
                service,
                version,
                initiatingUserId,
                submittedAtUtc,
                settingsResult.Settings.PolicyVersion,
                targetBaseline,
                versionNumberResult.VersionNumber,
                changeSummary,
                approvalTitle);

            var linkId = CreateApprovalLink(
                service,
                requestId,
                version.Id,
                BuildIdempotencyKey(requestId, version.Id));

            OperationLogRepository.RecordApproval(
                service,
                OperationCode,
                "Success",
                null,
                initiatingUserId,
                requestId,
                version.Id,
                BuildAuditName(requestId, version.Id),
                idempotencyKey);

            return StandardApprovalSubmitResult.Succeeded(
                requestId,
                version.Id,
                linkId,
                false,
                ApprovalRequestStatus.提出中,
                ApprovalSubmissionVersionStatus.提出済み,
                ApprovalLinkStatus.送信待ち,
                settingsResult.Settings.PolicyVersion,
                targetBaseline,
                versionNumberResult.VersionNumber,
                submittedAtUtc,
                idempotencyKey);
        }

        private static StandardApprovalSubmitResult Replay(
            IOrganizationService service,
            ApprovalSubmissionRepository.RequestRecord request,
            Entity requestWriteTarget,
            Guid initiatingUserId,
            Guid approverTeamId)
        {
            var requestId = request.Id;
            var needsAuthorityRepair = !request.ApproverTeamId.HasValue
                && string.IsNullOrWhiteSpace(request.PolicyVersion)
                && !request.RequestedAt.HasValue;
            if ((!needsAuthorityRepair && !request.ApproverTeamId.HasValue)
                || (request.ApproverTeamId.HasValue && request.ApproverTeamId.Value != approverTeamId))
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);
            if (!needsAuthorityRepair
                && (string.IsNullOrWhiteSpace(request.PolicyVersion) || !request.RequestedAt.HasValue))
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);

            var linksQuery = new QueryExpression(ApprovalLinkEntityName)
            {
                ColumnSet = new ColumnSet(
                    "pl_requestlookup",
                    "pl_submissionversionlookup",
                    "pl_linkstatuscode",
                    "pl_externalrequestkey",
                    "pl_resultknown"),
                TopCount = 101,
            };
            linksQuery.Criteria.AddCondition("pl_requestlookup", ConditionOperator.Equal, requestId);

            EntityCollection links;
            try
            {
                links = service.RetrieveMultiple(linksQuery);
            }
            catch (InvalidPluginExecutionException)
            {
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);
            }

            if (links.MoreRecords || links.Entities.Count == 0)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);

            Entity? selectedLink = null;
            ApprovalSubmissionRepository.SubmissionVersionRecord? selectedVersion = null;
            foreach (var candidate in links.Entities)
            {
                var requestReference = candidate.GetAttributeValue<EntityReference>("pl_requestlookup");
                var versionReference = candidate.GetAttributeValue<EntityReference>("pl_submissionversionlookup");
                if (requestReference == null
                    || requestReference.Id != requestId
                    || !string.Equals(requestReference.LogicalName, RequestEntityName, StringComparison.Ordinal)
                    || versionReference == null
                    || versionReference.Id == Guid.Empty
                    || !string.Equals(versionReference.LogicalName, SubmissionVersionEntityName, StringComparison.Ordinal))
                {
                    return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);
                }

                ApprovalSubmissionRepository.SubmissionVersionRecord candidateVersion;
                try
                {
                    candidateVersion = ApprovalSubmissionRepository.RetrieveSubmissionVersion(service, versionReference.Id);
                }
                catch (InvalidPluginExecutionException)
                {
                    return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);
                }

                if (!candidateVersion.StateCode.HasValue
                    || candidateVersion.StateCode.Value != 0
                    || !candidateVersion.RequestId.HasValue
                    || candidateVersion.RequestId.Value != requestId)
                {
                    return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);
                }

                // 同じRequestには差戻し済みの旧Linkも残る。現在の提出版は、
                // Request=提出中と整合する提出済み版として一意に解決する。
                if (candidateVersion.Status == ApprovalSubmissionVersionStatus.提出済み)
                {
                    if (selectedLink != null)
                        return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);
                    selectedLink = candidate;
                    selectedVersion = candidateVersion;
                }
            }

            if (selectedLink == null || selectedVersion == null)
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);

            var link = selectedLink;
            var version = selectedVersion;
            if (!version.SubmittedById.HasValue
                || version.SubmittedById.Value != initiatingUserId
                || !version.SubmittedAt.HasValue
                || !version.VersionNumber.HasValue
                || version.VersionNumber.Value <= 0
                || string.IsNullOrWhiteSpace(version.PolicyVersion)
                || string.IsNullOrWhiteSpace(version.TargetRowVersion)
                || (!needsAuthorityRepair
                    && !string.Equals(version.PolicyVersion, request.PolicyVersion, StringComparison.Ordinal)))
            {
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);
            }

            ApprovalLinkStatus linkStatus;
            if (!Enum.TryParse(link.GetAttributeValue<string>("pl_linkstatuscode"), false, out linkStatus)
                || !Enum.IsDefined(typeof(ApprovalLinkStatus), linkStatus))
            {
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);
            }

            var idempotencyKey = BuildIdempotencyKey(requestId, version.Id);
            var externalKey = link.GetAttributeValue<string>("pl_externalrequestkey") ?? string.Empty;
            if (!string.Equals(externalKey, idempotencyKey, StringComparison.Ordinal))
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);

            var previousSuccess = OperationLogRepository.FindSuccessApproval(
                service,
                OperationCode,
                idempotencyKey);
            if (previousSuccess == null
                || previousSuccess.TargetRequestId != requestId
                || previousSuccess.TargetSubmissionVersionId != version.Id
                || !string.Equals(previousSuccess.Name, BuildAuditName(requestId, version.Id), StringComparison.Ordinal))
            {
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);
            }

            // approverTeamIdは、実行Step設定が消えた状態での誤った再送成功を防ぐため、
            // 初回と同じく存在・Activeを確認する。結果はLookupへ再書込みしない。
            if (!IsActiveTeam(service, approverTeamId))
                return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ApproverTeamInvalid);

            if (needsAuthorityRepair)
            {
                var settingsResult = ApprovalSettingsRepository.RetrieveActive(service);
                if (!settingsResult.IsValid
                    || settingsResult.Settings == null
                    || !string.Equals(
                        settingsResult.Settings.PolicyVersion,
                        version.PolicyVersion,
                        StringComparison.Ordinal)
                    || !version.SubmittedAt.HasValue)
                {
                    return StandardApprovalSubmitResult.Failure(requestId, StandardApprovalSubmitError.ReplayDataInvalid);
                }

                // 旧版の検証デモで親だけ欠けた場合に限り、既存の提出版の
                // サーバー値から親の権威列を復旧する。Link・提出版・成功監査が
                // 先に揃っていることを上で確認しており、子行は再作成しない。
                StampRequestTarget(
                    requestWriteTarget,
                    approverTeamId,
                    version.PolicyVersion,
                    version.SubmittedAt.Value);
            }

            return StandardApprovalSubmitResult.Succeeded(
                requestId,
                version.Id,
                link.Id,
                true,
                request.Status,
                version.Status,
                linkStatus,
                version.PolicyVersion,
                version.TargetRowVersion,
                version.VersionNumber ?? 0,
                version.SubmittedAt,
                idempotencyKey);
        }

        private static StandardApprovalSubmitError ValidateRequestTarget(Entity? target)
        {
            if (target == null)
                return StandardApprovalSubmitError.TargetRequired;
            if (!string.Equals(target.LogicalName, RequestEntityName, StringComparison.Ordinal))
                return StandardApprovalSubmitError.TargetEntityMismatch;
            if (target.Id == Guid.Empty)
                return StandardApprovalSubmitError.TargetIdRequired;

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (attributeName == "pl_requestid")
                {
                    if (!(target[attributeName] is Guid requestId) || requestId != target.Id)
                        return StandardApprovalSubmitError.TargetIdMismatch;
                    continue;
                }
                if (StandardApprovalCrudContract.IsAuthorityAttribute(attributeName))
                    return StandardApprovalSubmitError.AuthorityAttributeForbidden;
                if (attributeName != "pl_requeststatuscode")
                    return StandardApprovalSubmitError.UnexpectedAttribute;
            }

            var status = target.GetAttributeValue<string>("pl_requeststatuscode");
            if (!string.Equals(status, ApprovalRequestStatus.提出中.ToString(), StringComparison.Ordinal))
                return StandardApprovalSubmitError.InvalidStatus;
            return StandardApprovalSubmitError.None;
        }

        private static DraftVersionLookupResult FindSingleDraftVersion(
            IOrganizationService service,
            Guid requestId)
        {
            var query = new QueryExpression(SubmissionVersionEntityName)
            {
                ColumnSet = new ColumnSet("pl_requestlookup", "pl_submissionstatuscode", "statecode"),
                TopCount = 2,
            };
            query.Criteria.AddCondition("pl_requestlookup", ConditionOperator.Equal, requestId);
            query.Criteria.AddCondition("pl_submissionstatuscode", ConditionOperator.Equal, ApprovalSubmissionVersionStatus.下書き.ToString());
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            EntityCollection result;
            try
            {
                result = service.RetrieveMultiple(query);
            }
            catch (InvalidPluginExecutionException)
            {
                return DraftVersionLookupResult.Invalid(StandardApprovalSubmitError.SubmissionVersionNotFound);
            }

            if (result.MoreRecords || result.Entities.Count > 1)
                return DraftVersionLookupResult.Invalid(StandardApprovalSubmitError.SubmissionVersionAmbiguous);
            if (result.Entities.Count == 0)
                return DraftVersionLookupResult.Invalid(StandardApprovalSubmitError.SubmissionVersionRequired);

            var version = result.Entities[0];
            if (version.Id == Guid.Empty)
                return DraftVersionLookupResult.Invalid(StandardApprovalSubmitError.SubmissionVersionNotFound);
            return DraftVersionLookupResult.Valid(version.Id);
        }

        private static VersionNumberResolution ResolveNextVersionNumber(
            IOrganizationService service,
            Guid requestId)
        {
            var query = new QueryExpression(SubmissionVersionEntityName)
            {
                ColumnSet = new ColumnSet("pl_versionnumber"),
                TopCount = 101,
            };
            query.Criteria.AddCondition("pl_requestlookup", ConditionOperator.Equal, requestId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            EntityCollection result;
            try
            {
                result = service.RetrieveMultiple(query);
            }
            catch (InvalidPluginExecutionException)
            {
                return VersionNumberResolution.Invalid();
            }

            if (result.MoreRecords)
                return VersionNumberResolution.Invalid();

            var maxVersion = 0;
            foreach (var entity in result.Entities)
            {
                if (!entity.Attributes.TryGetValue("pl_versionnumber", out var raw) || raw == null)
                    continue;

                int versionNumber;
                if (raw is int integer)
                {
                    versionNumber = integer;
                }
                else if (raw is long longValue && longValue <= int.MaxValue && longValue >= int.MinValue)
                {
                    versionNumber = (int)longValue;
                }
                else
                {
                    return VersionNumberResolution.Invalid();
                }

                if (versionNumber <= 0)
                    return VersionNumberResolution.Invalid();
                maxVersion = Math.Max(maxVersion, versionNumber);
            }

            if (maxVersion == int.MaxValue)
                return VersionNumberResolution.Invalid();
            return VersionNumberResolution.Valid(maxVersion + 1);
        }

        private static bool IsActiveTeam(IOrganizationService service, Guid approverTeamId)
        {
            if (approverTeamId == Guid.Empty)
                return false;

            try
            {
                // Dataverse's team table has no statecode column. A team is
                // considered resolvable here when the exact row exists; the
                // configured M365/Office-group identity is checked at the
                // registration boundary before this service is invoked.
                var team = service.Retrieve(TeamEntityName, approverTeamId, new ColumnSet("teamid"));
                return team != null && team.Id == approverTeamId;
            }
            catch (InvalidPluginExecutionException)
            {
                return false;
            }
        }

        private static void StampRequestTarget(
            Entity requestTarget,
            Guid approverTeamId,
            string policyVersion,
            DateTime submittedAtUtc)
        {
            requestTarget["pl_approverteamlookup"] = new EntityReference(TeamEntityName, approverTeamId);
            requestTarget["pl_approvalpolicyversion"] = policyVersion;
            requestTarget["pl_requestedat"] = submittedAtUtc;
        }

        private static Guid CreateApprovalLink(
            IOrganizationService service,
            Guid requestId,
            Guid submissionVersionId,
            string idempotencyKey)
        {
            var link = new Entity(ApprovalLinkEntityName)
            {
                ["pl_name"] = BuildAuditName(requestId, submissionVersionId),
                ["pl_requestlookup"] = new EntityReference(RequestEntityName, requestId),
                ["pl_submissionversionlookup"] = new EntityReference(SubmissionVersionEntityName, submissionVersionId),
                ["pl_approvalsourcecode"] = ApprovalResultSourceCodes.PowerAutomateGroup,
                ["pl_linkstatuscode"] = ApprovalLinkStatus.送信待ち.ToString(),
                ["pl_externalrequestkey"] = idempotencyKey,
                ["pl_resultknown"] = false,
                ["pl_decisioncode"] = string.Empty,
            };
            var linkId = service.Create(link);
            if (linkId == Guid.Empty)
                throw new InvalidPluginExecutionException("承認連携行の保存結果IDを解決できません。");
            return linkId;
        }

        private static string BuildIdempotencyKey(Guid requestId, Guid submissionVersionId)
            => "standard-submit-" + requestId.ToString("N") + "-" + submissionVersionId.ToString("N");

        private static string BuildAuditName(Guid requestId, Guid submissionVersionId)
            => "標準提出 " + requestId.ToString("N") + "/" + submissionVersionId.ToString("N");

        private sealed class DraftVersionLookupResult
        {
            private DraftVersionLookupResult(StandardApprovalSubmitError errorCode, Guid submissionVersionId)
            {
                ErrorCode = errorCode;
                SubmissionVersionId = submissionVersionId;
            }

            public StandardApprovalSubmitError ErrorCode { get; }
            public Guid SubmissionVersionId { get; }

            public static DraftVersionLookupResult Invalid(StandardApprovalSubmitError errorCode)
                => new DraftVersionLookupResult(errorCode, Guid.Empty);

            public static DraftVersionLookupResult Valid(Guid submissionVersionId)
                => new DraftVersionLookupResult(StandardApprovalSubmitError.None, submissionVersionId);
        }

        private sealed class VersionNumberResolution
        {
            private VersionNumberResolution(bool isValid, int versionNumber)
            {
                IsValid = isValid;
                VersionNumber = versionNumber;
            }

            public bool IsValid { get; }
            public int VersionNumber { get; }

            public static VersionNumberResolution Invalid()
                => new VersionNumberResolution(false, 0);

            public static VersionNumberResolution Valid(int versionNumber)
                => new VersionNumberResolution(true, versionNumber);
        }
    }
}
