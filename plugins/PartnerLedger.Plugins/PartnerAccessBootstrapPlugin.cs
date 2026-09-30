using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 標準pl_partner CreateのPostOperationに登録する。新規取引先の初期共有を、登録と同時に完了する
    /// （2026-09-28ユーザー決定。以前は承認者がアプリで承認者Teamを閲覧で追加して完了していた）。
    /// 同じトランザクションの中で、承認者Teamの保護行・主担当の行を作り、共有し、共有設定状態を
    /// 「設定完了」にする。どれかが失敗すると登録ごとロールバックする。
    /// 旧pl_RegisterPartner経路の互換Bootstrap（旧pl_AccessGrantの作成）は2026-09-27に削除した。
    /// </summary>
    public class PartnerAccessBootstrapPlugin : PluginBase
    {
        public const string BypassParameterName = ApprovalServerWriteBypass.ParameterName;

        // 飛ばすStepの一覧はApprovalServerWriteBypassにまとめている（監査#8）。
        public const string ProjectionCreateStepIds = ApprovalServerWriteBypass.ProjectionCreateStepIds;

        public PartnerAccessBootstrapPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(PartnerAccessBootstrapPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            var service = localPluginContext.SystemService;
            // 共有の前に配置の設定を検査し、設定が欠けたまま途中まで共有しない。
            var approverTeamId = PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(
                service,
                PartnerLedgerEnvironmentVariableNames.ApproverTeamId);

            if (!context.OutputParameters.Contains("id") || context.OutputParameters["id"] is not Guid partnerId)
            {
                throw new InvalidPluginExecutionException(
                    "新規取引先のPostOperation対象IDがありません。初期共有を実行できません。");
            }

            // 主担当は登録時の検査（PartnerCreateGuardPlugin）で必須・サーバー設定済み。登録後の変更は、
            // 主担当の変更（申請→承認、または直接）でPartnerMainOwnerChangeが共有を整える（2026-09-29）。
            var partner = service.Retrieve(
                PartnerStandardCreateContract.EntityName,
                partnerId,
                new ColumnSet(PartnerStandardCreateContract.MainOwnerAttribute));
            var mainOwner = partner.GetAttributeValue<EntityReference>(PartnerStandardCreateContract.MainOwnerAttribute);

            CompleteInitialShareSetup(
                service,
                partnerId,
                context.InitiatingUserId,
                mainOwner?.Id ?? Guid.Empty,
                approverTeamId);
        }

        /// <summary>
        /// 初期共有を完了する。順番は事前確認（2026-09-28、開発環境）で決めた：
        /// 行を先に作り、その後に取引先を共有すると、共有が子の行にも連鎖して、共有された人が行を読める。
        /// ①承認者Teamの保護行（Read）②主担当が登録者と別なら主担当の行（Write）③主担当へ共有
        /// ④承認者TeamへRead共有 ⑤共有設定状態を「設定完了」へ。⑤はバイパスを付けず、
        /// 共有状態ガードが保護行と直接共有を再検証する（「設定完了」への入口はガードだけ）。
        /// </summary>
        public static void CompleteInitialShareSetup(
            IOrganizationService service,
            Guid partnerId,
            Guid registrantId,
            Guid mainOwnerId,
            Guid approverTeamId)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (partnerId == Guid.Empty) throw new InvalidPluginExecutionException("新規取引先IDがありません。");
            if (registrantId == Guid.Empty) throw new InvalidPluginExecutionException("新規取引先の登録者がありません。");
            if (mainOwnerId == Guid.Empty) throw new InvalidPluginExecutionException("新規取引先の主担当がありません。");
            if (approverTeamId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("承認者Teamが設定されていません。");
            }

            // Teamの存在を先に検査する。存在しない設定で行や共有を作り始めない。
            service.Retrieve("team", approverTeamId, new ColumnSet("teamid"));

            var sharesWithMainOwner = mainOwnerId != registrantId;
            if (sharesWithMainOwner)
            {
                RequireShareableMainOwner(service, mainOwnerId);
            }

            CreateBootstrapSetting(service, registrantId, approverTeamId, new PartnerShareSettingInput
            {
                PartnerId = partnerId,
                PrincipalKind = PartnerSharePrincipalKind.Team,
                TeamId = approverTeamId,
                AccessLevel = PartnerShareAccessLevel.Read,
                RequestKey = ApproverRequestKey(partnerId),
            });

            if (sharesWithMainOwner)
            {
                CreateBootstrapSetting(service, registrantId, approverTeamId, new PartnerShareSettingInput
                {
                    PartnerId = partnerId,
                    PrincipalKind = PartnerSharePrincipalKind.User,
                    UserId = mainOwnerId,
                    AccessLevel = PartnerShareAccessLevel.Write,
                    RequestKey = MainOwnerRequestKey(partnerId),
                });
                Grant(service, partnerId, new EntityReference("systemuser", mainOwnerId),
                    PartnerShareSettingProjection.RightsFor(PartnerShareAccessLevel.Write));
            }

            Grant(service, partnerId, new EntityReference("team", approverTeamId),
                PartnerShareSettingProjection.RightsFor(PartnerShareAccessLevel.Read));

            service.Update(new Entity(PartnerStandardCreateContract.EntityName, partnerId)
            {
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] =
                    new OptionSetValue(PartnerStandardCreateContract.ReadyShareSetupStatusCode),
            });
        }

        // 要求キーの代替キーは環境全体で一意。固定値だと、他の人が自分の行に同じキーを先に付けて
        // この取引先の登録を失敗させられるため、乱数を足す（独立監査#1、2026-09-28）。
        // 同じ取引先に保護行が2つできないことは、共有状態ガード（保護行ちょうど1つ・一度だけ）が守る。
        public static string ApproverRequestKey(Guid partnerId)
            => "PartnerLedger-Bootstrap-Approver-" + partnerId.ToString("D") + "-" + Guid.NewGuid().ToString("N");

        public static string MainOwnerRequestKey(Guid partnerId)
            => "PartnerLedger-Bootstrap-MainOwner-" + partnerId.ToString("D") + "-" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// 主担当が通常の有効な利用者か、書き込みの前に確かめる（独立監査#2、2026-09-28）。
        /// Microsoftのサポート用アカウントなどは共有そのものがDataverseに拒否され、分かりにくいエラーで
        /// 登録ごと失敗するため、先に分かる文言で拒否する。
        /// </summary>
        private static void RequireShareableMainOwner(IOrganizationService service, Guid mainOwnerId)
        {
            if (!PartnerMainOwnerChange.IsShareableUser(service, mainOwnerId))
            {
                throw new InvalidPluginExecutionException(
                    "主担当に選んだユーザーには共有できません（無効なユーザーや、Microsoftのサポート用アカウントなど）。主担当には通常の利用者を選んでください。");
            }
        }

        private static void CreateBootstrapSetting(
            IOrganizationService service,
            Guid registrantId,
            Guid approverTeamId,
            PartnerShareSettingInput input)
        {
            // 手動の経路（投影のCreate PreOperation）と同じ部品で導出する。初期設定中の承認者Teamの行だけが保護される。
            var row = PartnerShareSettingRecordFactory.BuildCreate(
                PartnerSharePhase.InitialSetup,
                input,
                approverTeamId,
                isManagedProjection: true);
            // 所有者は登録者にする。SYSTEMのままだと、登録者が共有設定タブで行を読めない。
            row["ownerid"] = new EntityReference("systemuser", registrantId);

            var request = new CreateRequest { Target = row };
            request.Parameters[BypassParameterName] = ProjectionCreateStepIds;
            service.Execute(request);
        }

        private static void Grant(IOrganizationService service, Guid partnerId, EntityReference principal, AccessRights rights)
        {
            service.Execute(new GrantAccessRequest
            {
                Target = new EntityReference(PartnerStandardCreateContract.EntityName, partnerId),
                PrincipalAccess = new PrincipalAccess
                {
                    Principal = principal,
                    AccessMask = rights,
                },
            });
        }
    }
}
