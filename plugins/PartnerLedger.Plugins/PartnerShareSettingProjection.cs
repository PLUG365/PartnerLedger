using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 共有設定行の正本を、Dataverseの標準共有メッセージへ投影する計画。
    ///
    /// このクラスはDataverseへ書き込まない。標準Create／Update／取消のトランザクション
    /// 内で、サーバー側に解決済みの呼出者情報・現在の直接共有権・設定行一覧を受け取り、
    /// GrantAccess／ModifyAccess／RevokeAccessのどれを呼べるかだけを決める。実行主体の
    /// 解決、ロール／Teamの判定、設定行の保存は呼出し側の責務であり、クライアント入力を
    /// この計画へ直接渡して認可情報として使ってはならない。
    /// </summary>
    public enum PartnerShareProjectionOperation
    {
        None,
        Grant,
        Modify,
        Revoke,
        ReconciliationRequired,
    }

    /// <summary>
    /// 直接共有権の由来。ManagedByPartnerLedgerを指定できるのは、旧経路を止め、
    /// 現在の直接共有が共有設定行の投影だけであることを別工程で確認した後に限る。
    /// </summary>
    public enum PartnerShareDirectShareProvenance
    {
        Unknown,
        ManagedByPartnerLedger,
        UntrackedOrExternal,
    }

    public sealed class PartnerShareDirectShareState
    {
        /// <summary>RetrievePrincipalAccess等で現在権を解決できたか。</summary>
        public bool IsResolved { get; set; }

        /// <summary>対象Partnerとprincipalの現在の直接共有権。Noneは直接共有なし。</summary>
        public AccessRights CurrentRights { get; set; }

        /// <summary>CurrentRightsが共有設定行の投影だけで作られたか。</summary>
        public PartnerShareDirectShareProvenance Provenance { get; set; }
    }

    public enum PartnerShareProjectionError
    {
        None,
        ContractDenied,
        ExistingSettingsRequired,
        InvalidExistingSetting,
        RequestKeyConflict,
        DirectShareStateRequired,
        DirectShareStateInvalid,
        UntrackedDirectShare,
    }

    /// <summary>
    /// 共有設定行の保存と、実共有の投影を分けて返す結果。
    /// IsAllowed=falseの場合、呼出し側は設定行の保存も共有メッセージ実行も行わず、
    /// 標準CRUDトランザクションを失敗させる。ReconciliationRequiredは、旧経路の
    /// 共有権を勝手に剥がさないための明示的な保留状態である。
    /// </summary>
    public sealed class PartnerShareSettingProjectionPlan
    {
        private PartnerShareSettingProjectionPlan(
            bool isAllowed,
            PartnerShareProjectionError error,
            PartnerShareSettingError contractError,
            PartnerShareProjectionOperation operation,
            bool isReplay,
            Guid settingId,
            Guid partnerId,
            PartnerSharePrincipalKind? principalKind,
            Guid principalId,
            AccessRights accessMask,
            bool isManagedProjection)
        {
            IsAllowed = isAllowed;
            Error = error;
            ContractError = contractError;
            Operation = operation;
            IsReplay = isReplay;
            SettingId = settingId;
            PartnerId = partnerId;
            PrincipalKind = principalKind;
            PrincipalId = principalId;
            AccessMask = accessMask;
            IsManagedProjection = isManagedProjection;
        }

        public bool IsAllowed { get; }
        public PartnerShareProjectionError Error { get; }
        public PartnerShareSettingError ContractError { get; }
        public PartnerShareProjectionOperation Operation { get; }
        public bool IsReplay { get; }
        public bool RequiresReconciliation
            => Operation == PartnerShareProjectionOperation.ReconciliationRequired;
        public Guid SettingId { get; }
        public Guid PartnerId { get; }
        public PartnerSharePrincipalKind? PrincipalKind { get; }
        public Guid PrincipalId { get; }
        public AccessRights AccessMask { get; }
        public bool IsManagedProjection { get; }

        public EntityReference TargetReference
            => new EntityReference("pl_partner", PartnerId);

        public EntityReference PrincipalReference
            => new EntityReference(
                PrincipalKind == PartnerSharePrincipalKind.Team ? "team" : "systemuser",
                PrincipalId);

        internal static PartnerShareSettingProjectionPlan Allow(
            PartnerShareProjectionOperation operation,
            Guid settingId,
            Guid partnerId,
            PartnerSharePrincipalKind principalKind,
            Guid principalId,
            AccessRights accessMask,
            bool isReplay = false,
            bool isManagedProjection = false)
            => new PartnerShareSettingProjectionPlan(
                true,
                PartnerShareProjectionError.None,
                PartnerShareSettingError.None,
                operation,
                isReplay,
                settingId,
                partnerId,
                principalKind,
                principalId,
                accessMask,
                isManagedProjection);

        internal static PartnerShareSettingProjectionPlan Deny(
            PartnerShareProjectionError error,
            PartnerShareSettingError contractError = PartnerShareSettingError.None,
            PartnerShareProjectionOperation operation = PartnerShareProjectionOperation.None,
            Guid settingId = default,
            Guid partnerId = default,
            PartnerSharePrincipalKind? principalKind = null,
            Guid principalId = default,
            AccessRights accessMask = AccessRights.None)
            => new PartnerShareSettingProjectionPlan(
                false,
                error,
                contractError,
                operation,
                false,
                settingId,
                partnerId,
                principalKind,
                principalId,
                accessMask,
                false);
    }

    public static class PartnerShareSettingProjection
    {
        private static readonly AccessRights ManagedRights =
            AccessRights.ReadAccess | AccessRights.WriteAccess;

        public static PartnerShareSettingProjectionPlan PlanCreate(
            PartnerSharePhase phase,
            PartnerShareCaller caller,
            PartnerShareSettingInput desired,
            IEnumerable<PartnerShareSettingRecord> existing,
            PartnerShareDirectShareState directShare,
            Guid resolvedApproverTeamId = default)
        {
            if (desired == null)
            {
                return PartnerShareSettingProjectionPlan.Deny(PartnerShareProjectionError.ContractDenied);
            }

            var authorization = PartnerShareSettingContract.AuthorizeCreate(phase, caller, desired);
            if (!authorization.IsAllowed)
            {
                return DenyContract(authorization.Error, desired);
            }

            var existingResult = PrepareExisting(existing);
            if (!existingResult.IsValid)
            {
                return PartnerShareSettingProjectionPlan.Deny(existingResult.Error);
            }

            var contextError = ValidateDirectShareState(directShare);
            if (contextError != PartnerShareProjectionError.None)
            {
                return PartnerShareSettingProjectionPlan.Deny(contextError);
            }

            var records = existingResult.Records!;
            var requestConflict = FindRequestConflict(records, desired, Guid.Empty);
            if (requestConflict != null)
            {
                if (PartnerShareSettingContract.IsSameRequest(requestConflict, desired))
                {
                    return BuildReplayPlan(requestConflict, desired);
                }

                return PartnerShareSettingProjectionPlan.Deny(
                    PartnerShareProjectionError.RequestKeyConflict,
                    partnerId: desired.PartnerId,
                    principalKind: desired.PrincipalKind,
                    principalId: PartnerShareSettingContract.PrincipalId(desired));
            }

            var projectedRights = EffectiveRights(records, desired.PartnerId, desired, null);
            var isInitialApproverPath = phase == PartnerSharePhase.InitialSetup
                && resolvedApproverTeamId != Guid.Empty
                && desired.PrincipalKind == PartnerSharePrincipalKind.Team
                && desired.TeamId == resolvedApproverTeamId;
            return BuildPositivePlan(
                directShare!,
                settingId: Guid.Empty,
                desired.PartnerId,
                desired.PrincipalKind!.Value,
                PartnerShareSettingContract.PrincipalId(desired),
                projectedRights,
                isInitialApproverPath: isInitialApproverPath);
        }

        public static PartnerShareSettingProjectionPlan PlanUpdate(
            PartnerSharePhase phase,
            PartnerShareCaller caller,
            PartnerShareSettingRecord current,
            PartnerShareSettingInput desired,
            IEnumerable<PartnerShareSettingRecord> existing,
            PartnerShareDirectShareState directShare)
        {
            if (current == null || desired == null)
            {
                return PartnerShareSettingProjectionPlan.Deny(PartnerShareProjectionError.ContractDenied);
            }

            var authorization = PartnerShareSettingContract.AuthorizeUpdate(phase, caller, current, desired);
            if (!authorization.IsAllowed)
            {
                return DenyContract(authorization.Error, desired, current.Id);
            }

            var existingResult = PrepareExisting(existing);
            if (!existingResult.IsValid)
            {
                return PartnerShareSettingProjectionPlan.Deny(existingResult.Error, settingId: current.Id);
            }

            var contextError = ValidateDirectShareState(directShare);
            if (contextError != PartnerShareProjectionError.None)
            {
                return PartnerShareSettingProjectionPlan.Deny(contextError, settingId: current.Id);
            }

            if (PartnerShareSettingContract.IsSameRequest(current, desired))
            {
                return BuildReplayPlan(current, desired);
            }

            var records = existingResult.Records!;
            var requestConflict = FindRequestConflict(records, desired, current.Id);
            if (requestConflict != null)
            {
                return PartnerShareSettingProjectionPlan.Deny(
                    PartnerShareProjectionError.RequestKeyConflict,
                    settingId: current.Id,
                    partnerId: desired.PartnerId,
                    principalKind: desired.PrincipalKind,
                    principalId: PartnerShareSettingContract.PrincipalId(desired));
            }

            var projectedRights = EffectiveRights(records, desired.PartnerId, desired, current.Id);
            return BuildPositivePlan(
                directShare!,
                current.Id,
                desired.PartnerId,
                desired.PrincipalKind!.Value,
                PartnerShareSettingContract.PrincipalId(desired),
                projectedRights);
        }

        public static PartnerShareSettingProjectionPlan PlanRevoke(
            PartnerSharePhase phase,
            PartnerShareCaller caller,
            PartnerShareSettingRecord target,
            IEnumerable<PartnerShareSettingRecord> existing,
            PartnerShareDirectShareState directShare)
        {
            if (target == null)
            {
                return PartnerShareSettingProjectionPlan.Deny(PartnerShareProjectionError.ContractDenied);
            }

            var existingResult = PrepareExisting(existing);
            if (!existingResult.IsValid)
            {
                return PartnerShareSettingProjectionPlan.Deny(
                    existingResult.Error,
                    settingId: target.Id,
                    partnerId: target.PartnerId,
                    principalKind: target.PrincipalKind,
                    principalId: target.PrincipalId);
            }

            var authorization = PartnerShareSettingContract.AuthorizeRevoke(
                phase,
                caller,
                target,
                existingResult.Records!);
            if (!authorization.IsAllowed)
            {
                return PartnerShareSettingProjectionPlan.Deny(
                    PartnerShareProjectionError.ContractDenied,
                    authorization.Error,
                    settingId: target.Id,
                    partnerId: target.PartnerId,
                    principalKind: target.PrincipalKind,
                    principalId: target.PrincipalId);
            }

            var contextError = ValidateDirectShareState(directShare);
            if (contextError != PartnerShareProjectionError.None)
            {
                return PartnerShareSettingProjectionPlan.Deny(
                    contextError,
                    settingId: target.Id,
                    partnerId: target.PartnerId,
                    principalKind: target.PrincipalKind,
                    principalId: target.PrincipalId);
            }

            var remainingRights = EffectiveRights(
                existingResult.Records!,
                target.PartnerId,
                target.PrincipalKind,
                target.PrincipalId,
                target.Id);

            if (remainingRights != AccessRights.None)
            {
                return BuildPositivePlan(
                    directShare!,
                    target.Id,
                    target.PartnerId,
                    target.PrincipalKind,
                    target.PrincipalId,
                    remainingRights);
            }

            if (directShare!.CurrentRights == AccessRights.None)
            {
                return PartnerShareSettingProjectionPlan.Allow(
                    PartnerShareProjectionOperation.None,
                    target.Id,
                    target.PartnerId,
                    target.PrincipalKind,
                    target.PrincipalId,
                    AccessRights.None);
            }

            if (directShare.Provenance != PartnerShareDirectShareProvenance.ManagedByPartnerLedger
                || HasUnmanagedRights(directShare.CurrentRights))
            {
                return ReconciliationPlan(target);
            }

            return PartnerShareSettingProjectionPlan.Allow(
                PartnerShareProjectionOperation.Revoke,
                target.Id,
                target.PartnerId,
                target.PrincipalKind,
                target.PrincipalId,
                AccessRights.None);
        }

        private static PartnerShareSettingProjectionPlan BuildPositivePlan(
            PartnerShareDirectShareState directShare,
            Guid settingId,
            Guid partnerId,
            PartnerSharePrincipalKind principalKind,
            Guid principalId,
            AccessRights projectedRights,
            bool isInitialApproverPath = false)
        {
            if (projectedRights == AccessRights.None)
            {
                return PartnerShareSettingProjectionPlan.Deny(
                    PartnerShareProjectionError.InvalidExistingSetting,
                    settingId: settingId,
                    partnerId: partnerId,
                    principalKind: principalKind,
                    principalId: principalId);
            }

            if (directShare.CurrentRights == AccessRights.None)
            {
                return PartnerShareSettingProjectionPlan.Allow(
                    PartnerShareProjectionOperation.Grant,
                    settingId,
                    partnerId,
                    principalKind,
                    principalId,
                    projectedRights,
                    isManagedProjection: true);
            }

            if (directShare.Provenance == PartnerShareDirectShareProvenance.ManagedByPartnerLedger)
            {
                if (HasUnmanagedRights(directShare.CurrentRights))
                {
                    return ReconciliationPlan(
                        settingId,
                        partnerId,
                        principalKind,
                        principalId);
                }

                return PartnerShareSettingProjectionPlan.Allow(
                    directShare.CurrentRights == projectedRights
                        ? PartnerShareProjectionOperation.None
                        : PartnerShareProjectionOperation.Modify,
                    settingId,
                    partnerId,
                    principalKind,
                    principalId,
                    projectedRights,
                    isManagedProjection: true);
            }

            // 旧経路や外部の共有権を知らずに剥がさない。現在の権利を残したまま
            // 加算できる場合だけModifyを許し、Write->Read等の縮小は保留する。
            var currentManagedRights = directShare.CurrentRights & ManagedRights;
            if ((currentManagedRights & ~projectedRights) != AccessRights.None)
            {
                return ReconciliationPlan(
                    settingId,
                    partnerId,
                    principalKind,
                    principalId);
            }

            var additiveRights = directShare.CurrentRights | projectedRights;
            return PartnerShareSettingProjectionPlan.Allow(
                additiveRights == directShare.CurrentRights
                    ? PartnerShareProjectionOperation.None
                    : PartnerShareProjectionOperation.Modify,
                settingId,
                partnerId,
                principalKind,
                principalId,
                additiveRights,
                // Standard pl_partner Createが先に作った、承認者TeamのRead共有だけは
                // PartnerLedgerが同じInitialSetup経路で管理する。その他の外部共有は
                // 従来どおり未照合として権利縮小・取消を保留する。
                isManagedProjection: isInitialApproverPath
                    && directShare.CurrentRights == AccessRights.ReadAccess);
        }

        private static AccessRights EffectiveRights(
            IEnumerable<PartnerShareSettingRecord> records,
            Guid partnerId,
            PartnerShareSettingInput desired,
            Guid? excludedSettingId)
        {
            return EffectiveRights(
                records,
                partnerId,
                desired.PrincipalKind!.Value,
                PartnerShareSettingContract.PrincipalId(desired),
                excludedSettingId)
                | RightsFor(desired.AccessLevel!.Value);
        }

        private static AccessRights EffectiveRights(
            IEnumerable<PartnerShareSettingRecord> records,
            Guid partnerId,
            PartnerSharePrincipalKind principalKind,
            Guid principalId,
            Guid? excludedSettingId)
        {
            var rights = AccessRights.None;
            foreach (var record in records)
            {
                if (record.Id == excludedSettingId
                    || record.State != PartnerShareSettingState.Active
                    || record.PartnerId != partnerId
                    || record.PrincipalKind != principalKind
                    || record.PrincipalId != principalId)
                {
                    continue;
                }

                rights |= RightsFor(record.AccessLevel);
            }

            return rights;
        }

        public static AccessRights RightsFor(PartnerShareAccessLevel level)
            => level switch
            {
                PartnerShareAccessLevel.Read => AccessRights.ReadAccess,
                PartnerShareAccessLevel.Write => AccessRights.ReadAccess | AccessRights.WriteAccess,
                _ => AccessRights.None,
            };

        private static bool HasUnmanagedRights(AccessRights rights)
            => (rights & ~ManagedRights) != AccessRights.None;

        private static PartnerShareSettingProjectionPlan BuildReplayPlan(
            PartnerShareSettingRecord existing,
            PartnerShareSettingInput desired)
            => PartnerShareSettingProjectionPlan.Allow(
                PartnerShareProjectionOperation.None,
                existing.Id,
                desired.PartnerId,
                desired.PrincipalKind!.Value,
                PartnerShareSettingContract.PrincipalId(desired),
                RightsFor(desired.AccessLevel!.Value),
                isReplay: true,
                isManagedProjection: existing.IsManagedProjection);

        private static PartnerShareSettingRecord? FindRequestConflict(
            IReadOnlyCollection<PartnerShareSettingRecord> records,
            PartnerShareSettingInput desired,
            Guid excludedSettingId)
        {
            var key = desired.RequestKey.Trim();
            var matches = records
                .Where(record => record.Id != excludedSettingId
                    && string.Equals(record.RequestKey.Trim(), key, StringComparison.Ordinal))
                .ToList();
            if (matches.Count == 0) return null;
            return matches[0];
        }

        private static PreparedExistingSettings PrepareExisting(
            IEnumerable<PartnerShareSettingRecord> existing)
        {
            if (existing == null)
            {
                return PreparedExistingSettings.Invalid(PartnerShareProjectionError.ExistingSettingsRequired);
            }

            var records = existing.ToList();
            var ids = new HashSet<Guid>();
            foreach (var record in records)
            {
                if (record == null || !ids.Add(record.Id) || !IsWellFormed(record))
                {
                    return PreparedExistingSettings.Invalid(PartnerShareProjectionError.InvalidExistingSetting);
                }
            }

            return PreparedExistingSettings.Valid(records);
        }

        private static bool IsWellFormed(PartnerShareSettingRecord record)
        {
            if (record.Id == Guid.Empty || record.PartnerId == Guid.Empty || record.PrincipalId == Guid.Empty
                || record.CreatedByUserId == Guid.Empty)
            {
                return false;
            }

            if (record.PrincipalKind != PartnerSharePrincipalKind.User
                && record.PrincipalKind != PartnerSharePrincipalKind.Team)
            {
                return false;
            }

            if (record.IsProtectedManagementPath
                && record.PrincipalKind != PartnerSharePrincipalKind.Team)
            {
                return false;
            }

            if (record.AccessLevel != PartnerShareAccessLevel.Read
                && record.AccessLevel != PartnerShareAccessLevel.Write)
            {
                return false;
            }

            if (record.State != PartnerShareSettingState.Active
                && record.State != PartnerShareSettingState.Revoked)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(record.RequestKey)
                || record.RequestKey.Trim().Length > PartnerShareSettingContract.RequestKeyMaxLength
                || !PartnerShareSettingFingerprint.IsValid(record.ContentHash))
            {
                return false;
            }

            var expectedHash = PartnerShareSettingFingerprint.Compute(new PartnerShareSettingInput
            {
                PartnerId = record.PartnerId,
                PrincipalKind = record.PrincipalKind,
                UserId = record.PrincipalKind == PartnerSharePrincipalKind.User ? record.PrincipalId : (Guid?)null,
                TeamId = record.PrincipalKind == PartnerSharePrincipalKind.Team ? record.PrincipalId : (Guid?)null,
                AccessLevel = record.AccessLevel,
                RequestKey = record.RequestKey,
            });

            return string.Equals(expectedHash, record.ContentHash, StringComparison.OrdinalIgnoreCase);
        }

        private static PartnerShareProjectionError ValidateDirectShareState(
            PartnerShareDirectShareState? directShare)
        {
            if (directShare == null || !directShare.IsResolved)
            {
                return PartnerShareProjectionError.DirectShareStateRequired;
            }

            if (directShare.Provenance != PartnerShareDirectShareProvenance.Unknown
                && directShare.Provenance != PartnerShareDirectShareProvenance.ManagedByPartnerLedger
                && directShare.Provenance != PartnerShareDirectShareProvenance.UntrackedOrExternal)
            {
                return PartnerShareProjectionError.DirectShareStateInvalid;
            }

            return PartnerShareProjectionError.None;
        }

        private static PartnerShareSettingProjectionPlan DenyContract(
            PartnerShareSettingError error,
            PartnerShareSettingInput desired,
            Guid settingId = default)
            => PartnerShareSettingProjectionPlan.Deny(
                PartnerShareProjectionError.ContractDenied,
                error,
                settingId: settingId,
                partnerId: desired.PartnerId,
                principalKind: desired.PrincipalKind,
                principalId: desired.PrincipalKind.HasValue
                    ? PartnerShareSettingContract.PrincipalId(desired)
                    : Guid.Empty);

        private static PartnerShareSettingProjectionPlan ReconciliationPlan(
            PartnerShareSettingRecord target)
            => ReconciliationPlan(
                target.Id,
                target.PartnerId,
                target.PrincipalKind,
                target.PrincipalId);

        private static PartnerShareSettingProjectionPlan ReconciliationPlan(
            Guid settingId,
            Guid partnerId,
            PartnerSharePrincipalKind principalKind,
            Guid principalId)
            => PartnerShareSettingProjectionPlan.Deny(
                PartnerShareProjectionError.UntrackedDirectShare,
                operation: PartnerShareProjectionOperation.ReconciliationRequired,
                settingId: settingId,
                partnerId: partnerId,
                principalKind: principalKind,
                principalId: principalId);

        private sealed class PreparedExistingSettings
        {
            private PreparedExistingSettings(
                bool isValid,
                PartnerShareProjectionError error,
                List<PartnerShareSettingRecord>? records)
            {
                IsValid = isValid;
                Error = error;
                Records = records;
            }

            public bool IsValid { get; }
            public PartnerShareProjectionError Error { get; }
            public List<PartnerShareSettingRecord>? Records { get; }

            public static PreparedExistingSettings Valid(List<PartnerShareSettingRecord> records)
                => new PreparedExistingSettings(true, PartnerShareProjectionError.None, records);

            public static PreparedExistingSettings Invalid(PartnerShareProjectionError error)
                => new PreparedExistingSettings(false, error, null);
        }
    }
}
