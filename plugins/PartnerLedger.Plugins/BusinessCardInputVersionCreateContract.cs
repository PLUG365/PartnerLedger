using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    public enum BusinessCardInputVersionCreateValidationError
    {
        None,
        TargetRequired,
        TargetEntityInvalid,
        UnsupportedAttribute,
        ForbiddenAuthorityInput,
        CaptureLookupRequired,
        CaptureLookupTypeInvalid,
        CaptureNotReadable,
        CaptureInactive,
        CaptureStatusInvalid,
        CaptureVersionInvalid,
        ImageNotAvailable,
        DuplicateCurrentVersion,
    }

    /// <summary>
    /// 名刺画像の標準File Upload後に作る現行入力版のCreate境界。
    /// 呼出元は親Captureだけを指定し、版番号・入力状態・画像状態・登録日時・表示名は
    /// サーバーが親CaptureとFile列から導出する。Custom APIは使わない。
    /// </summary>
    public static class BusinessCardInputVersionCreateContract
    {
        public const string EntityName = "pl_cardinputversion";
        public const string CaptureEntityName = "pl_cardcapture";
        public const string CaptureLookupAttribute = "pl_cardcapturelookup";
        public const string CaptureKeyAttribute = "pl_capturekey";
        public const string CaptureStatusAttribute = "pl_capturestatuscode";
        public const string CaptureCurrentVersionAttribute = "pl_currentversionnumber";
        public const string CaptureFileAttribute = "pl_imagefile";
        public const string VersionNumberAttribute = "pl_versionnumber";
        public const string InputStateAttribute = "pl_inputstatecode";
        public const string ImageStateAttribute = "pl_imagestatecode";
        public const string RegisteredAtAttribute = "pl_registeredat";
        public const string NameAttribute = "pl_name";
        public const string CaptureInitialStatus = "未登録";
        public const string FixedInputState = "固定済み";
        public const string ImageArrivedState = "画像到着確認済み";

        private static readonly HashSet<string> ForbiddenAuthorityAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            VersionNumberAttribute,
            InputStateAttribute,
            ImageStateAttribute,
            RegisteredAtAttribute,
            "ownerid",
            "statecode",
            "statuscode",
            "createdby",
            "createdon",
            "createdonbehalfby",
            "modifiedby",
            "modifiedon",
            "modifiedonbehalfby",
            "owningbusinessunit",
            "owningteam",
            "owninguser",
            "overriddencreatedon",
            "importsequencenumber",
            "timezoneruleversionnumber",
            "utcconversiontimezonecode",
        };

        public static BusinessCardInputVersionCreateValidationResult Validate(Entity? target)
        {
            if (target == null) return Invalid(BusinessCardInputVersionCreateValidationError.TargetRequired);
            if (!string.Equals(target.LogicalName, EntityName, StringComparison.Ordinal))
            {
                return Invalid(BusinessCardInputVersionCreateValidationError.TargetEntityInvalid);
            }

            foreach (var attributeName in target.Attributes.Keys)
            {
                if (ForbiddenAuthorityAttributes.Contains(attributeName))
                {
                    return Invalid(BusinessCardInputVersionCreateValidationError.ForbiddenAuthorityInput);
                }

                if (!string.Equals(attributeName, CaptureLookupAttribute, StringComparison.Ordinal))
                {
                    return Invalid(BusinessCardInputVersionCreateValidationError.UnsupportedAttribute);
                }
            }

            if (!target.Attributes.Contains(CaptureLookupAttribute) || target[CaptureLookupAttribute] == null)
            {
                return Invalid(BusinessCardInputVersionCreateValidationError.CaptureLookupRequired);
            }

            if (!(target[CaptureLookupAttribute] is EntityReference captureReference)
                || captureReference.Id == Guid.Empty
                || !string.Equals(captureReference.LogicalName, CaptureEntityName, StringComparison.OrdinalIgnoreCase))
            {
                return Invalid(BusinessCardInputVersionCreateValidationError.CaptureLookupTypeInvalid);
            }

            return Valid();
        }

        public static void ValidateAndApply(
            Entity? target,
            IOrganizationService service,
            DateTime registeredAtUtc)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            var validation = Validate(target);
            if (!validation.IsValid)
            {
                throw new InvalidPluginExecutionException("名刺入力版Createの入力が不正です: " + validation.Error);
            }

            var captureReference = target!.GetAttributeValue<EntityReference>(CaptureLookupAttribute)!;
            Entity capture;
            try
            {
                capture = service.Retrieve(
                    CaptureEntityName,
                    captureReference.Id,
                    new ColumnSet(
                        "statecode",
                        CaptureKeyAttribute,
                        CaptureStatusAttribute,
                        CaptureCurrentVersionAttribute,
                        CaptureFileAttribute));
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(
                    "親の名刺取込を読み取れないため、入力版を作成できません。", exception);
            }

            EnsureCaptureActive(capture);
            EnsureCaptureReady(capture);
            var versionNumber = GetCurrentVersion(capture);
            EnsureNoActiveDuplicate(service, captureReference.Id, versionNumber);

            var captureKey = capture.GetAttributeValue<string>(CaptureKeyAttribute);
            if (string.IsNullOrWhiteSpace(captureKey))
            {
                throw new InvalidPluginExecutionException("親の名刺取込キーを取得できないため、入力版を作成できません。");
            }

            var now = registeredAtUtc.Kind == DateTimeKind.Utc
                ? registeredAtUtc
                : registeredAtUtc.ToUniversalTime();
            target[VersionNumberAttribute] = versionNumber;
            target[InputStateAttribute] = FixedInputState;
            target[ImageStateAttribute] = ImageArrivedState;
            target[RegisteredAtAttribute] = now;
            target[NameAttribute] = $"名刺入力版 {captureKey.Trim()} v{versionNumber}";
        }

        private static void EnsureCaptureActive(Entity capture)
        {
            var state = capture.GetAttributeValue<OptionSetValue>("statecode");
            if (state == null || state.Value != 0)
            {
                throw new InvalidPluginExecutionException("非アクティブな名刺取込へ入力版を作成できません。");
            }
        }

        private static void EnsureCaptureReady(Entity capture)
        {
            var status = capture.GetAttributeValue<string>(CaptureStatusAttribute);
            if (!string.Equals(status, CaptureInitialStatus, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException(
                    "名刺取込が未登録ではないため、入力版を作成できません: " + (status ?? "未設定"));
            }

            if (!capture.Attributes.Contains(CaptureFileAttribute) || capture[CaptureFileAttribute] == null)
            {
                throw new InvalidPluginExecutionException("名刺画像が到着していないため、入力版を作成できません。");
            }
        }

        private static int GetCurrentVersion(Entity capture)
        {
            var version = capture.GetAttributeValue<int?>(CaptureCurrentVersionAttribute);
            if (!version.HasValue || version.Value < 1)
            {
                throw new InvalidPluginExecutionException("名刺取込の現行版番号が不正です。");
            }
            return version.Value;
        }

        private static void EnsureNoActiveDuplicate(
            IOrganizationService service,
            Guid captureId,
            int versionNumber)
        {
            var query = new QueryExpression(EntityName)
            {
                ColumnSet = new ColumnSet(VersionNumberAttribute),
                TopCount = 2,
            };
            query.Criteria.AddCondition(CaptureLookupAttribute, ConditionOperator.Equal, captureId);
            query.Criteria.AddCondition(VersionNumberAttribute, ConditionOperator.Equal, versionNumber);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            EntityCollection existing;
            try
            {
                existing = service.RetrieveMultiple(query);
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(
                    "同じ入力版の存在を確認できないため、Createを中止しました。", exception);
            }

            if (existing.Entities.Count > 0)
            {
                throw new InvalidPluginExecutionException(
                    "同じ名刺取込の現行入力版は既に存在します。再送せず、既存行を照合してください。");
            }
        }

        private static BusinessCardInputVersionCreateValidationResult Valid()
            => new BusinessCardInputVersionCreateValidationResult
            {
                IsValid = true,
                Error = BusinessCardInputVersionCreateValidationError.None,
            };

        private static BusinessCardInputVersionCreateValidationResult Invalid(
            BusinessCardInputVersionCreateValidationError error)
            => new BusinessCardInputVersionCreateValidationResult
            {
                IsValid = false,
                Error = error,
            };
    }

    public sealed class BusinessCardInputVersionCreateValidationResult
    {
        public bool IsValid { get; internal set; }
        public BusinessCardInputVersionCreateValidationError Error { get; internal set; }
    }
}
