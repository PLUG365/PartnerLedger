using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ApprovalServerWriteBypassTests
    {
        private const string ParameterName = "BypassBusinessLogicExecutionStepIds";

        [Theory]
        [InlineData("pl_request", "7379e607-1ab1-f111-aaac-e4fb1eff79c7,21c4bac4-26b1-f111-aaac-e4fb1eff79c7")]
        [InlineData("pl_submissionversion", "7a79e607-1ab1-f111-aaac-e4fb1eff79c7")]
        [InlineData("pl_partner", "bed8d8a4-1db5-f111-aaad-e4fb1eff79c7")]
        [InlineData("pl_contract", "9960c19a-4db2-f111-aaac-e4fb1eff79c7")]
        public void サーバー内部更新は対象テーブルのガードStepだけを飛ばす(string entityName, string expectedStepIds)
        {
            var request = new UpdateRequest { Target = new Entity(entityName, Guid.NewGuid()) };

            ApprovalServerWriteBypass.Apply(request);

            Assert.Equal(expectedStepIds, request.Parameters[ParameterName]);
        }

        [Theory]
        [InlineData("pl_approvallink")]
        [InlineData("pl_operationlog")]
        [InlineData("pl_partnersharesetting")]
        public void 対象外テーブルの更新には指定を付けない(string entityName)
        {
            var request = new UpdateRequest { Target = new Entity(entityName, Guid.NewGuid()) };

            ApprovalServerWriteBypass.Apply(request);

            Assert.False(request.Parameters.Contains(ParameterName));
        }

        [Theory]
        [InlineData("7379e607-1ab1-f111-aaac-e4fb1eff79c7", "StandardApprovalCrudGuardPlugin", "pl_request")]
        [InlineData("7a79e607-1ab1-f111-aaac-e4fb1eff79c7", "StandardApprovalCrudGuardPlugin", "pl_submissionversion")]
        [InlineData("21c4bac4-26b1-f111-aaac-e4fb1eff79c7", "StandardApprovalSubmitPlugin", "pl_request")]
        [InlineData("bed8d8a4-1db5-f111-aaad-e4fb1eff79c7", "PartnerStandardUpdatePlugin", "pl_partner")]
        [InlineData("9960c19a-4db2-f111-aaac-e4fb1eff79c7", "ContractStandardUpdatePlugin", "pl_contract")]
        public void 飛ばすStep_IDはSolution内の該当Update_Stepと一致する(string stepId, string pluginType, string entityName)
        {
            const string updateMessageId = "20bebb1b-ea3e-db11-86a7-000a3a5473e8";
            var path = Path.Combine(FindRepositoryRoot(), "solutions", "PartnerLedger", "SdkMessageProcessingSteps", "{" + stepId + "}.xml");
            Assert.True(File.Exists(path), "Solution内にStepが無い: " + stepId);

            var step = XDocument.Load(path).Root!;
            Assert.Equal(updateMessageId, step.Element("SdkMessageId")!.Value);
            Assert.StartsWith("PartnerLedger.Plugins." + pluginType + ",", step.Element("PluginTypeName")!.Value);
            Assert.Equal(entityName, step.Element("PrimaryEntity")!.Value);
            Assert.Contains(stepId, ApprovalServerWriteBypass.StepIdsFor(entityName));
        }

        [Theory]
        [InlineData(ApprovalServerWriteBypass.ProjectionCreatePreStepId, "9ebdbb1b-ea3e-db11-86a7-000a3a5473e8", "20")]
        [InlineData(ApprovalServerWriteBypass.ProjectionCreatePostStepId, "9ebdbb1b-ea3e-db11-86a7-000a3a5473e8", "40")]
        [InlineData(ApprovalServerWriteBypass.ProjectionUpdatePreStepId, "20bebb1b-ea3e-db11-86a7-000a3a5473e8", "20")]
        public void 共有設定の行を書くときに飛ばすStep_IDはSolution内の投影Stepと一致する(string stepId, string messageId, string stage)
        {
            // 取引先登録時の初期共有・主担当の変更で使う（2026-09-28〜29）。一覧はこのクラスにまとめている（監査#8）。
            var path = Path.Combine(FindRepositoryRoot(), "solutions", "PartnerLedger", "SdkMessageProcessingSteps", "{" + stepId + "}.xml");
            Assert.True(File.Exists(path), "Solution内にStepが無い: " + stepId);

            var step = XDocument.Load(path).Root!;
            Assert.Equal(messageId, step.Element("SdkMessageId")!.Value);
            Assert.Equal(stage, step.Element("Stage")!.Value);
            Assert.StartsWith("PartnerLedger.Plugins.PartnerShareSettingProjectionPlugin,", step.Element("PluginTypeName")!.Value);
            Assert.Equal("pl_partnersharesetting", step.Element("PrimaryEntity")!.Value);
            Assert.Contains(stepId, ApprovalServerWriteBypass.ProjectionCreateStepIds + "," + ApprovalServerWriteBypass.ProjectionUpdateStepIds);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "solutions", "PartnerLedger")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("リポジトリのルートが見つからない。");
        }
    }
}
