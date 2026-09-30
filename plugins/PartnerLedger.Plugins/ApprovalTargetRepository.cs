using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    public enum ApprovalTargetResolutionError
    {
        None,
        RequestRequired,
        RequestNotFound,
        PartnerRequired,
        PartnerLookupUnsupported,
        ContractIdInvalid,
        ContractLookupUnsupported,
        TargetNotFound,
        TargetEntityUnsupported,
        TargetIdMismatch,
        TargetStateRequired,
        TargetInactive,
        TargetRowVersionRequired,
        ContractPartnerLookupRequired,
        ContractPartnerLookupUnsupported,
        ContractPartnerMismatch,
    }

    public sealed class ApprovalTargetRecord
    {
        public string EntityName { get; internal set; } = string.Empty;
        public Guid Id { get; internal set; }
        public Guid PartnerId { get; internal set; }
        public string RowVersion { get; internal set; } = string.Empty;
        public Entity CurrentRow { get; internal set; } = null!;
    }

    public sealed class ApprovalTargetResolutionResult
    {
        private ApprovalTargetResolutionResult(
            bool isValid,
            ApprovalTargetResolutionError error,
            ApprovalTargetRecord? target)
        {
            IsValid = isValid;
            Error = error;
            Target = target;
        }

        public bool IsValid { get; }
        public ApprovalTargetResolutionError Error { get; }
        public ApprovalTargetRecord? Target { get; }

        public static ApprovalTargetResolutionResult Valid(ApprovalTargetRecord target)
            => new ApprovalTargetResolutionResult(true, ApprovalTargetResolutionError.None, target);

        public static ApprovalTargetResolutionResult Invalid(ApprovalTargetResolutionError error)
            => new ApprovalTargetResolutionResult(false, error, null);
    }

    /// <summary>
    /// 承認反映の対象行を、申請に保存されたLookupと現在のDataverse行から再解決する。
    /// 変更セットの値は扱わず、対象Entity／GUID、所属会社、アクティブ状態、row versionだけを
    /// fail-closedで確認する。ここでは更新を実行しない。
    /// </summary>
    public static class ApprovalTargetRepository
    {
        public const string PartnerEntityName = "pl_partner";
        public const string ContractEntityName = "pl_contract";
        private const string PartnerLookupAttribute = "pl_partnerlookup";

        private static readonly ColumnSet PartnerColumns = new ColumnSet(
            "pl_name",
            "pl_tradingstatuscode",
            "pl_address",
            "pl_phone",
            "pl_mainownerlookup",
            "pl_normalizedname",
            "statecode",
            "statuscode",
            "versionnumber");

        private static readonly ColumnSet ContractColumns = new ColumnSet(
            "pl_name",
            "pl_contractstatuscode",
            "pl_autorenew",
            "pl_decisiondate",
            "pl_enddate",
            "pl_noticedate",
            "pl_link",
            PartnerLookupAttribute,
            "statecode",
            "statuscode",
            "versionnumber");

        public static ApprovalTargetResolutionResult RetrieveForRequest(
            IOrganizationService service,
            Guid requestId)
        {
            if (requestId == Guid.Empty)
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.RequestRequired);

            ApprovalSubmissionRepository.RequestRecord request;
            try
            {
                request = ApprovalSubmissionRepository.RetrieveRequest(service, requestId);
            }
            catch (InvalidPluginExecutionException)
            {
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.RequestNotFound);
            }

            if (!request.PartnerId.HasValue || request.PartnerId.Value == Guid.Empty)
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.PartnerRequired);
            if (!string.Equals(request.PartnerLookupLogicalName, PartnerEntityName, StringComparison.Ordinal))
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.PartnerLookupUnsupported);
            if (request.ContractLookupPresent
                && (!request.ContractId.HasValue || request.ContractId.Value == Guid.Empty))
            {
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.ContractIdInvalid);
            }
            if (request.ContractLookupPresent
                && !string.Equals(request.ContractLookupLogicalName, ContractEntityName, StringComparison.Ordinal))
            {
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.ContractLookupUnsupported);
            }

            return Retrieve(service, request.PartnerId.Value, request.ContractId);
        }

        public static ApprovalTargetResolutionResult Retrieve(
            IOrganizationService service,
            Guid partnerId,
            Guid? contractId)
        {
            if (partnerId == Guid.Empty)
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.PartnerRequired);
            if (contractId.HasValue && contractId.Value == Guid.Empty)
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.ContractIdInvalid);

            if (!contractId.HasValue)
            {
                var partner = TryRetrieve(service, PartnerEntityName, partnerId, PartnerColumns);
                return partner == null
                    ? ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.TargetNotFound)
                    : ValidateTarget(partner, PartnerEntityName, partnerId, partnerId);
            }

            var contract = TryRetrieve(service, ContractEntityName, contractId.Value, ContractColumns);
            if (contract == null)
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.TargetNotFound);

            var targetResult = ValidateTarget(contract, ContractEntityName, contractId.Value, partnerId);
            if (!targetResult.IsValid)
                return targetResult;

            var partnerReference = contract.GetAttributeValue<EntityReference>(PartnerLookupAttribute);
            if (partnerReference == null || partnerReference.Id == Guid.Empty)
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.ContractPartnerLookupRequired);
            if (!string.Equals(partnerReference.LogicalName, PartnerEntityName, StringComparison.Ordinal))
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.ContractPartnerLookupUnsupported);
            if (partnerReference.Id != partnerId)
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.ContractPartnerMismatch);

            return targetResult;
        }

        private static Entity? TryRetrieve(
            IOrganizationService service,
            string entityName,
            Guid id,
            ColumnSet columns)
        {
            try
            {
                return service.Retrieve(entityName, id, columns);
            }
            catch (InvalidPluginExecutionException)
            {
                return null;
            }
        }

        private static ApprovalTargetResolutionResult ValidateTarget(
            Entity entity,
            string expectedEntityName,
            Guid expectedId,
            Guid partnerId)
        {
            if (!string.Equals(entity.LogicalName, expectedEntityName, StringComparison.Ordinal))
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.TargetEntityUnsupported);
            if (entity.Id != expectedId)
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.TargetIdMismatch);

            var stateCode = ReadOptionSetValue(entity, "statecode");
            if (!stateCode.HasValue)
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.TargetStateRequired);
            if (stateCode.Value != 0)
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.TargetInactive);
            if (string.IsNullOrWhiteSpace(entity.RowVersion))
                return ApprovalTargetResolutionResult.Invalid(ApprovalTargetResolutionError.TargetRowVersionRequired);

            return ApprovalTargetResolutionResult.Valid(new ApprovalTargetRecord
            {
                EntityName = expectedEntityName,
                Id = expectedId,
                PartnerId = partnerId,
                RowVersion = entity.RowVersion,
                CurrentRow = entity,
            });
        }

        private static int? ReadOptionSetValue(Entity entity, string attributeName)
        {
            if (!entity.Attributes.TryGetValue(attributeName, out var raw))
                return null;
            if (raw is OptionSetValue optionSet)
                return optionSet.Value;
            if (raw is int integer)
                return integer;
            return null;
        }
    }
}
