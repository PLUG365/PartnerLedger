using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// Server-owned initial OCR job creation for a persisted, current business-card input version.
    /// Canvas callers never provide a job key, attempt number, or job status.
    /// </summary>
    public static class BusinessCardOcrJobCreateContract
    {
        public const string InputVersionEntityName = "pl_cardinputversion";
        public const string CaptureEntityName = "pl_cardcapture";
        public const string JobEntityName = "pl_ocrjob";
        public const string CaptureLookupAttribute = "pl_cardcapturelookup";
        public const string VersionNumberAttribute = "pl_versionnumber";
        public const string InputStateAttribute = "pl_inputstatecode";
        public const string ImageStateAttribute = "pl_imagestatecode";
        public const string CaptureKeyAttribute = "pl_capturekey";
        public const string CaptureStatusAttribute = "pl_capturestatuscode";
        public const string CaptureCurrentVersionAttribute = "pl_currentversionnumber";
        public const string CaptureFileAttribute = "pl_imagefile";
        public const string InputVersionLookupAttribute = "pl_cardinputversionlookup";
        public const string JobKeyAttribute = "pl_jobkey";
        public const string AttemptNumberAttribute = "pl_attemptnumber";
        public const string JobStatusAttribute = "pl_jobstatuscode";
        public const string NameAttribute = "pl_name";
        public const string FixedInputState = "固定済み";
        public const string ImageArrivedState = "画像到着確認済み";
        public const string UnregisteredCaptureStatus = "未登録";
        public const string WaitingStatus = "待機";
        public const int InitialAttemptNumber = 1;
        private const int MaxJobKeyLength = 200;

        public static void EnsureJobExists(Guid inputVersionId, IOrganizationService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (inputVersionId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("名刺入力版IDがないためOCR Jobを作成できません。");
            }

            var inputVersion = RetrieveRequired(
                service,
                InputVersionEntityName,
                inputVersionId,
                new ColumnSet("statecode", "ownerid", CaptureLookupAttribute, VersionNumberAttribute, InputStateAttribute, ImageStateAttribute),
                "保存済み名刺入力版を読めないためOCR Jobを作成できません。");
            EnsureActive(inputVersion, "名刺入力版");

            var inputState = inputVersion.GetAttributeValue<string>(InputStateAttribute);
            if (!string.Equals(inputState, FixedInputState, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("入力版が固定済みではないためOCR Jobを作成できません。");
            }

            var imageState = inputVersion.GetAttributeValue<string>(ImageStateAttribute);
            if (!string.Equals(imageState, ImageArrivedState, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("画像到着が確認できないためOCR Jobを作成できません。");
            }

            var captureReference = inputVersion.GetAttributeValue<EntityReference>(CaptureLookupAttribute);
            if (captureReference == null
                || captureReference.Id == Guid.Empty
                || !string.Equals(captureReference.LogicalName, CaptureEntityName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginExecutionException("親の名刺取込が不正なためOCR Jobを作成できません。");
            }

            var capture = RetrieveRequired(
                service,
                CaptureEntityName,
                captureReference.Id,
                new ColumnSet("statecode", "ownerid", CaptureKeyAttribute, CaptureStatusAttribute, CaptureCurrentVersionAttribute, CaptureFileAttribute),
                "親の名刺取込を読めないためOCR Jobを作成できません。");
            EnsureActive(capture, "名刺取込");

            var captureStatus = capture.GetAttributeValue<string>(CaptureStatusAttribute);
            if (!string.Equals(captureStatus, UnregisteredCaptureStatus, StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("親の名刺取込が未登録ではないためOCR Jobを作成できません。");
            }

            var versionNumber = inputVersion.GetAttributeValue<int?>(VersionNumberAttribute);
            var currentVersionNumber = capture.GetAttributeValue<int?>(CaptureCurrentVersionAttribute);
            if (!versionNumber.HasValue || versionNumber.Value < 1
                || !currentVersionNumber.HasValue || currentVersionNumber.Value != versionNumber.Value)
            {
                throw new InvalidPluginExecutionException("名刺入力版が親の現行版ではないためOCR Jobを作成できません。");
            }

            if (!capture.Attributes.Contains(CaptureFileAttribute) || capture[CaptureFileAttribute] == null)
            {
                throw new InvalidPluginExecutionException("親の名刺画像が存在しないためOCR Jobを作成できません。");
            }

            var captureKey = capture.GetAttributeValue<string>(CaptureKeyAttribute)?.Trim();
            if (string.IsNullOrWhiteSpace(captureKey))
            {
                throw new InvalidPluginExecutionException("親の名刺取込キーを取得できないためOCR Jobを作成できません。");
            }

            var captureOwner = capture.GetAttributeValue<EntityReference>(BusinessCardRecordOwnerContract.OwnerAttribute);
            if (captureOwner == null
                || captureOwner.Id == Guid.Empty
                || (captureOwner.LogicalName != "systemuser" && captureOwner.LogicalName != "team"))
            {
                throw new InvalidPluginExecutionException("親の名刺取込所有者が有効なUserまたはTeamではありません。");
            }

            BusinessCardRecordOwnerContract.EnsureRecordOwner(
                service,
                InputVersionEntityName,
                inputVersionId,
                captureOwner);

            var jobKey = BuildJobKey(captureKey!, versionNumber.Value);
            var existing = FindByJobKey(service, jobKey);
            if (existing.Entities.Count > 0)
            {
                var existingJob = EnsureSameJob(existing, jobKey, inputVersionId);
                BusinessCardRecordOwnerContract.EnsureRecordOwner(service, JobEntityName, existingJob.Id, captureOwner);
                return;
            }

            var job = BuildJob(jobKey, inputVersionId);
            Guid jobId;
            try
            {
                jobId = service.Create(job);
            }
            catch (Exception createException)
            {
                EntityCollection reconciled;
                try
                {
                    reconciled = FindByJobKey(service, jobKey);
                }
                catch (Exception readException)
                {
                    throw new InvalidPluginExecutionException(
                        "OCR Job作成結果を照合できません。再送せず管理者に確認してください.",
                        new AggregateException(createException, readException));
                }

                if (reconciled.Entities.Count == 0)
                {
                    throw new InvalidPluginExecutionException(
                        "OCR Job作成結果を照合できません。再送せず管理者に確認してください.",
                        createException);
                }

                var reconciledJob = EnsureSameJob(reconciled, jobKey, inputVersionId);
                BusinessCardRecordOwnerContract.EnsureRecordOwner(service, JobEntityName, reconciledJob.Id, captureOwner);
                return;
            }

            if (jobId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("OCR Job作成後に行IDを取得できません。");
            }
            BusinessCardRecordOwnerContract.EnsureRecordOwner(service, JobEntityName, jobId, captureOwner);
        }

        public static string BuildJobKey(string captureKey, int versionNumber)
        {
            if (string.IsNullOrWhiteSpace(captureKey) || versionNumber < 1)
            {
                throw new InvalidPluginExecutionException("OCR JobKeyを作成する入力が不正です。");
            }

            var key = $"{captureKey.Trim()}|v{versionNumber}|a{InitialAttemptNumber}";
            if (key.Length > MaxJobKeyLength)
            {
                throw new InvalidPluginExecutionException("導出したOCR JobKeyが許容長を超えています。");
            }
            return key;
        }

        private static Entity RetrieveRequired(
            IOrganizationService service,
            string entityName,
            Guid id,
            ColumnSet columns,
            string errorMessage)
        {
            try
            {
                return service.Retrieve(entityName, id, columns);
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException(errorMessage, exception);
            }
        }

        private static void EnsureActive(Entity entity, string displayName)
        {
            var state = entity.GetAttributeValue<OptionSetValue>("statecode");
            if (state == null || state.Value != 0)
            {
                throw new InvalidPluginExecutionException($"非アクティブな{displayName}のためOCR Jobを作成できません。");
            }
        }

        private static EntityCollection FindByJobKey(IOrganizationService service, string jobKey)
        {
            var query = new QueryExpression(JobEntityName)
            {
                ColumnSet = new ColumnSet("statecode", "ownerid", InputVersionLookupAttribute, JobKeyAttribute, AttemptNumberAttribute),
                TopCount = 2,
            };
            query.Criteria.AddCondition(JobKeyAttribute, ConditionOperator.Equal, jobKey);
            try
            {
                return service.RetrieveMultiple(query);
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException("同じOCR JobKeyの有無を照合できません。Job作成を中止しました。", exception);
            }
        }

        private static Entity EnsureSameJob(EntityCollection matches, string jobKey, Guid inputVersionId)
        {
            if (matches.Entities.Count != 1)
            {
                throw new InvalidPluginExecutionException("同じOCR JobKeyが複数存在するため、処理を中止しました。");
            }

            var existing = matches.Entities[0];
            var state = existing.GetAttributeValue<OptionSetValue>("statecode");
            var inputVersionReference = existing.GetAttributeValue<EntityReference>(InputVersionLookupAttribute);
            var existingKey = existing.GetAttributeValue<string>(JobKeyAttribute);
            var attemptNumber = existing.GetAttributeValue<int?>(AttemptNumberAttribute);
            if (state == null || state.Value != 0
                || inputVersionReference == null
                || !string.Equals(inputVersionReference.LogicalName, InputVersionEntityName, StringComparison.OrdinalIgnoreCase)
                || inputVersionReference.Id != inputVersionId
                || !string.Equals(existingKey, jobKey, StringComparison.Ordinal)
                || attemptNumber != InitialAttemptNumber)
            {
                throw new InvalidPluginExecutionException("同じOCR JobKeyに異なる入力版または管理値が登録済みのため、処理を中止しました。");
            }

            return existing;
        }

        private static Entity BuildJob(string jobKey, Guid inputVersionId)
            => new Entity(JobEntityName)
            {
                [InputVersionLookupAttribute] = new EntityReference(InputVersionEntityName, inputVersionId),
                [NameAttribute] = "OCR-" + jobKey,
                [JobKeyAttribute] = jobKey,
                [AttemptNumberAttribute] = InitialAttemptNumber,
                [JobStatusAttribute] = WaitingStatus,
            };
    }
}
