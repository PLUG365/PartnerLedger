using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// Keeps user-owned OCR child rows under the owner of their parent Capture.
    /// Only plug-ins call this contract, through the SYSTEM service (2026-09-27).
    /// </summary>
    public static class BusinessCardRecordOwnerContract
    {
        public const string CaptureEntityName = "pl_cardcapture";
        public const string InputVersionEntityName = "pl_cardinputversion";
        public const string JobEntityName = "pl_ocrjob";
        public const string ReviewEntityName = "pl_businesscardreview";
        public const string CaptureLookupAttribute = "pl_cardcapturelookup";
        public const string InputVersionLookupAttribute = "pl_cardinputversionlookup";
        public const string OwnerAttribute = "ownerid";

        public static void EnsureRecordOwner(
            IOrganizationService service,
            string entityName,
            Guid recordId,
            EntityReference expectedOwner)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            ValidateRecord(entityName, recordId);
            ValidateOwner(expectedOwner);

            Entity record;
            try
            {
                record = service.Retrieve(entityName, recordId, new ColumnSet(OwnerAttribute));
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException("名刺関連行の所有者を確認できません。", exception);
            }

            var currentOwner = record.GetAttributeValue<EntityReference>(OwnerAttribute);
            ValidateOwner(currentOwner);
            if (SameOwner(currentOwner!, expectedOwner)) return;

            var target = new EntityReference(entityName, recordId);
            try
            {
                service.Execute(new AssignRequest
                {
                    Target = target,
                    Assignee = new EntityReference(expectedOwner.LogicalName, expectedOwner.Id),
                });
                RevokeFormerOwnerShare(service, target, currentOwner!);
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException("名刺関連行を取込所有者へ移せません。処理全体を取り消しました。", exception);
            }
        }

        private static void RevokeFormerOwnerShare(
            IOrganizationService service,
            EntityReference target,
            EntityReference formerOwner)
        {
            RetrieveSharedPrincipalsAndAccessResponse response;
            try
            {
                response = (RetrieveSharedPrincipalsAndAccessResponse)service.Execute(
                    new RetrieveSharedPrincipalsAndAccessRequest { Target = target });
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException("Assign後に旧所有者の直接共有を確認できません。", exception);
            }

            var hasFormerOwnerShare = response.PrincipalAccesses != null
                && response.PrincipalAccesses.Any(access => access.Principal != null && SameOwner(access.Principal, formerOwner));
            if (!hasFormerOwnerShare) return;

            try
            {
                service.Execute(new RevokeAccessRequest
                {
                    Target = target,
                    Revokee = formerOwner,
                });
            }
            catch (Exception exception)
            {
                throw new InvalidPluginExecutionException("Assign後の旧所有者アクセスを解除できません。処理全体を取り消しました。", exception);
            }
        }

        public static void EnsureReviewOwnerMatchesCapture(IOrganizationService service, Guid reviewId)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (reviewId == Guid.Empty) throw new InvalidPluginExecutionException("名刺確認版IDがありません。");

            var review = RetrieveRequired(
                service,
                ReviewEntityName,
                reviewId,
                new ColumnSet(InputVersionLookupAttribute),
                "名刺確認版を読み取れません。");
            var inputVersionReference = review.GetAttributeValue<EntityReference>(InputVersionLookupAttribute);
            if (inputVersionReference == null
                || inputVersionReference.Id == Guid.Empty
                || !string.Equals(inputVersionReference.LogicalName, InputVersionEntityName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginExecutionException("名刺確認版の入力版参照が不正です。");
            }

            var owner = GetCaptureOwnerForInputVersion(service, inputVersionReference.Id);
            EnsureRecordOwner(service, ReviewEntityName, reviewId, owner);
        }

        public static EntityReference GetCaptureOwnerForInputVersion(IOrganizationService service, Guid inputVersionId)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (inputVersionId == Guid.Empty) throw new InvalidPluginExecutionException("名刺入力版IDがありません。");

            var inputVersion = RetrieveRequired(
                service,
                InputVersionEntityName,
                inputVersionId,
                new ColumnSet(CaptureLookupAttribute),
                "名刺入力版を読み取れません。");
            var captureReference = inputVersion.GetAttributeValue<EntityReference>(CaptureLookupAttribute);
            if (captureReference == null
                || captureReference.Id == Guid.Empty
                || !string.Equals(captureReference.LogicalName, CaptureEntityName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginExecutionException("名刺入力版の親取込参照が不正です。");
            }

            var capture = RetrieveRequired(
                service,
                CaptureEntityName,
                captureReference.Id,
                new ColumnSet(OwnerAttribute),
                "親の名刺取込を読み取れません。");
            var owner = capture.GetAttributeValue<EntityReference>(OwnerAttribute);
            ValidateOwner(owner);
            return new EntityReference(owner!.LogicalName, owner.Id);
        }

        public static void EnsureCaptureChildrenOwnedBy(
            IOrganizationService service,
            Guid captureId,
            EntityReference newOwner)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (captureId == Guid.Empty) throw new InvalidPluginExecutionException("名刺取込IDがありません。");
            ValidateOwner(newOwner);

            RetrieveRequired(
                service,
                CaptureEntityName,
                captureId,
                new ColumnSet(OwnerAttribute),
                "所有者変更対象の名刺取込を読み取れません。");

            var inputVersions = RetrieveAll(service, InputVersionEntityName, CaptureLookupAttribute, captureId);
            foreach (var inputVersion in inputVersions)
            {
                EnsureRecordOwner(service, InputVersionEntityName, inputVersion.Id, newOwner);
                EnsureRelatedRowsOwnedBy(service, JobEntityName, InputVersionLookupAttribute, inputVersion.Id, newOwner);
                EnsureRelatedRowsOwnedBy(service, ReviewEntityName, InputVersionLookupAttribute, inputVersion.Id, newOwner);
            }
        }

        private static void EnsureRelatedRowsOwnedBy(
            IOrganizationService service,
            string entityName,
            string lookupAttribute,
            Guid inputVersionId,
            EntityReference newOwner)
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(OwnerAttribute),
            };
            query.Criteria.AddCondition(lookupAttribute, ConditionOperator.Equal, inputVersionId);
            foreach (var row in RetrieveAll(service, query))
            {
                EnsureRecordOwner(service, entityName, row.Id, newOwner);
            }
        }

        private static List<Entity> RetrieveAll(
            IOrganizationService service,
            string entityName,
            string lookupAttribute,
            Guid parentId)
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(OwnerAttribute),
            };
            query.Criteria.AddCondition(lookupAttribute, ConditionOperator.Equal, parentId);
            return RetrieveAll(service, query);
        }

        private static List<Entity> RetrieveAll(IOrganizationService service, QueryExpression query)
        {
            var rows = new List<Entity>();
            query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            while (true)
            {
                EntityCollection page;
                try
                {
                    page = service.RetrieveMultiple(query);
                }
                catch (Exception exception)
                {
                    throw new InvalidPluginExecutionException("名刺関連行を列挙できません。", exception);
                }

                rows.AddRange(page.Entities);
                if (!page.MoreRecords) return rows;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
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

        private static void ValidateRecord(string entityName, Guid recordId)
        {
            if (recordId == Guid.Empty
                || (!string.Equals(entityName, InputVersionEntityName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(entityName, JobEntityName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(entityName, ReviewEntityName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidPluginExecutionException("名刺関連行の対象が不正です。");
            }
        }

        private static void ValidateOwner(EntityReference? owner)
        {
            if (owner == null
                || owner.Id == Guid.Empty
                || (owner.LogicalName != "systemuser" && owner.LogicalName != "team"))
            {
                throw new InvalidPluginExecutionException("名刺取込の所有者は有効なUserまたはTeamである必要があります。");
            }
        }

        private static bool SameOwner(EntityReference left, EntityReference right)
            => left.Id == right.Id && string.Equals(left.LogicalName, right.LogicalName, StringComparison.OrdinalIgnoreCase);
    }
}
