using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    public sealed class ApprovalCancellationResult
    {
        private ApprovalCancellationResult(
            bool success,
            bool isReplay,
            Guid requestId,
            Guid? submissionVersionId,
            ApprovalCancellationError errorCode,
            string reason)
        {
            Success = success;
            IsReplay = isReplay;
            RequestId = requestId;
            SubmissionVersionId = submissionVersionId;
            ErrorCode = errorCode;
            Reason = reason;
        }

        public bool Success { get; }
        public bool IsReplay { get; }
        public Guid RequestId { get; }
        public Guid? SubmissionVersionId { get; }
        public ApprovalCancellationError ErrorCode { get; }
        public string Reason { get; }

        public static ApprovalCancellationResult Succeeded(
            Guid requestId,
            Guid? submissionVersionId,
            string reason,
            bool isReplay)
            => new ApprovalCancellationResult(
                true,
                isReplay,
                requestId,
                submissionVersionId,
                ApprovalCancellationError.None,
                reason);

        public static ApprovalCancellationResult Failed(
            Guid requestId,
            ApprovalCancellationError errorCode)
            => new ApprovalCancellationResult(
                false,
                false,
                requestId,
                null,
                errorCode,
                string.Empty);
    }

    /// <summary>
    /// 申請の取消（提出中の管理者取消：PL-033）と取下げ（下書き・差戻し：PL-034）を
    /// 標準Request Updateへ結び付ける同期サービス。
    /// Requestは親の標準Updateで取消されるため、このサービスは提出版と監査を更新し、
    /// Requestのアクティブ申請キーは呼出し元Pluginが同じパイプラインで解放する。
    /// </summary>
    public static class ApprovalCancellationService
    {
        public const string OperationCode = "CancelApprovalRequest";

        /// <remarks>
        /// 2026-09-27からPluginは両方にSYSTEMのサービスを渡す。引数を分けたままにしているのは、
        /// 管理者判定の読取と取消の書込みをコード上で混ぜないことを単体テストで確かめるため。
        /// 実行時の権限はどちらもSYSTEMで同じであり、認可は操作者（initiatingUserId）のロールで判定する。
        /// </remarks>
        public static ApprovalCancellationResult Cancel(
            IOrganizationService writeService,
            IOrganizationService authorizationReadService,
            Guid requestId,
            string? reason,
            Guid initiatingUserId,
            DateTime cancelledAtUtc)
        {
            if (writeService == null || authorizationReadService == null
                || requestId == Guid.Empty || initiatingUserId == Guid.Empty
                || cancelledAtUtc == DateTime.MinValue)
            {
                return ApprovalCancellationResult.Failed(requestId, ApprovalCancellationError.InvalidInput);
            }

            var normalizedReason = (reason ?? string.Empty).Trim();
            if (normalizedReason.Length > ApprovalCancellationContract.ReasonMaxLength)
                return ApprovalCancellationResult.Failed(requestId, ApprovalCancellationError.InvalidInput);

            ApprovalSubmissionRepository.RequestRecord request;
            try
            {
                request = ApprovalSubmissionRepository.RetrieveRequest(writeService, requestId);
            }
            catch (Exception)
            {
                return ApprovalCancellationResult.Failed(requestId, ApprovalCancellationError.RequestNotFound);
            }

            if (request.StateCode != 0)
                return ApprovalCancellationResult.Failed(requestId, ApprovalCancellationError.RequestInactive);

            // 状態・申請者・管理者の判定は、このトランザクション内で再取得した値だけで行う。
            var isRequester = request.RequestingUserId.HasValue && request.RequestingUserId.Value == initiatingUserId;
            var isAdministrator = IsAdministrator(authorizationReadService, initiatingUserId);
            var authorization = ApprovalCancellationContract.Authorize(
                request.Status,
                isRequester,
                isAdministrator,
                normalizedReason);
            if (authorization.Error != ApprovalCancellationError.None)
                return ApprovalCancellationResult.Failed(requestId, authorization.Error);

            if (authorization.Path == ApprovalCancellationPath.Replay)
            {
                return ApprovalCancellationResult.Succeeded(
                    requestId,
                    null,
                    normalizedReason,
                    isReplay: true);
            }

            if (authorization.Path == ApprovalCancellationPath.Withdraw)
                return Withdraw(writeService, request, normalizedReason, initiatingUserId, isRequester);

            if (authorization.Path != ApprovalCancellationPath.AdministratorCancel)
                return ApprovalCancellationResult.Failed(requestId, ApprovalCancellationError.RequestStateInvalid);

            EntityCollection links;
            try
            {
                var linkQuery = new QueryExpression(StandardApprovalCrudContract.ApprovalLinkEntityName)
                {
                    ColumnSet = new ColumnSet(StandardApprovalCrudContract.ApprovalLinkPrimaryIdAttribute),
                };
                linkQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                linkQuery.Criteria.AddCondition("pl_requestlookup", ConditionOperator.Equal, requestId);
                links = writeService.RetrieveMultiple(linkQuery);
            }
            catch (Exception)
            {
                return ApprovalCancellationResult.Failed(requestId, ApprovalCancellationError.ApprovalLinkNotFound);
            }

            if (links.Entities.Count != 1)
                return ApprovalCancellationResult.Failed(requestId, ApprovalCancellationError.ApprovalLinkNotFound);

            ApprovalDecisionRepository.ApprovalLinkRecord link;
            ApprovalSubmissionRepository.SubmissionVersionRecord version;
            try
            {
                link = ApprovalDecisionRepository.Retrieve(writeService, links.Entities[0].Id);
                if (!link.RequestId.HasValue || !link.SubmissionVersionId.HasValue)
                    return ApprovalCancellationResult.Failed(requestId, ApprovalCancellationError.DataIntegrityError);
                version = ApprovalSubmissionRepository.RetrieveSubmissionVersion(writeService, link.SubmissionVersionId.Value);
            }
            catch (Exception)
            {
                return ApprovalCancellationResult.Failed(requestId, ApprovalCancellationError.SubmissionVersionNotFound);
            }

            var validation = ApprovalCancellationContract.Validate(
                request.Status,
                request.StateCode ?? -1,
                links.Entities.Count,
                link.Status,
                link.ResultKnown,
                version.Status,
                link.RequestId.Value == requestId
                    && version.RequestId.HasValue
                    && version.RequestId.Value == requestId,
                normalizedReason);
            if (!validation.IsValid)
                return ApprovalCancellationResult.Failed(requestId, validation.Error);

            try
            {
                ApprovalSubmissionRepository.UpdateSubmissionVersionStatus(
                    writeService,
                    version,
                    ApprovalSubmissionVersionStatus.取消);
                OperationLogRepository.RecordApproval(
                    writeService,
                    OperationCode,
                    "Success",
                    null,
                    initiatingUserId,
                    requestId,
                    version.Id,
                    BuildAuditReason(request.Status, "管理者取消", normalizedReason));
            }
            catch (Exception exception)
            {
                // サーバー処理の書込みが権限（ガードStepを飛ばす指定を含む）で拒否された場合は、
                // 競合と誤表示せず権限エラーとして返す。
                return ApprovalCancellationResult.Failed(
                    requestId,
                    IsPrivilegeDenied(exception)
                        ? ApprovalCancellationError.Unauthorized
                        : ApprovalCancellationError.ConcurrencyConflict);
            }

            return ApprovalCancellationResult.Succeeded(
                requestId,
                version.Id,
                normalizedReason,
                isReplay: false);
        }

        /// <summary>
        /// 下書き・差戻しの取下げ。未提出の提出版（下書き）だけを行バージョン一致で取消にし、
        /// 判断済みの提出版とApprovalLinkは記録として残す。提出と同じ下書き版を取り合った場合は
        /// 行バージョン不一致で片方だけが成功し、失敗側は呼出し元Pluginの例外で全体が戻る。
        /// </summary>
        private static ApprovalCancellationResult Withdraw(
            IOrganizationService writeService,
            ApprovalSubmissionRepository.RequestRecord request,
            string normalizedReason,
            Guid initiatingUserId,
            bool isRequester)
        {
            EntityCollection drafts;
            try
            {
                var draftQuery = new QueryExpression(StandardApprovalCrudContract.SubmissionVersionEntityName)
                {
                    ColumnSet = new ColumnSet(StandardApprovalCrudContract.SubmissionVersionPrimaryIdAttribute),
                };
                draftQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                draftQuery.Criteria.AddCondition("pl_requestlookup", ConditionOperator.Equal, request.Id);
                draftQuery.Criteria.AddCondition(
                    "pl_submissionstatuscode",
                    ConditionOperator.Equal,
                    ApprovalSubmissionVersionStatus.下書き.ToString());
                drafts = writeService.RetrieveMultiple(draftQuery);
            }
            catch (Exception)
            {
                return ApprovalCancellationResult.Failed(request.Id, ApprovalCancellationError.DataIntegrityError);
            }

            Guid? cancelledVersionId = null;
            try
            {
                foreach (var row in drafts.Entities)
                {
                    var version = ApprovalSubmissionRepository.RetrieveSubmissionVersion(writeService, row.Id);
                    if (!version.RequestId.HasValue || version.RequestId.Value != request.Id)
                        return ApprovalCancellationResult.Failed(request.Id, ApprovalCancellationError.SubmissionVersionPairMismatch);
                    if (version.Status != ApprovalSubmissionVersionStatus.下書き)
                        return ApprovalCancellationResult.Failed(request.Id, ApprovalCancellationError.ConcurrencyConflict);

                    ApprovalSubmissionRepository.UpdateSubmissionVersionStatus(
                        writeService,
                        version,
                        ApprovalSubmissionVersionStatus.取消);
                    cancelledVersionId ??= version.Id;
                }

                OperationLogRepository.RecordApproval(
                    writeService,
                    OperationCode,
                    "Success",
                    null,
                    initiatingUserId,
                    request.Id,
                    cancelledVersionId,
                    BuildAuditReason(request.Status, isRequester ? "申請者の取下げ" : "管理者取消", normalizedReason));
            }
            catch (Exception exception)
            {
                return ApprovalCancellationResult.Failed(
                    request.Id,
                    IsPrivilegeDenied(exception)
                        ? ApprovalCancellationError.Unauthorized
                        : ApprovalCancellationError.ConcurrencyConflict);
            }

            return ApprovalCancellationResult.Succeeded(
                request.Id,
                cancelledVersionId,
                normalizedReason,
                isReplay: false);
        }

        // Dataverseの権限不足（0x80040220）。
        public const int PrivilegeDeniedErrorCode = -2147220960;

        private static bool IsPrivilegeDenied(Exception exception)
            => exception is System.ServiceModel.FaultException<OrganizationServiceFault> fault
               && (fault.Detail?.ErrorCode == PrivilegeDeniedErrorCode
                   || (fault.Detail?.Message ?? string.Empty).IndexOf("missing prv", StringComparison.OrdinalIgnoreCase) >= 0);

        public static bool IsAdministrator(IOrganizationService service, Guid userId)
        {
            try
            {
                var query = new QueryExpression("role")
                {
                    ColumnSet = new ColumnSet("name"),
                };
                var userRoleLink = query.AddLink("systemuserroles", "roleid", "roleid");
                userRoleLink.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
                return service.RetrieveMultiple(query)
                    .Entities
                    .Select(role => role.GetAttributeValue<string>("name"))
                    .Any(ApprovalCancellationContract.IsAllowedAdministratorRole);
            }
            catch (Exception)
            {
                // Authorization must fail closed when the role relationship cannot be read.
                return false;
            }
        }

        private static string BuildAuditReason(ApprovalRequestStatus previousStatus, string actor, string reason)
        {
            var audit = $"取消前状態:{previousStatus};実行:{actor};理由:{(reason.Length == 0 ? "なし" : reason)}";
            return audit.Length <= 850 ? audit : audit.Substring(0, 847) + "...";
        }
    }
}
