using System;
using System.Collections.Generic;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// Custom APIを入口にせず、標準の共有設定行Create／Updateから同期処理が検査する
    /// PL-030の純粋な認可契約。DataverseのSecurity Role・行共有そのものを代替しない。
    ///
    /// 共有設定行は「現在の直接共有意図」の正本であり、DataverseのGrantAccess等による
    /// 実共有は同期プラグインが投影する。グループ本体やメンバーシップはこの契約の対象外。
    /// </summary>
    public enum PartnerSharePhase
    {
        InitialSetup,
        Ready,
    }

    public enum PartnerSharePrincipalKind
    {
        User,
        Team,
    }

    public enum PartnerShareAccessLevel
    {
        Read = 1,
        Write = 2,
    }

    public enum PartnerShareSettingState
    {
        Active,
        Revoked,
    }

    public enum PartnerShareSettingError
    {
        None,
        PartnerRequired,
        PrincipalKindRequired,
        PrincipalKindInvalid,
        PrincipalRequired,
        PrincipalExclusive,
        AccessLevelRequired,
        AccessLevelInvalid,
        RequestKeyRequired,
        RequestKeyTooLong,
        CallerRequired,
        CallerUserRequired,
        InitialSetupApproverRequired,
        CompanyAccessRequired,
        AccessExceedsCaller,
        SettingRequired,
        SettingIdRequired,
        SettingNotActive,
        ImmutableTarget,
        ProtectedManagementPath,
        LastManagementPath,
    }

    public sealed class PartnerShareSettingInput
    {
        public Guid PartnerId { get; set; }
        public PartnerSharePrincipalKind? PrincipalKind { get; set; }
        public Guid? UserId { get; set; }
        public Guid? TeamId { get; set; }
        public PartnerShareAccessLevel? AccessLevel { get; set; }
        public string RequestKey { get; set; } = string.Empty;
    }

    public sealed class PartnerShareSettingRecord
    {
        public Guid Id { get; set; }
        public Guid PartnerId { get; set; }
        public PartnerSharePrincipalKind PrincipalKind { get; set; }
        public Guid PrincipalId { get; set; }
        public PartnerShareAccessLevel AccessLevel { get; set; }
        public PartnerShareSettingState State { get; set; }
        public Guid CreatedByUserId { get; set; }
        public bool IsProtectedManagementPath { get; set; }
        /// <summary>
        /// この設定行の保存処理が直接共有を投影したことをサーバーが確認できたか。
        /// 旧経路・外部操作の共有はfalseのままにし、取消で勝手に剥がさない。
        /// </summary>
        public bool IsManagedProjection { get; set; }
        public string RequestKey { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;
    }

    /// <summary>
    /// 呼出者情報は、実際のUser／Team所属とSecurity Roleをサーバー側で解決した後に渡す。
    /// クライアント入力をそのままこの値へ変換してはならない。
    /// </summary>
    public sealed class PartnerShareCaller
    {
        public Guid UserId { get; set; }
        public bool IsApprover { get; set; }
        public PartnerShareAccessLevel? EffectiveAccess { get; set; }
    }

    public sealed class PartnerShareSettingDecision
    {
        private PartnerShareSettingDecision(bool isAllowed, PartnerShareSettingError error)
        {
            IsAllowed = isAllowed;
            Error = error;
        }

        public bool IsAllowed { get; }
        public PartnerShareSettingError Error { get; }

        public static PartnerShareSettingDecision Allow()
            => new PartnerShareSettingDecision(true, PartnerShareSettingError.None);

        public static PartnerShareSettingDecision Deny(PartnerShareSettingError error)
            => new PartnerShareSettingDecision(false, error);
    }

    public static class PartnerShareSettingContract
    {
        public const int RequestKeyMaxLength = 200;
        public static PartnerShareSettingDecision Validate(PartnerShareSettingInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.PartnerId == Guid.Empty) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.PartnerRequired);
            if (!input.PrincipalKind.HasValue) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.PrincipalKindRequired);
            if (input.PrincipalKind.Value != PartnerSharePrincipalKind.User
                && input.PrincipalKind.Value != PartnerSharePrincipalKind.Team)
            {
                return PartnerShareSettingDecision.Deny(PartnerShareSettingError.PrincipalKindInvalid);
            }
            if (!input.AccessLevel.HasValue) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.AccessLevelRequired);
            if (input.AccessLevel.Value != PartnerShareAccessLevel.Read
                && input.AccessLevel.Value != PartnerShareAccessLevel.Write)
            {
                return PartnerShareSettingDecision.Deny(PartnerShareSettingError.AccessLevelInvalid);
            }
            if (string.IsNullOrWhiteSpace(input.RequestKey)) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.RequestKeyRequired);
            if (input.RequestKey.Trim().Length > RequestKeyMaxLength) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.RequestKeyTooLong);

            var hasUser = input.UserId.HasValue;
            var hasTeam = input.TeamId.HasValue;
            if (hasUser && hasTeam) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.PrincipalExclusive);

            if (input.PrincipalKind == PartnerSharePrincipalKind.User)
            {
                if (!hasUser || input.UserId == Guid.Empty) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.PrincipalRequired);
                if (hasTeam) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.PrincipalExclusive);
            }
            else
            {
                if (!hasTeam || input.TeamId == Guid.Empty) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.PrincipalRequired);
                if (hasUser) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.PrincipalExclusive);
            }

            return PartnerShareSettingDecision.Allow();
        }

        public static PartnerShareSettingDecision AuthorizeCreate(
            PartnerSharePhase phase,
            PartnerShareCaller caller,
            PartnerShareSettingInput input)
        {
            var callerValidation = ValidateCaller(caller);
            if (!callerValidation.IsAllowed) return callerValidation;
            var validation = Validate(input);
            if (!validation.IsAllowed) return validation;
            return AuthorizeWrite(phase, caller, input.AccessLevel!.Value);
        }

        public static PartnerShareSettingDecision AuthorizeUpdate(
            PartnerSharePhase phase,
            PartnerShareCaller caller,
            PartnerShareSettingRecord current,
            PartnerShareSettingInput desired)
        {
            if (current == null) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.SettingRequired);
            if (current.Id == Guid.Empty) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.SettingIdRequired);
            var callerValidation = ValidateCaller(caller);
            if (!callerValidation.IsAllowed) return callerValidation;
            var validation = Validate(desired);
            if (!validation.IsAllowed) return validation;
            if (current.State != PartnerShareSettingState.Active) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.SettingNotActive);
            if (current.PartnerId != desired.PartnerId || current.PrincipalKind != desired.PrincipalKind ||
                current.PrincipalId != PrincipalId(desired))
            {
                return PartnerShareSettingDecision.Deny(PartnerShareSettingError.ImmutableTarget);
            }
            if (!caller.IsApprover && current.IsProtectedManagementPath)
            {
                return PartnerShareSettingDecision.Deny(PartnerShareSettingError.ProtectedManagementPath);
            }
            // 変更できるのは、変更前・変更後とも自分と同じか低い共有だけ（2026-09-28ユーザー決定）。
            // 閲覧だけの人が、編集の共有を閲覧へ下げることはできない。
            var higher = current.AccessLevel > desired.AccessLevel!.Value ? current.AccessLevel : desired.AccessLevel.Value;
            return AuthorizeWrite(phase, caller, higher);
        }

        public static PartnerShareSettingDecision AuthorizeRevoke(
            PartnerSharePhase phase,
            PartnerShareCaller caller,
            PartnerShareSettingRecord target,
            IEnumerable<PartnerShareSettingRecord> existing)
        {
            if (target == null) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.SettingRequired);
            if (target.Id == Guid.Empty) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.SettingIdRequired);
            var callerValidation = ValidateCaller(caller);
            if (!callerValidation.IsAllowed) return callerValidation;
            if (target.State != PartnerShareSettingState.Active) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.SettingNotActive);
            if (!caller.IsApprover && target.IsProtectedManagementPath)
            {
                return PartnerShareSettingDecision.Deny(PartnerShareSettingError.ProtectedManagementPath);
            }
            if (target.IsProtectedManagementPath && !HasOtherProtectedPath(existing, target))
            {
                return PartnerShareSettingDecision.Deny(PartnerShareSettingError.LastManagementPath);
            }
            // 取消できるのは、自分と同じか低い共有だけ（2026-09-28ユーザー決定）。
            // 閲覧だけの人は、編集の共有（主担当など）を外せない。
            return AuthorizeWrite(phase, caller, target.AccessLevel);
        }

        public static bool IsSameRequest(PartnerShareSettingRecord existing, PartnerShareSettingInput input)
        {
            if (existing == null || input == null) return false;
            if (!Validate(input).IsAllowed) return false;
            if (existing.Id == Guid.Empty || existing.State != PartnerShareSettingState.Active) return false;
            return string.Equals(existing.RequestKey, input.RequestKey.Trim(), StringComparison.Ordinal) &&
                   string.Equals(existing.ContentHash, PartnerShareSettingFingerprint.Compute(input), StringComparison.Ordinal) &&
                   existing.PartnerId == input.PartnerId &&
                   existing.PrincipalKind == input.PrincipalKind &&
                   existing.PrincipalId == PrincipalId(input) &&
                   existing.AccessLevel == input.AccessLevel!.Value;
        }

        public static Guid PrincipalId(PartnerShareSettingInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return input.PrincipalKind == PartnerSharePrincipalKind.User
                ? input.UserId ?? Guid.Empty
                : input.TeamId ?? Guid.Empty;
        }

        private static PartnerShareSettingDecision AuthorizeWrite(
            PartnerSharePhase phase,
            PartnerShareCaller caller,
            PartnerShareAccessLevel requested)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (phase == PartnerSharePhase.InitialSetup && !caller.IsApprover)
            {
                return PartnerShareSettingDecision.Deny(PartnerShareSettingError.InitialSetupApproverRequired);
            }
            if (caller.IsApprover) return PartnerShareSettingDecision.Allow();
            if (!caller.EffectiveAccess.HasValue)
            {
                return PartnerShareSettingDecision.Deny(PartnerShareSettingError.CompanyAccessRequired);
            }
            if (requested > caller.EffectiveAccess.Value)
            {
                return PartnerShareSettingDecision.Deny(PartnerShareSettingError.AccessExceedsCaller);
            }
            return PartnerShareSettingDecision.Allow();
        }

        private static PartnerShareSettingDecision ValidateCaller(PartnerShareCaller caller)
        {
            if (caller == null) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.CallerRequired);
            if (caller.UserId == Guid.Empty) return PartnerShareSettingDecision.Deny(PartnerShareSettingError.CallerUserRequired);
            return PartnerShareSettingDecision.Allow();
        }

        private static bool HasOtherProtectedPath(
            IEnumerable<PartnerShareSettingRecord> existing,
            PartnerShareSettingRecord target)
        {
            if (existing == null) return false;
            foreach (var setting in existing)
            {
                if (setting.Id != target.Id &&
                    setting.PartnerId == target.PartnerId &&
                    setting.State == PartnerShareSettingState.Active &&
                    setting.IsProtectedManagementPath)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
