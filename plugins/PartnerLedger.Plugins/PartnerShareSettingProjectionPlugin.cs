using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// pl_PartnerShareSettingの標準Create／Updateを、認可済みの標準共有要求へ投影する
    /// CreateはPreOperationで入力・認可を検証し、PostOperationで保存済みの共有設定行へ
    /// 親行の共有を投影する。Updateは現行行を参照できるPreOperationで投影する。
    /// いずれも同期実行とし、行保存とGrantAccess／ModifyAccess／RevokeAccessを同一トランザクションへ置く。
    ///
    /// 承認者Teamは環境変数Current Valueから解決し、欠落・無効時は処理を拒否する。
    /// </summary>
    public sealed class PartnerShareSettingProjectionPlugin : PluginBase
    {
        public PartnerShareSettingProjectionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(PartnerShareSettingProjectionPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null) throw new ArgumentNullException(nameof(localPluginContext));

            var context = localPluginContext.PluginExecutionContext;
            var isCreate = string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase);
            var isUpdate = string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase);
            if ((!isCreate && !isUpdate)
                || !string.Equals(
                    context.PrimaryEntityName,
                    PartnerShareSettingRecordFactory.EntityName,
                    StringComparison.Ordinal)
                || isCreate && context.Stage != 20 && context.Stage != 40
                || isUpdate && context.Stage != 20)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定投影の実行コンテキストが不正です。CreateはPreOperation／PostOperation、UpdateはPreOperationだけを許可します。");
            }

            var approverTeamId = PartnerLedgerEnvironmentVariableReader.ReadRequiredActiveTeamId(
                localPluginContext.SystemService,
                PartnerLedgerEnvironmentVariableNames.ApproverTeamId);

            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            if (target == null)
            {
                throw new InvalidPluginExecutionException("共有設定投影のTargetがありません。");
            }

            if (isCreate)
            {
                if (context.Stage == 20)
                {
                    ExecuteCreatePreOperation(localPluginContext, target, approverTeamId);
                }
                else
                {
                    ExecuteCreatePostOperation(localPluginContext, target, approverTeamId);
                }
                return;
            }

            ExecuteUpdate(localPluginContext, target, approverTeamId);
        }

        private void ExecuteCreatePreOperation(ILocalPluginContext localPluginContext, Entity target, Guid approverTeamId)
        {
            var service = localPluginContext.SystemService;
            RemovePlatformGeneratedCreateAttributes(
                target,
                localPluginContext.PluginExecutionContext.InitiatingUserId,
                service);
            var input = PartnerShareSettingStandardInputParser.ParseCreate(target);
            var plan = BuildCreatePlan(localPluginContext, input, approverTeamId);
            EnsureCreatePlanCanPersist(plan);

            var phase = PartnerShareSettingRepository.ResolvePhase(service, input.PartnerId);

            var derived = PartnerShareSettingRecordFactory.BuildCreate(
                phase,
                input,
                approverTeamId,
                plan.IsManagedProjection);
            ApplyAttributes(target, derived);
        }

        private void ExecuteCreatePostOperation(
            ILocalPluginContext localPluginContext,
            Entity target,
            Guid approverTeamId)
        {
            if (target.Id == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定のPostOperation対象IDがありません。");
            }

            var input = PartnerShareSettingStandardInputParser.ParseCreate(
                CreateBusinessInputTarget(target));
            var phase = PartnerShareSettingRepository.ResolvePhase(
                localPluginContext.SystemService,
                input.PartnerId);
            var plan = BuildCreatePlan(localPluginContext, input, approverTeamId, target.Id);
            PartnerShareSettingProjectionBoundary.Execute(
                localPluginContext.SystemService,
                plan);
            CompleteInitialShareSetupIfNeeded(localPluginContext, phase, input, approverTeamId);
        }

        private PartnerShareSettingProjectionPlan BuildCreatePlan(
            ILocalPluginContext localPluginContext,
            PartnerShareSettingInput input,
            Guid approverTeamId,
            Guid excludedSettingId = default)
        {
            var service = localPluginContext.SystemService;
            var initiatingUserId = localPluginContext.PluginExecutionContext.InitiatingUserId;
            var resolvedCaller = PartnerShareSettingAuthorizationResolver.Resolve(
                localPluginContext.InitiatingUserService,
                service,
                initiatingUserId,
                input.PartnerId,
                approverTeamId);

            return PartnerShareSettingProjectionBoundary.Create(
                service,
                initiatingUserId,
                resolvedCaller,
                CreateBusinessInputEntity(input),
                PartnerShareDirectShareProvenance.UntrackedOrExternal,
                excludedSettingId,
                approverTeamId);
        }

        private static void EnsureCreatePlanCanPersist(
            PartnerShareSettingProjectionPlan plan)
        {
            if (!plan.IsAllowed)
            {
                throw new InvalidPluginExecutionException(
                    "共有設定の投影計画が許可されていません: " + plan.Error);
            }

            // 標準Createでは既存行を返して終了できないため、リプレイを新規行として保存しない。
            if (plan.IsReplay)
            {
                throw new InvalidPluginExecutionException(
                    "同じrequest keyの共有設定が既に存在します。既存行を更新するか、別のrequest keyを指定してください。");
            }
        }

        private static void RemovePlatformGeneratedCreateAttributes(
            Entity target,
            Guid initiatingUserId,
            IOrganizationService service)
        {
            // UserOwnedテーブルのPreOperation Targetには、主キー・所属BU・監査代理参照が
            // Dataverse側から入ることがある。主キー・監査代理参照は業務入力へ渡さず、
            // クライアント値としても採用しない。監査列はDataverse標準Createが必要とする
            // 場合があるため、Targetにあるサーバー値を残す。所有者はUserOwnedのCreate必須列なので、呼出者本人のUser参照
            // であることを検証して標準Createへ残す。所有者から派生する内部BUも、呼出者
            // のUserに紐づくBUと一致することを検証して標準Createへ残す。状態などの
            // 認可に関わる列はParserで安全な既定値だけを許可して不正値を拒否する。
            target.Attributes.Remove("pl_partnersharesettingid");
            target.Attributes.Remove("owningteam");
            target.Attributes.Remove("modifiedonbehalfby");
            target.Attributes.Remove("createdonbehalfby");

            if (target.Attributes.TryGetValue("ownerid", out var ownerValue))
            {
                if (!(ownerValue is EntityReference owner)
                    || owner.Id == Guid.Empty
                    || !string.Equals(owner.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase)
                    || owner.Id != initiatingUserId)
                {
                    throw new InvalidPluginExecutionException(
                        "共有設定の所有者を標準Createの呼出者以外へ変更する入力は許可されていません。");
                }

            }

            if (target.Attributes.TryGetValue("owninguser", out var owningUserValue))
            {
                if (!(owningUserValue is EntityReference owningUser)
                    || owningUser.Id == Guid.Empty
                    || !string.Equals(owningUser.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase)
                    || owningUser.Id != initiatingUserId)
                {
                    throw new InvalidPluginExecutionException(
                        "共有設定の内部所有者を標準Createの呼出者以外へ変更する入力は許可されていません。");
                }
            }

            if (target.Attributes.TryGetValue("owningbusinessunit", out var businessUnitValue))
            {
                if (!(businessUnitValue is EntityReference businessUnit)
                    || businessUnit.Id == Guid.Empty
                    || !string.Equals(businessUnit.LogicalName, "businessunit", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidPluginExecutionException(
                        "共有設定の所属BU参照が不正です。");
                }

                var initiatingUser = service.Retrieve(
                    "systemuser",
                    initiatingUserId,
                    new ColumnSet("businessunitid"));
                var expectedBusinessUnit = initiatingUser.GetAttributeValue<EntityReference>("businessunitid");
                if (expectedBusinessUnit == null || expectedBusinessUnit.Id != businessUnit.Id)
                {
                    throw new InvalidPluginExecutionException(
                        "共有設定の所属BUを呼出者のBU以外へ変更する入力は許可されていません。");
                }
            }
        }

        private void ExecuteUpdate(ILocalPluginContext localPluginContext, Entity target, Guid approverTeamId)
        {
            var service = localPluginContext.SystemService;
            var current = PartnerShareSettingRepository.Retrieve(service, target.Id);
            var command = PartnerShareSettingStandardInputParser.ParseUpdate(target, current);
            var phase = PartnerShareSettingRepository.ResolvePhase(service, current.PartnerId);
            var resolvedCaller = PartnerShareSettingAuthorizationResolver.Resolve(
                localPluginContext.InitiatingUserService,
                service,
                localPluginContext.PluginExecutionContext.InitiatingUserId,
                current.PartnerId,
                approverTeamId);

            var plan = PartnerShareSettingProjectionBoundary.Update(
                service,
                localPluginContext.PluginExecutionContext.InitiatingUserId,
                resolvedCaller,
                target,
                current.IsManagedProjection
                    ? PartnerShareDirectShareProvenance.ManagedByPartnerLedger
                    : PartnerShareDirectShareProvenance.UntrackedOrExternal);
            PartnerShareSettingProjectionBoundary.Execute(service, plan);

            var derived = command.IsRevoke
                ? PartnerShareSettingRecordFactory.BuildRevoke(target.Id)
                : PartnerShareSettingRecordFactory.BuildUpdate(
                    target.Id,
                    command.Desired ?? throw new InvalidPluginExecutionException(
                        "共有設定の更新内容が解決できません。"));
            ApplyAttributes(target, derived);

            if (!command.IsRevoke
                && PartnerShareSetupCompletion.ShouldComplete(
                    phase,
                    current,
                    approverTeamId))
            {
                CompleteInitialShareSetup(localPluginContext, current.PartnerId);
            }
        }

        private void CompleteInitialShareSetupIfNeeded(
            ILocalPluginContext localPluginContext,
            PartnerSharePhase phase,
            PartnerShareSettingInput input,
            Guid approverTeamId)
        {
            if (PartnerShareSetupCompletion.ShouldComplete(phase, input, approverTeamId))
            {
                CompleteInitialShareSetup(localPluginContext, input.PartnerId);
            }
        }

        private static void CompleteInitialShareSetup(
            ILocalPluginContext localPluginContext,
            Guid partnerId)
        {
            localPluginContext.SystemService.Update(new Entity(
                PartnerStandardCreateContract.EntityName,
                partnerId)
            {
                [PartnerStandardCreateContract.ShareSetupStatusAttribute] =
                    new OptionSetValue(PartnerStandardCreateContract.ReadyShareSetupStatusCode),
            });
        }

        private static Entity CreateBusinessInputEntity(PartnerShareSettingInput input)
        {
            var entity = new Entity(PartnerShareSettingRecordFactory.EntityName)
            {
                [PartnerShareSettingRecordFactory.PartnerLookupAttribute] =
                    new EntityReference("pl_partner", input.PartnerId),
                [PartnerShareSettingRecordFactory.PrincipalKindAttribute] =
                    new OptionSetValue(input.PrincipalKind == PartnerSharePrincipalKind.User
                        ? PartnerShareSettingRecordFactory.PrincipalKindUserOption
                        : PartnerShareSettingRecordFactory.PrincipalKindTeamOption),
                [PartnerShareSettingRecordFactory.AccessLevelAttribute] =
                    new OptionSetValue(input.AccessLevel == PartnerShareAccessLevel.Read
                        ? PartnerShareSettingRecordFactory.AccessLevelReadOption
                        : PartnerShareSettingRecordFactory.AccessLevelWriteOption),
                [PartnerShareSettingRecordFactory.RequestKeyAttribute] = input.RequestKey,
            };

            if (input.PrincipalKind == PartnerSharePrincipalKind.User)
            {
                entity[PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute] =
                    new EntityReference("systemuser", input.UserId!.Value);
            }
            else
            {
                entity[PartnerShareSettingRecordFactory.TeamPrincipalLookupAttribute] =
                    new EntityReference("team", input.TeamId!.Value);
            }

            return entity;
        }

        private static Entity CreateBusinessInputTarget(Entity target)
        {
            var businessInput = new Entity(
                PartnerShareSettingRecordFactory.EntityName,
                target.Id);
            CopyAttributeIfPresent(
                target,
                businessInput,
                PartnerShareSettingRecordFactory.PartnerLookupAttribute);
            CopyAttributeIfPresent(
                target,
                businessInput,
                PartnerShareSettingRecordFactory.PrincipalKindAttribute);
            CopyAttributeIfPresent(
                target,
                businessInput,
                PartnerShareSettingRecordFactory.UserPrincipalLookupAttribute);
            CopyAttributeIfPresent(
                target,
                businessInput,
                PartnerShareSettingRecordFactory.TeamPrincipalLookupAttribute);
            CopyAttributeIfPresent(
                target,
                businessInput,
                PartnerShareSettingRecordFactory.AccessLevelAttribute);
            CopyAttributeIfPresent(
                target,
                businessInput,
                PartnerShareSettingRecordFactory.RequestKeyAttribute);
            return businessInput;
        }

        private static void CopyAttributeIfPresent(
            Entity source,
            Entity target,
            string attributeName)
        {
            if (source.Attributes.TryGetValue(attributeName, out var value))
            {
                target[attributeName] = value;
            }
        }

        private static void ApplyAttributes(Entity target, Entity source)
        {
            foreach (var attribute in source.Attributes)
            {
                target[attribute.Key] = attribute.Value;
            }
        }

    }
}
