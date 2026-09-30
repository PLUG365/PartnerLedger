using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace PartnerLedger.Plugins
{
    public enum ApprovalTargetUpdatePlanError
    {
        None,
        TargetRequired,
        ChangeSetRequired,
        TargetEntityUnsupported,
        TargetEntityMismatch,
        TargetIdMismatch,
        TargetStateInvalid,
        TargetRowVersionRequired,
        TargetRowVersionMismatch,
        TargetChangedSinceSubmission,
        ChangeSetFingerprintRequired,
        ChangesRequired,
        DuplicateAttribute,
        AttributeUnsupported,
        PartnerNameNormalizationInvalid,
        ValueInvalid,
    }

    public sealed class ApprovalTargetUpdatePlan
    {
        public string EntityName { get; internal set; } = string.Empty;
        public Guid TargetId { get; internal set; }
        public string TargetRowVersion { get; internal set; } = string.Empty;
        public string ChangeSetFingerprint { get; internal set; } = string.Empty;
        public UpdateRequest UpdateRequest { get; internal set; } = null!;
    }

    public sealed class ApprovalTargetUpdatePlanResult
    {
        private ApprovalTargetUpdatePlanResult(
            bool isValid,
            ApprovalTargetUpdatePlanError error,
            ApprovalTargetUpdatePlan? plan)
        {
            IsValid = isValid;
            Error = error;
            Plan = plan;
        }

        public bool IsValid { get; }
        public ApprovalTargetUpdatePlanError Error { get; }
        public ApprovalTargetUpdatePlan? Plan { get; }

        public static ApprovalTargetUpdatePlanResult Valid(ApprovalTargetUpdatePlan plan)
            => new ApprovalTargetUpdatePlanResult(true, ApprovalTargetUpdatePlanError.None, plan);

        public static ApprovalTargetUpdatePlanResult Invalid(ApprovalTargetUpdatePlanError error)
            => new ApprovalTargetUpdatePlanResult(false, error, null);
    }

    /// <summary>
    /// 検証済み変更セットをDataverseの競合制御付き更新要求へ変換する。
    /// ここではIOrganizationServiceを呼び出さない。取引先名を更新するときは、同じ更新要求に
    /// サーバー側で導出した pl_normalizedname も含め、表示名と派生値がずれた状態を作らない。
    /// </summary>
    public static class ApprovalTargetUpdatePlanner
    {
        public static ApprovalTargetUpdatePlanResult Build(
            ApprovalTargetRecord? target,
            ApprovalChangeSet? changeSet,
            string submittedTargetRowVersion)
        {
            if (target == null)
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.TargetRequired);
            if (changeSet == null)
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.ChangeSetRequired);
            if (!IsSupportedEntity(target.EntityName)
                || !IsSupportedEntity(changeSet.EntityName))
            {
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.TargetEntityUnsupported);
            }
            if (!string.Equals(target.EntityName, changeSet.EntityName, StringComparison.Ordinal))
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.TargetEntityMismatch);
            if (target.Id == Guid.Empty
                || changeSet.TargetId == Guid.Empty
                || target.Id != changeSet.TargetId
                || target.CurrentRow == null
                || target.CurrentRow.Id != target.Id
                || !string.Equals(target.CurrentRow.LogicalName, target.EntityName, StringComparison.Ordinal))
            {
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.TargetIdMismatch);
            }
            if (!IsActive(target.CurrentRow))
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.TargetStateInvalid);
            if (string.IsNullOrWhiteSpace(target.RowVersion)
                || string.IsNullOrWhiteSpace(target.CurrentRow.RowVersion))
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.TargetRowVersionRequired);
            if (!string.Equals(target.RowVersion, target.CurrentRow.RowVersion, StringComparison.Ordinal))
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.TargetRowVersionMismatch);
            if (string.IsNullOrWhiteSpace(submittedTargetRowVersion))
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.TargetRowVersionRequired);
            if (string.IsNullOrWhiteSpace(changeSet.Fingerprint))
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.ChangeSetFingerprintRequired);
            if (changeSet.Operations == null || changeSet.Operations.Count == 0)
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.ChangesRequired);
            if (changeSet.Operations.Any(operation => operation == null))
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.ValueInvalid);

            // 提出時の状態との比較。新形式は変更する項目だけの指紋（無関係な項目の更新では止まらない）、
            // 旧形式（接頭辞なし）は提出時の行全体の版との完全一致。
            var submitted = submittedTargetRowVersion.Trim();
            var unchangedSinceSubmission = ApprovalTargetBaseline.IsFieldToken(submitted)
                ? string.Equals(
                    submitted,
                    ApprovalTargetBaseline.Compute(target.CurrentRow, changeSet.Operations.Select(operation => operation.AttributeName)),
                    StringComparison.Ordinal)
                : string.Equals(submitted, target.RowVersion, StringComparison.Ordinal);
            if (!unchangedSinceSubmission)
                return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.TargetChangedSinceSubmission);

            var update = new Entity(target.EntityName, target.Id)
            {
                RowVersion = target.RowVersion,
            };
            var seenAttributes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in changeSet.Operations)
            {
                if (operation == null || operation.Value == null)
                    return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.ValueInvalid);
                if (!seenAttributes.Add(operation.AttributeName))
                    return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.DuplicateAttribute);

                var error = TryMapValue(changeSet.EntityName, operation, out var value);
                if (error != ApprovalTargetUpdatePlanError.None)
                    return ApprovalTargetUpdatePlanResult.Invalid(error);
                update[operation.AttributeName] = value;

                if (changeSet.EntityName == ApprovalTargetRepository.PartnerEntityName
                    && operation.AttributeName == "pl_name")
                {
                    if (!PartnerNameNormalizer.TryNormalize(value as string, out var normalizedName, out _))
                        return ApprovalTargetUpdatePlanResult.Invalid(ApprovalTargetUpdatePlanError.PartnerNameNormalizationInvalid);
                    // The derived attribute is intentionally not accepted from the change set.
                    // It is calculated from the already validated display name here.
                    update["pl_normalizedname"] = normalizedName;
                }
            }

            var request = new UpdateRequest
            {
                Target = update,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
            };
            ApprovalServerWriteBypass.Apply(request);
            return ApprovalTargetUpdatePlanResult.Valid(new ApprovalTargetUpdatePlan
            {
                EntityName = target.EntityName,
                TargetId = target.Id,
                TargetRowVersion = target.RowVersion,
                ChangeSetFingerprint = changeSet.Fingerprint,
                UpdateRequest = request,
            });
        }

        private static ApprovalTargetUpdatePlanError TryMapValue(
            string entityName,
            ApprovalChangeOperation operation,
            out object? value)
        {
            value = null;
            var attributeName = operation.AttributeName;
            if (!IsSupportedAttribute(entityName, attributeName))
                return ApprovalTargetUpdatePlanError.AttributeUnsupported;

            var changeValue = operation.Value;
            switch (changeValue.Kind)
            {
                case ApprovalChangeValueKind.Text:
                    if (changeValue.IsNull)
                    {
                        if (attributeName == "pl_name")
                            return ApprovalTargetUpdatePlanError.ValueInvalid;
                        value = null;
                        return ApprovalTargetUpdatePlanError.None;
                    }
                    if (changeValue.TextValue == null)
                        return ApprovalTargetUpdatePlanError.ValueInvalid;
                    // 標準Update（取引先・契約）と同じく前後の空白を除き、空白だけの任意項目はnullにする。
                    // 反映はそのガードStepを飛ばすため、同じ正規化をここで行う。
                    var text = changeValue.TextValue.Trim();
                    if ((attributeName == "pl_name" && text.Length == 0) || text.Length > 200)
                        return ApprovalTargetUpdatePlanError.ValueInvalid;
                    value = text.Length == 0 ? null : text;
                    return ApprovalTargetUpdatePlanError.None;

                case ApprovalChangeValueKind.Choice:
                    if (!changeValue.ChoiceValue.HasValue
                        || !IsAllowedChoice(entityName, attributeName, changeValue.ChoiceValue.Value))
                    {
                        return ApprovalTargetUpdatePlanError.ValueInvalid;
                    }
                    value = new OptionSetValue(changeValue.ChoiceValue.Value);
                    return ApprovalTargetUpdatePlanError.None;

                case ApprovalChangeValueKind.SystemUser:
                    // 主担当（2026-09-29）。通常の利用者か・今と違う人かは、反映の前に別に確かめる。
                    if (!changeValue.UserIdValue.HasValue
                        || changeValue.UserIdValue.Value == Guid.Empty
                        || entityName != ApprovalTargetRepository.PartnerEntityName
                        || attributeName != "pl_mainownerlookup")
                    {
                        return ApprovalTargetUpdatePlanError.ValueInvalid;
                    }
                    value = new EntityReference("systemuser", changeValue.UserIdValue.Value);
                    return ApprovalTargetUpdatePlanError.None;

                case ApprovalChangeValueKind.Boolean:
                    if (!changeValue.BooleanValue.HasValue || attributeName != "pl_autorenew")
                        return ApprovalTargetUpdatePlanError.ValueInvalid;
                    value = changeValue.BooleanValue.Value;
                    return ApprovalTargetUpdatePlanError.None;

                case ApprovalChangeValueKind.DateTime:
                    if (attributeName != "pl_decisiondate"
                        && attributeName != "pl_enddate"
                        && attributeName != "pl_noticedate")
                    {
                        return ApprovalTargetUpdatePlanError.ValueInvalid;
                    }
                    if (changeValue.IsNull)
                    {
                        value = null;
                        return ApprovalTargetUpdatePlanError.None;
                    }
                    if (!changeValue.DateTimeValue.HasValue
                        || changeValue.DateTimeValue.Value.Kind != DateTimeKind.Utc)
                    {
                        return ApprovalTargetUpdatePlanError.ValueInvalid;
                    }
                    value = changeValue.DateTimeValue.Value;
                    return ApprovalTargetUpdatePlanError.None;

                default:
                    return ApprovalTargetUpdatePlanError.ValueInvalid;
            }
        }

        private static bool IsSupportedEntity(string entityName)
            => entityName == ApprovalTargetRepository.PartnerEntityName
               || entityName == ApprovalTargetRepository.ContractEntityName;

        private static bool IsSupportedAttribute(string entityName, string attributeName)
            => entityName == ApprovalTargetRepository.PartnerEntityName
                ? attributeName == "pl_name"
                    || attributeName == "pl_tradingstatuscode"
                    || attributeName == "pl_address"
                    || attributeName == "pl_phone"
                    || attributeName == "pl_mainownerlookup"
                : entityName == ApprovalTargetRepository.ContractEntityName
                    && (attributeName == "pl_name"
                        || attributeName == "pl_contractstatuscode"
                        || attributeName == "pl_autorenew"
                        || attributeName == "pl_decisiondate"
                        || attributeName == "pl_enddate"
                        || attributeName == "pl_noticedate"
                        || attributeName == "pl_link");

        private static bool IsAllowedChoice(string entityName, string attributeName, int value)
            => entityName == ApprovalTargetRepository.PartnerEntityName
                && attributeName == "pl_tradingstatuscode"
                ? value >= 100000000 && value <= 100000003
                : entityName == ApprovalTargetRepository.ContractEntityName
                    && attributeName == "pl_contractstatuscode"
                    && (value == 100000000 || value == 100000001);

        private static bool IsActive(Entity entity)
        {
            if (!entity.Attributes.TryGetValue("statecode", out var raw))
                return false;
            var value = raw is OptionSetValue optionSet
                ? optionSet.Value
                : raw is int integer ? integer : -1;
            return value == 0;
        }
    }
}
