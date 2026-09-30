using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 独立監査C-7対応: 業務操作者（呼び出し元）と操作結果をpl_OperationLogへ記録する。
    /// プラグインの書込みは常にSystemService（SYSTEM）で行うため、
    /// createdby/modifiedbyだけでは「誰が」実行を依頼したかが残らない。
    /// そのためpl_InitiatingUserLookup（→systemuser）へ依頼者を記録する。
    /// 旧pl_TargetGrantLookup（→pl_accessgrant）は、旧アクセス付与の廃止に合わせて2026-09-27に削除した。
    ///
    /// 独立監査2回目P1-3対応: 既存のpl_idempotencykey列（追加のスキーマ変更は不要）を
    /// 使い、GrantAccess/ModifyAccess/RevokeAccessの再送を検出できるようにした。
    /// </summary>
    public static class OperationLogRepository
    {
        public const string EntityName = "pl_operationlog";

        public sealed class ApprovalOperationReplay
        {
            public Guid? TargetRequestId { get; set; }
            public Guid? TargetSubmissionVersionId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string ResultCode { get; set; } = string.Empty;
        }

        public static Guid Record(
            IOrganizationService service,
            string operationCode,
            string resultCode,
            string? errorCode,
            Guid initiatingUserId,
            string name,
            string? idempotencyKey = null)
        {
            var entity = new Entity(EntityName)
            {
                ["pl_operationcode"] = operationCode,
                ["pl_resultcode"] = resultCode,
                ["pl_errorcode"] = errorCode ?? string.Empty,
                ["pl_recordedat"] = DateTime.UtcNow,
                ["pl_name"] = name,
                ["pl_initiatinguserlookup"] = new EntityReference("systemuser", initiatingUserId),
            };
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                entity["pl_idempotencykey"] = idempotencyKey;
            }
            return service.Create(entity);
        }

        /// <summary>
        /// 標準契約Updateの直接処理を、既存OperationLogへ対象契約と変更列だけで記録する。
        /// 契約Lookup列を追加せず、pl_Nameへ固定形式の対象ID／変更列を保存する暫定境界とする。
        /// 監査主体・時刻・結果コードは既存列へ保存し、完全な変更前後値は今回のカードの
        /// スコープ外（標準Dataverse監査を別途採用する場合に再評価）とする。
        /// </summary>
        public static Guid RecordContractUpdate(
            IOrganizationService service,
            string operationCode,
            string resultCode,
            string? errorCode,
            Guid initiatingUserId,
            Guid contractId,
            IEnumerable<string> changedAttributes)
        {
            if (contractId == Guid.Empty) throw new ArgumentException("契約IDが空です。", nameof(contractId));
            var attributes = changedAttributes == null ? string.Empty : string.Join(",", changedAttributes);
            var name = "contract:" + contractId.ToString("D") + ":fields:" + attributes;
            return Record(
                service,
                operationCode,
                resultCode,
                errorCode,
                initiatingUserId,
                name,
                idempotencyKey: null);
        }

        public static Guid RecordPartnerUpdate(
            IOrganizationService service,
            string operationCode,
            string resultCode,
            string? errorCode,
            Guid initiatingUserId,
            Guid partnerId,
            IEnumerable<string> changedAttributes)
        {
            if (partnerId == Guid.Empty) throw new ArgumentException("取引先IDが空です。", nameof(partnerId));
            var attributes = changedAttributes == null ? string.Empty : string.Join(",", changedAttributes);
            var name = "partner:" + partnerId.ToString("D") + ":fields:" + attributes;
            return Record(
                service,
                operationCode,
                resultCode,
                errorCode,
                initiatingUserId,
                name,
                idempotencyKey: null);
        }

        /// <summary>
        /// 承認操作の成功再送を、申請と提出版の両方へ結び付けて取得する。
        /// 対象Lookupが欠損した古い監査行は、呼び出し側で一致しない再送として扱う。
        /// </summary>
        public static ApprovalOperationReplay? FindSuccessApproval(
            IOrganizationService service,
            string operationCode,
            string idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                return null;
            }

            var query = new QueryExpression(EntityName)
            {
                ColumnSet = new ColumnSet(
                    "pl_targetrequestlookup",
                    "pl_targetsubmissionversionlookup",
                    "pl_name"),
                TopCount = 1,
            };
            query.Criteria.AddCondition("pl_operationcode", ConditionOperator.Equal, operationCode);
            query.Criteria.AddCondition("pl_resultcode", ConditionOperator.Equal, "Success");
            query.Criteria.AddCondition("pl_idempotencykey", ConditionOperator.Equal, idempotencyKey);
            query.AddOrder("pl_recordedat", OrderType.Descending);

            var match = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            if (match == null)
            {
                return null;
            }

            return new ApprovalOperationReplay
            {
                TargetRequestId = match.GetAttributeValue<EntityReference>("pl_targetrequestlookup")?.Id,
                TargetSubmissionVersionId = match.GetAttributeValue<EntityReference>("pl_targetsubmissionversionlookup")?.Id,
                Name = match.GetAttributeValue<string>("pl_name") ?? string.Empty,
                ResultCode = match.GetAttributeValue<string>("pl_resultcode") ?? string.Empty,
            };
        }

        /// <summary>
        /// 承認結果取込みの受理済み記録を、成功／結果不明を含めて取得する。
        /// 拒否記録は呼び出し側が再評価できるよう、結果コードも返す。
        /// </summary>
        public static ApprovalOperationReplay? FindApproval(
            IOrganizationService service,
            string operationCode,
            string idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                return null;
            }

            var query = new QueryExpression(EntityName)
            {
                ColumnSet = new ColumnSet(
                    "pl_targetrequestlookup",
                    "pl_targetsubmissionversionlookup",
                    "pl_name",
                    "pl_resultcode"),
                TopCount = 1,
            };
            query.Criteria.AddCondition("pl_operationcode", ConditionOperator.Equal, operationCode);
            query.Criteria.AddCondition("pl_idempotencykey", ConditionOperator.Equal, idempotencyKey);
            query.AddOrder("pl_recordedat", OrderType.Descending);

            var match = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            if (match == null)
            {
                return null;
            }

            return new ApprovalOperationReplay
            {
                TargetRequestId = match.GetAttributeValue<EntityReference>("pl_targetrequestlookup")?.Id,
                TargetSubmissionVersionId = match.GetAttributeValue<EntityReference>("pl_targetsubmissionversionlookup")?.Id,
                Name = match.GetAttributeValue<string>("pl_name") ?? string.Empty,
                ResultCode = match.GetAttributeValue<string>("pl_resultcode") ?? string.Empty,
            };
        }

        /// <summary>
        /// 申請・提出版に対する承認操作を監査する。pl_Nameには既存の
        /// OperationLog設計と同じく操作理由を保存し、申請／提出版Lookupを必ず付ける。
        /// </summary>
        public static Guid RecordApproval(
            IOrganizationService service,
            string operationCode,
            string resultCode,
            string? errorCode,
            Guid initiatingUserId,
            Guid? targetRequestId,
            Guid? targetSubmissionVersionId,
            string reason,
            string? idempotencyKey = null)
        {
            var entity = new Entity(EntityName)
            {
                ["pl_operationcode"] = operationCode,
                ["pl_resultcode"] = resultCode,
                ["pl_errorcode"] = errorCode ?? string.Empty,
                ["pl_recordedat"] = DateTime.UtcNow,
                ["pl_name"] = reason,
                ["pl_initiatinguserlookup"] = new EntityReference("systemuser", initiatingUserId),
            };
            if (targetRequestId.HasValue)
            {
                entity["pl_targetrequestlookup"] = new EntityReference("pl_request", targetRequestId.Value);
            }
            if (targetSubmissionVersionId.HasValue)
            {
                entity["pl_targetsubmissionversionlookup"] = new EntityReference("pl_submissionversion", targetSubmissionVersionId.Value);
            }
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                entity["pl_idempotencykey"] = idempotencyKey;
            }
            return service.Create(entity);
        }
    }
}
