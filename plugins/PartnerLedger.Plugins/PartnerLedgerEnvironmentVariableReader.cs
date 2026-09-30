using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    public static class PartnerLedgerEnvironmentVariableNames
    {
        public const string ApproverTeamId = "pl_ApproverTeamId";
    }

    /// <summary>
    /// Reads required deployment settings from their Dataverse Environment Variable
    /// Current Value. Default Value is deliberately never read as a fallback.
    /// Plug-ins call this with the SYSTEM service (SystemService), not the caller's service.
    /// </summary>
    public static class PartnerLedgerEnvironmentVariableReader
    {
        public static Guid ReadRequiredGuid(IOrganizationService executionService, string schemaName)
        {
            if (executionService == null) throw new ArgumentNullException(nameof(executionService));
            if (string.IsNullOrWhiteSpace(schemaName)) throw new ArgumentException("Schema name is required.", nameof(schemaName));

            var definitionQuery = new QueryExpression("environmentvariabledefinition")
            {
                ColumnSet = new ColumnSet("schemaname"),
                TopCount = 2,
            };
            definitionQuery.Criteria.AddCondition("schemaname", ConditionOperator.Equal, schemaName);

            // TopCount=2 is intentional: duplicate definitions are an ambiguous
            // deployment and must fail closed rather than choosing one arbitrarily.
            var definitions = executionService.RetrieveMultiple(definitionQuery);
            if (definitions.Entities.Count != 1 || definitions.Entities[0].Id == Guid.Empty)
            {
                throw InvalidSetting(schemaName, "定義がないか、重複しています");
            }

            var valueQuery = new QueryExpression("environmentvariablevalue")
            {
                ColumnSet = new ColumnSet("value"),
                TopCount = 2,
            };
            valueQuery.Criteria.AddCondition(
                "environmentvariabledefinitionid",
                ConditionOperator.Equal,
                definitions.Entities[0].Id);

            // Only the Current Value row is queried. The definition's defaultvalue
            // is never selected, so an unset deployment cannot silently inherit a
            // development or fallback identity.
            var values = executionService.RetrieveMultiple(valueQuery);
            if (values.Entities.Count != 1)
            {
                throw InvalidSetting(schemaName, "Current Valueがないか、重複しています");
            }

            var rawValue = values.Entities[0].GetAttributeValue<string>("value")?.Trim();
            if (string.IsNullOrWhiteSpace(rawValue)
                || !Guid.TryParse(rawValue, out var parsed)
                || parsed == Guid.Empty)
            {
                throw InvalidSetting(schemaName, "Current Valueが空か、GUID形式ではありません");
            }

            return parsed;
        }

        public static Guid ReadRequiredActiveTeamId(IOrganizationService executionService, string schemaName)
        {
            var configuredId = ReadRequiredGuid(executionService, schemaName);
            // Team has no statecode. A resolvable Entra-backed group Team is the
            // required deployment target; owner/access Teams must not be used.
            // 値がEntraグループのオブジェクトIDなら、そのグループのTeamを使う（2026-09-27、
            // インポート試験で取り違えが起きたため）。該当が複数あれば曖昧なので失敗させる。
            var teamId = ResolveTeamIdFromEntraObjectId(executionService, schemaName, configuredId) ?? configuredId;
            var team = executionService.Retrieve("team", teamId, new ColumnSet("teamtype"));
            var teamType = team?.GetAttributeValue<OptionSetValue>("teamtype");
            if (team == null || team.Id != teamId || teamType == null
                || (teamType.Value != 2 && teamType.Value != 3))
            {
                throw InvalidSetting(schemaName, "参照先Teamが存在しないか、EntraグループTeamではありません");
            }

            return teamId;
        }

        private static Guid? ResolveTeamIdFromEntraObjectId(IOrganizationService executionService, string schemaName, Guid objectId)
        {
            var query = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet("teamtype"),
                TopCount = 2,
            };
            query.Criteria.AddCondition("azureactivedirectoryobjectid", ConditionOperator.Equal, objectId);
            var teams = executionService.RetrieveMultiple(query);
            if (teams.Entities.Count == 0) return null;
            if (teams.Entities.Count > 1)
            {
                throw InvalidSetting(schemaName, "同じEntraグループのTeamが複数あり、どれか決められません。DataverseのTeamのIDを設定してください");
            }

            return teams.Entities[0].Id;
        }

        private static InvalidPluginExecutionException InvalidSetting(string schemaName, string reason)
            => new InvalidPluginExecutionException(
                "PartnerLedgerの環境設定 '" + schemaName + "' を使用できません: " + reason + "。");
    }
}
