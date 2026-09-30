using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class BusinessCardOcrJobCreateContractTests
    {
        private static readonly Guid CaptureId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid InputVersionId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        private static readonly EntityReference CaptureOwner = new EntityReference("systemuser", Guid.Parse("88888888-8888-4888-8888-888888888888"));
        private static readonly EntityReference OriginalInputOwner = new EntityReference("systemuser", Guid.Parse("99999999-9999-4999-8999-999999999999"));
        private static readonly EntityReference ExecutionOwner = new EntityReference("systemuser", Guid.Parse("77777777-7777-4777-8777-777777777777"));
        private const string CaptureKey = "R12-D-TEST-01";
        private const string JobKey = CaptureKey + "|v1|a1";

        [Fact]
        public void 画像到着済みの最新固定版からJobの管理値をサーバー導出する()
        {
            var service = ReadyService();

            BusinessCardOcrJobCreateContract.EnsureJobExists(InputVersionId, service);

            var job = Assert.Single(ReadJobs(service).Entities);
            Assert.Equal(new EntityReference("pl_cardinputversion", InputVersionId), job.GetAttributeValue<EntityReference>(BusinessCardOcrJobCreateContract.InputVersionLookupAttribute));
            Assert.Equal(JobKey, job.GetAttributeValue<string>(BusinessCardOcrJobCreateContract.JobKeyAttribute));
            Assert.Equal(1, job.GetAttributeValue<int>(BusinessCardOcrJobCreateContract.AttemptNumberAttribute));
            Assert.Equal(BusinessCardOcrJobCreateContract.WaitingStatus, job.GetAttributeValue<string>(BusinessCardOcrJobCreateContract.JobStatusAttribute));
            Assert.Equal("OCR-" + JobKey, job.GetAttributeValue<string>(BusinessCardOcrJobCreateContract.NameAttribute));
            Assert.Equal(CaptureOwner.Id, job.GetAttributeValue<EntityReference>("ownerid")?.Id);
            Assert.Equal(CaptureOwner.Id, service.Retrieve("pl_cardinputversion", InputVersionId, new ColumnSet("ownerid")).GetAttributeValue<EntityReference>("ownerid")?.Id);
            Assert.Equal(2, service.ExecutedRequests.OfType<AssignRequest>().Count());
        }

        [Theory]
        [InlineData("入力中", "画像到着確認済み")]
        [InlineData("固定済み", "未到着")]
        public void 固定済み画像到着済みでないInputVersionはJobを作らない(string inputState, string imageState)
        {
            var service = ReadyService(inputState: inputState, imageState: imageState);

            Assert.Throws<InvalidPluginExecutionException>(() =>
                BusinessCardOcrJobCreateContract.EnsureJobExists(InputVersionId, service));

            Assert.Empty(ReadJobs(service).Entities);
        }

        [Fact]
        public void 親Captureの現行版と一致しないInputVersionはJobを作らない()
        {
            var service = ReadyService(currentVersion: 2);

            Assert.Throws<InvalidPluginExecutionException>(() =>
                BusinessCardOcrJobCreateContract.EnsureJobExists(InputVersionId, service));

            Assert.Empty(ReadJobs(service).Entities);
        }

        [Fact]
        public void 同じJobKeyとInputVersionが既にあれば二重作成しない()
        {
            var service = ReadyService();
            service.Seed(ReadyJob());

            BusinessCardOcrJobCreateContract.EnsureJobExists(InputVersionId, service);

            Assert.Single(ReadJobs(service).Entities);
        }

        [Fact]
        public void 同じJobKeyが別InputVersionを指す場合は競合として失敗する()
        {
            var service = ReadyService();
            var conflictingJob = ReadyJob();
            conflictingJob[BusinessCardOcrJobCreateContract.InputVersionLookupAttribute]
                = new EntityReference("pl_cardinputversion", Guid.Parse("44444444-4444-4444-8444-444444444444"));
            service.Seed(conflictingJob);

            Assert.Throws<InvalidPluginExecutionException>(() =>
                BusinessCardOcrJobCreateContract.EnsureJobExists(InputVersionId, service));

            Assert.Single(ReadJobs(service).Entities);
        }

        [Fact]
        public void Create結果不明でも一致するJobKeyを再読取できれば成功扱いする()
        {
            var service = ReadyService();
            service.BeforeCreate = entity =>
            {
                var persisted = Clone(entity);
                persisted["statecode"] = new OptionSetValue(0);
                service.Seed(persisted);
                return new InvalidPluginExecutionException("simulated lost response");
            };

            BusinessCardOcrJobCreateContract.EnsureJobExists(InputVersionId, service);

            Assert.Single(ReadJobs(service).Entities);
        }

        [Fact]
        public void Create失敗後に一致Jobを照合できなければ自動再試行せず失敗する()
        {
            var service = ReadyService();
            service.BeforeCreate = _ => new InvalidPluginExecutionException("simulated create failure");

            var exception = Assert.Throws<InvalidPluginExecutionException>(() =>
                BusinessCardOcrJobCreateContract.EnsureJobExists(InputVersionId, service));

            Assert.Contains("Job作成結果を照合できません", exception.Message);
            Assert.Empty(ReadJobs(service).Entities);
        }

        private static FakeOrganizationService ReadyService(
            string inputState = "固定済み",
            string imageState = "画像到着確認済み",
            int currentVersion = 1)
        {
            var service = new FakeOrganizationService();
            service.DefaultOwner = ExecutionOwner;
            service.Seed(new Entity("pl_cardcapture", CaptureId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["ownerid"] = new EntityReference(CaptureOwner.LogicalName, CaptureOwner.Id),
                ["pl_capturekey"] = CaptureKey,
                ["pl_capturestatuscode"] = "未登録",
                ["pl_currentversionnumber"] = currentVersion,
                ["pl_imagefile"] = Guid.Parse("55555555-5555-4555-8555-555555555555"),
            });
            service.Seed(new Entity("pl_cardinputversion", InputVersionId)
            {
                ["statecode"] = new OptionSetValue(0),
                ["ownerid"] = new EntityReference(OriginalInputOwner.LogicalName, OriginalInputOwner.Id),
                [BusinessCardOcrJobCreateContract.CaptureLookupAttribute] = new EntityReference("pl_cardcapture", CaptureId),
                [BusinessCardOcrJobCreateContract.VersionNumberAttribute] = 1,
                [BusinessCardOcrJobCreateContract.InputStateAttribute] = inputState,
                [BusinessCardOcrJobCreateContract.ImageStateAttribute] = imageState,
            });
            return service;
        }

        private static Entity ReadyJob()
            => new Entity(BusinessCardOcrJobCreateContract.JobEntityName)
            {
                ["statecode"] = new OptionSetValue(0),
                ["ownerid"] = new EntityReference(ExecutionOwner.LogicalName, ExecutionOwner.Id),
                [BusinessCardOcrJobCreateContract.InputVersionLookupAttribute] = new EntityReference("pl_cardinputversion", InputVersionId),
                [BusinessCardOcrJobCreateContract.JobKeyAttribute] = JobKey,
                [BusinessCardOcrJobCreateContract.AttemptNumberAttribute] = 1,
                [BusinessCardOcrJobCreateContract.JobStatusAttribute] = BusinessCardOcrJobCreateContract.WaitingStatus,
            };

        private static EntityCollection ReadJobs(IOrganizationService service)
        {
            var query = new QueryExpression(BusinessCardOcrJobCreateContract.JobEntityName)
            {
                ColumnSet = new ColumnSet(BusinessCardOcrJobCreateContract.JobKeyAttribute),
            };
            query.Criteria.AddCondition(BusinessCardOcrJobCreateContract.JobKeyAttribute, ConditionOperator.Equal, JobKey);
            return service.RetrieveMultiple(query);
        }

        private static Entity Clone(Entity source)
        {
            var clone = new Entity(source.LogicalName, source.Id);
            foreach (var item in source.Attributes)
            {
                clone[item.Key] = item.Value is EntityReference reference
                    ? new EntityReference(reference.LogicalName, reference.Id)
                    : item.Value;
            }
            return clone;
        }
    }
}
