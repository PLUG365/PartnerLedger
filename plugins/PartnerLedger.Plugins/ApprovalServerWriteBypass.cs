using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 承認系Plugin（提出・判定・取消・反映）が同一トランザクション内で行うネスト更新に、
    /// 利用者入力用のガードStepだけを飛ばすDataverse標準の指定を付ける。
    /// 使えるのはprvBypassCustomBusinessLogicを持つ主体（既定ではシステム管理者）とSYSTEMだけで、
    /// PluginはSYSTEMのサービス（SystemService）からこの指定を付ける（2026-09-27に開発環境で確認）。
    /// 利用者の直接Updateはこの指定を付けられないため従来どおりガードで検査される。
    /// SharedVariablesのマーカーは実Pipelineの子Updateから見えないことがあるため、これに頼らない。
    /// Step IDはSolution importで保持される登録IDで、パッケージテストでSolutionと照合する。
    /// 取引先登録時の初期共有（PartnerAccessBootstrapPlugin.ProjectionCreateStepIds）も、共有設定投影の
    /// Create Stepを飛ばしている。どのStepを飛ばしているかを確かめるときは、両方を見る。
    /// </summary>
    public static class ApprovalServerWriteBypass
    {
        public const string ParameterName = "BypassBusinessLogicExecutionStepIds";

        public const string RequestCrudGuardUpdateStepId = "7379e607-1ab1-f111-aaac-e4fb1eff79c7";
        public const string SubmissionVersionCrudGuardUpdateStepId = "7a79e607-1ab1-f111-aaac-e4fb1eff79c7";
        public const string StandardApprovalSubmitStepId = "21c4bac4-26b1-f111-aaac-e4fb1eff79c7";
        public const string PartnerStandardUpdateStepId = "bed8d8a4-1db5-f111-aaad-e4fb1eff79c7";
        public const string ContractStandardUpdateStepId = "9960c19a-4db2-f111-aaac-e4fb1eff79c7";

        // 共有設定投影（PartnerShareSettingProjectionPlugin）のStep。サーバーが自分で計画・投影した共有設定の行を
        // 書くときだけ飛ばす（取引先登録時の初期共有、主担当の変更。2026-09-28〜29）。
        public const string ProjectionCreatePreStepId = "5df0f894-15b0-f111-aaac-e4fb1eff79c7";
        public const string ProjectionCreatePostStepId = "a3fd50d3-20b0-f111-aaac-e4fb1eff79c7";
        public const string ProjectionUpdatePreStepId = "5ff0f894-15b0-f111-aaac-e4fb1eff79c7";
        public const string ProjectionCreateStepIds = ProjectionCreatePreStepId + "," + ProjectionCreatePostStepId;
        public const string ProjectionUpdateStepIds = ProjectionUpdatePreStepId;

        private static readonly IDictionary<string, string> StepIdsByEntity = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 申請の状態更新は、入力ガードと提出Plugin（状態列でFilter）の両方を通さない。
            [StandardApprovalCrudContract.RequestEntityName] = RequestCrudGuardUpdateStepId + "," + StandardApprovalSubmitStepId,
            [StandardApprovalCrudContract.SubmissionVersionEntityName] = SubmissionVersionCrudGuardUpdateStepId,
            [StandardApprovalCrudContract.PartnerEntityName] = PartnerStandardUpdateStepId,
            [StandardApprovalCrudContract.ContractEntityName] = ContractStandardUpdateStepId,
        };

        public static string StepIdsFor(string entityName)
            => StepIdsByEntity.TryGetValue(entityName, out var stepIds) ? stepIds : string.Empty;

        public static void Apply(UpdateRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var entityName = request.Target?.LogicalName;
            if (entityName == null || !StepIdsByEntity.TryGetValue(entityName, out var stepIds))
                return;

            request.Parameters[ParameterName] = stepIds;
        }
    }
}
