using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class BusinessCardRecordOwnerContractTests
    {
        private static readonly Guid CaptureId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private static readonly Guid InputVersionId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid JobId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        private static readonly Guid ReviewId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        private static readonly EntityReference OldOwner = new EntityReference("systemuser", Guid.Parse("55555555-5555-4555-8555-555555555555"));
        private static readonly EntityReference NewUserOwner = new EntityReference("systemuser", Guid.Parse("66666666-6666-4666-8666-666666666666"));
        private static readonly EntityReference NewTeamOwner = new EntityReference("team", Guid.Parse("77777777-7777-4777-8777-777777777777"));
        private static readonly EntityReference FlowOwner = new EntityReference("systemuser", Guid.Parse("88888888-8888-4888-8888-888888888888"));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Capture所有者変更で関連する全子行だけをUserまたはTeamへ同期する(bool useTeam)
        {
            var service = ReadyService();
            var newOwner = useTeam ? NewTeamOwner : NewUserOwner;

            BusinessCardRecordOwnerContract.EnsureCaptureChildrenOwnedBy(service, CaptureId, newOwner);

            Assert.Equal(newOwner.Id, Owner(service, "pl_cardinputversion", InputVersionId).Id);
            Assert.Equal(newOwner.Id, Owner(service, "pl_ocrjob", JobId).Id);
            Assert.Equal(newOwner.Id, Owner(service, "pl_businesscardreview", ReviewId).Id);
            Assert.Equal(3, service.ExecutedRequests.OfType<AssignRequest>().Count());
            Assert.All(service.ExecutedRequests.OfType<AssignRequest>(), request =>
            {
                Assert.Equal(newOwner.Id, request.Assignee.Id);
                Assert.Contains(request.Target.LogicalName, new[] { "pl_cardinputversion", "pl_ocrjob", "pl_businesscardreview" });
            });
        }

        [Fact]
        public void Capture所有者が同じでも子だけ補正し無関係Captureに触れない()
        {
            var service = ReadyService();
            SeedUnrelatedRows(service);

            BusinessCardRecordOwnerContract.EnsureCaptureChildrenOwnedBy(service, CaptureId, OldOwner);

            Assert.Equal(2, service.ExecutedRequests.OfType<AssignRequest>().Count());
            Assert.Equal(OldOwner.Id, Owner(service, "pl_cardinputversion", InputVersionId).Id);
            Assert.Equal(OldOwner.Id, Owner(service, "pl_ocrjob", JobId).Id);
            Assert.Equal(OldOwner.Id, Owner(service, "pl_businesscardreview", ReviewId).Id);
            Assert.Equal(OldOwner.Id, Owner(service, "pl_cardinputversion", UnrelatedInputVersionId).Id);
        }

        [Fact]
        public void 確認版Create後に親Capture所有者へ移す()
        {
            var service = ReadyService();
            service.Seed(new Entity("pl_businesscardreview", ReviewId)
            {
                ["ownerid"] = new EntityReference(FlowOwner.LogicalName, FlowOwner.Id),
                [BusinessCardRecordOwnerContract.InputVersionLookupAttribute] = new EntityReference("pl_cardinputversion", InputVersionId),
            });

            BusinessCardRecordOwnerContract.EnsureReviewOwnerMatchesCapture(service, ReviewId);

            Assert.Equal(OldOwner.Id, Owner(service, "pl_businesscardreview", ReviewId).Id);
            var assign = Assert.Single(service.ExecutedRequests.OfType<AssignRequest>());
            Assert.Equal("pl_businesscardreview", assign.Target.LogicalName);
            Assert.Equal(OldOwner.Id, assign.Assignee.Id);
        }

        [Fact]
        public void Assign後に環境設定由来の旧所有者直接共有があれば取り消す()
        {
            var service = ReadyService();
            service.RetrievedSharedPrincipalAccesses = new[]
            {
                new PrincipalAccess
                {
                    Principal = new EntityReference(FlowOwner.LogicalName, FlowOwner.Id),
                    AccessMask = AccessRights.ReadAccess | AccessRights.WriteAccess,
                },
            };

            BusinessCardRecordOwnerContract.EnsureRecordOwner(service, "pl_businesscardreview", ReviewId, NewUserOwner);

            var revoke = Assert.Single(service.ExecutedRequests.OfType<RevokeAccessRequest>());
            Assert.Equal("pl_businesscardreview", revoke.Target.LogicalName);
            Assert.Equal(ReviewId, revoke.Target.Id);
            Assert.Equal(FlowOwner.Id, revoke.Revokee.Id);
        }

        [Fact]
        public void 不正な確認版Lookupは所有者を変更しない()
        {
            var service = ReadyService();
            service.Seed(new Entity("pl_businesscardreview", ReviewId)
            {
                ["ownerid"] = new EntityReference(FlowOwner.LogicalName, FlowOwner.Id),
                [BusinessCardRecordOwnerContract.InputVersionLookupAttribute] = new EntityReference("pl_partner", InputVersionId),
            });

            Assert.Throws<InvalidPluginExecutionException>(() =>
                BusinessCardRecordOwnerContract.EnsureReviewOwnerMatchesCapture(service, ReviewId));

            Assert.Empty(service.ExecutedRequests.OfType<AssignRequest>());
            Assert.Equal(FlowOwner.Id, Owner(service, "pl_businesscardreview", ReviewId).Id);
        }

        [Fact]
        public void 子行Assign失敗時はトランザクション全体をロールバックする()
        {
            var service = ReadyService();
            service.BeforeExecute = request =>
            {
                if (request is AssignRequest assign && assign.Target.LogicalName == "pl_businesscardreview")
                    throw new InvalidPluginExecutionException("simulated assign failure");
            };

            Assert.Throws<InvalidPluginExecutionException>(() => service.RunInTransaction(() =>
            {
                BusinessCardRecordOwnerContract.EnsureCaptureChildrenOwnedBy(service, CaptureId, NewUserOwner);
                service.Execute(new AssignRequest
                {
                    Target = new EntityReference("pl_cardcapture", CaptureId),
                    Assignee = new EntityReference(NewUserOwner.LogicalName, NewUserOwner.Id),
                });
                return true;
            }));

            Assert.Equal(OldOwner.Id, Owner(service, "pl_cardinputversion", InputVersionId).Id);
            Assert.Equal(FlowOwner.Id, Owner(service, "pl_ocrjob", JobId).Id);
            Assert.Equal(FlowOwner.Id, Owner(service, "pl_businesscardreview", ReviewId).Id);
            Assert.Empty(service.ExecutedRequests);
        }

        private static Guid UnrelatedInputVersionId => Guid.Parse("99999999-9999-4999-8999-999999999999");

        private static FakeOrganizationService ReadyService()
        {
            var service = new FakeOrganizationService();
            service.Seed(new Entity("pl_cardcapture", CaptureId)
            {
                ["ownerid"] = new EntityReference(OldOwner.LogicalName, OldOwner.Id),
            });
            service.Seed(new Entity("pl_cardinputversion", InputVersionId)
            {
                ["ownerid"] = new EntityReference(OldOwner.LogicalName, OldOwner.Id),
                [BusinessCardRecordOwnerContract.CaptureLookupAttribute] = new EntityReference("pl_cardcapture", CaptureId),
            });
            service.Seed(new Entity("pl_ocrjob", JobId)
            {
                ["ownerid"] = new EntityReference(FlowOwner.LogicalName, FlowOwner.Id),
                [BusinessCardRecordOwnerContract.InputVersionLookupAttribute] = new EntityReference("pl_cardinputversion", InputVersionId),
            });
            service.Seed(new Entity("pl_businesscardreview", ReviewId)
            {
                ["ownerid"] = new EntityReference(FlowOwner.LogicalName, FlowOwner.Id),
                [BusinessCardRecordOwnerContract.InputVersionLookupAttribute] = new EntityReference("pl_cardinputversion", InputVersionId),
            });
            return service;
        }

        private static void SeedUnrelatedRows(FakeOrganizationService service)
        {
            var unrelatedCaptureId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
            service.Seed(new Entity("pl_cardcapture", unrelatedCaptureId)
            {
                ["ownerid"] = new EntityReference(NewUserOwner.LogicalName, NewUserOwner.Id),
            });
            service.Seed(new Entity("pl_cardinputversion", UnrelatedInputVersionId)
            {
                ["ownerid"] = new EntityReference(OldOwner.LogicalName, OldOwner.Id),
                [BusinessCardRecordOwnerContract.CaptureLookupAttribute] = new EntityReference("pl_cardcapture", unrelatedCaptureId),
            });
        }

        private static EntityReference Owner(FakeOrganizationService service, string entityName, Guid id)
            => service.Retrieve(entityName, id, new ColumnSet("ownerid")).GetAttributeValue<EntityReference>("ownerid")!;
    }
}
