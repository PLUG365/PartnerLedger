using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PartnerLedger.Plugins
{
    /// <summary>
    /// 承認依頼に載せる申請内容の概要。提出時に、検証済みの変更セットと同じトランザクションで読んだ
    /// 対象行から作り、提出版（pl_changesummary）へ保存する。承認者が判断に使う文なので、
    /// 利用者の書いた文は使わない。項目名と値の表記はアプリ（approvalPresentation.ts）に合わせる。
    /// Teamsの承認アプリはMarkdownを解釈しないため、装飾のない1行1項目の文にする（2026-09-26）。
    /// </summary>
    public static class ApprovalChangeSummary
    {
        public const string AttributeName = "pl_changesummary";
        public const int MaxLength = 4000;
        public const string TitleAttributeName = "pl_approvaltitle";
        public const int TitleMaxLength = 400;
        private const string Blank = "（空欄）";

        // 日付はアプリの利用者と同じ日本時間の年月日で表す（日本に夏時間はない）。
        private static readonly TimeSpan JapanOffset = TimeSpan.FromHours(9);

        // 承認依頼の件名に使う申請の種類の見出し（アプリのapprovalRequestTypeLabelsと同じ表記）。
        private static readonly IDictionary<string, string> RequestTypeLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ApprovalPolicyContract.CompanyNameRequestType] = "会社名の変更",
            [ApprovalPolicyContract.TradingStatusRequestType] = "取引状態の変更",
            [ApprovalPolicyContract.AddressRequestType] = "住所の変更",
            [ApprovalPolicyContract.PhoneRequestType] = "代表電話の変更",
            [ApprovalPolicyContract.MainOwnerRequestType] = "主担当の変更",
            [ApprovalPolicyContract.ContractUpdateRequestType] = "契約の更新・終了判断",
        };

        private static readonly IDictionary<string, string> AttributeLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pl_tradingstatuscode"] = "取引状態",
            ["pl_address"] = "住所",
            ["pl_phone"] = "代表電話",
            ["pl_mainownerlookup"] = "主担当",
            ["pl_contractstatuscode"] = "判断",
            ["pl_enddate"] = "契約終了日",
            ["pl_noticedate"] = "解約通知期限",
            ["pl_decisiondate"] = "更新判断期限",
            ["pl_autorenew"] = "自動更新",
            ["pl_link"] = "契約書リンク",
        };

        // 変更セットは項目名順に正規化されるため、表示は業務の順に並べ直す。
        private static readonly IList<string> DisplayOrder = new[]
        {
            "pl_name",
            "pl_tradingstatuscode",
            "pl_address",
            "pl_phone",
            "pl_mainownerlookup",
            "pl_contractstatuscode",
            "pl_enddate",
            "pl_noticedate",
            "pl_decisiondate",
            "pl_autorenew",
            "pl_link",
        };

        private static readonly IDictionary<string, IDictionary<int, string>> ChoiceLabels = new Dictionary<string, IDictionary<int, string>>(StringComparer.Ordinal)
        {
            ["pl_tradingstatuscode"] = new Dictionary<int, string>
            {
                [100000000] = "未承認",
                [100000001] = "取引中",
                [100000002] = "休止中",
                [100000003] = "終了",
            },
            ["pl_contractstatuscode"] = new Dictionary<int, string>
            {
                [100000000] = "更新（締結済み）",
                [100000001] = "終了",
            },
        };

        public static string Build(ApprovalTargetRecord target, ApprovalChangeSet changeSet)
            => Build(target, changeSet, _ => null);

        /// <summary>
        /// userNameは、主担当などユーザーへの参照を名前にするための関数（2026-09-29）。名前が分からなければnullを返す。
        /// </summary>
        public static string Build(ApprovalTargetRecord target, ApprovalChangeSet changeSet, Func<Guid, string?> userName)
        {
            if (userName == null) throw new ArgumentNullException(nameof(userName));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (changeSet == null) throw new ArgumentNullException(nameof(changeSet));
            if (target.CurrentRow == null) throw new ArgumentException("対象行がありません。", nameof(target));

            var isContract = string.Equals(target.EntityName, ApprovalTargetRepository.ContractEntityName, StringComparison.Ordinal);
            var row = target.CurrentRow;
            var builder = new StringBuilder();
            var targetName = Plain(row.GetAttributeValue<string>("pl_name"));
            if (isContract)
            {
                builder.Append("対象：契約「").Append(targetName).Append('」');
                var partnerName = Plain(row.GetAttributeValue<EntityReference>("pl_partnerlookup")?.Name);
                if (partnerName.Length > 0)
                    builder.Append("（取引先「").Append(partnerName).Append("」）");
            }
            else
            {
                builder.Append("対象：取引先「").Append(targetName).Append('」');
            }
            builder.Append("\n変更内容：");

            var operations = (changeSet.Operations ?? Enumerable.Empty<ApprovalChangeOperation>())
                .Where(operation => operation != null)
                .OrderBy(operation => DisplayRank(operation.AttributeName))
                .ThenBy(operation => operation.AttributeName, StringComparer.Ordinal);
            foreach (var operation in operations)
            {
                var attribute = operation.AttributeName;
                builder.Append("\n・")
                    .Append(Label(isContract, attribute))
                    .Append('：')
                    .Append(FormatCurrent(attribute, row.Attributes.TryGetValue(attribute, out var current) ? current : null, userName))
                    .Append(" → ")
                    .Append(FormatChange(attribute, operation.Value, userName));
            }

            var text = builder.ToString();
            return text.Length <= MaxLength ? text : text.Substring(0, MaxLength - 1) + "…";
        }

        /// <summary>
        /// 承認依頼の件名。申請の種類と対象の名前から作る（例：会社名の変更：青空商事）。
        /// 種類と変更できる項目の対応は提出時に検証済みのため、件名と中身が食い違わない。
        /// </summary>
        public static string BuildTitle(ApprovalTargetRecord target, string? requestTypeCode)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.CurrentRow == null) throw new ArgumentException("対象行がありません。", nameof(target));

            var label = requestTypeCode != null && RequestTypeLabels.TryGetValue(requestTypeCode, out var known) ? known : "承認申請";
            var row = target.CurrentRow;
            var name = Plain(row.GetAttributeValue<string>("pl_name"));
            if (string.Equals(target.EntityName, ApprovalTargetRepository.ContractEntityName, StringComparison.Ordinal))
            {
                var partnerName = Plain(row.GetAttributeValue<EntityReference>("pl_partnerlookup")?.Name);
                if (partnerName.Length > 0)
                    name += "（" + partnerName + "）";
            }

            var title = label + "：" + name;
            return title.Length <= TitleMaxLength ? title : title.Substring(0, TitleMaxLength - 1) + "…";
        }

        private static int DisplayRank(string attribute)
        {
            var index = DisplayOrder.IndexOf(attribute);
            return index < 0 ? int.MaxValue : index;
        }

        private static string Label(bool isContract, string attribute)
        {
            if (attribute == "pl_name") return isContract ? "契約名" : "会社名";
            return AttributeLabels.TryGetValue(attribute, out var label) ? label : attribute;
        }

        /// <summary>ユーザーの名前を読む（SYSTEMのサービスで）。無ければnull。存在しないIDでも例外にしないよう検索で読む（監査L-3）。</summary>
        public static string? RetrieveUserName(IOrganizationService service, Guid userId)
        {
            if (service == null || userId == Guid.Empty) return null;
            return PartnerMainOwnerChange.RetrieveUser(service, userId, "fullname")?.GetAttributeValue<string>("fullname");
        }

        private static string FormatCurrent(string attribute, object? value, Func<Guid, string?> userName)
        {
            switch (value)
            {
                case null:
                    return Blank;
                case EntityReference reference:
                    return OrBlank(Plain(!string.IsNullOrWhiteSpace(reference.Name) ? reference.Name : userName(reference.Id)));
                case string text:
                    return OrBlank(Plain(text));
                case OptionSetValue choice:
                    return ChoiceLabel(attribute, choice.Value);
                case bool flag:
                    return flag ? "あり" : "なし";
                case DateTime time:
                    return FormatDate(time);
                default:
                    return OrBlank(Plain(Convert.ToString(value, CultureInfo.InvariantCulture)));
            }
        }

        private static string FormatChange(string attribute, ApprovalChangeValue? value, Func<Guid, string?> userName)
        {
            if (value == null || value.IsNull) return Blank;
            switch (value.Kind)
            {
                case ApprovalChangeValueKind.Text:
                    return OrBlank(Plain(value.TextValue));
                case ApprovalChangeValueKind.Choice:
                    return value.ChoiceValue.HasValue ? ChoiceLabel(attribute, value.ChoiceValue.Value) : Blank;
                case ApprovalChangeValueKind.Boolean:
                    return value.BooleanValue.HasValue ? (value.BooleanValue.Value ? "あり" : "なし") : Blank;
                case ApprovalChangeValueKind.DateTime:
                    return value.DateTimeValue.HasValue ? FormatDate(value.DateTimeValue.Value) : Blank;
                case ApprovalChangeValueKind.SystemUser:
                    return value.UserIdValue.HasValue ? OrBlank(Plain(userName(value.UserIdValue.Value))) : Blank;
                default:
                    return Blank;
            }
        }

        private static string ChoiceLabel(string attribute, int value)
            => ChoiceLabels.TryGetValue(attribute, out var labels) && labels.TryGetValue(value, out var label)
                ? label
                : value.ToString(CultureInfo.InvariantCulture);

        private static string FormatDate(DateTime time)
        {
            var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc);
            return (utc + JapanOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string OrBlank(string text) => text.Length == 0 ? Blank : text;

        /// <summary>
        /// 1行1項目の形を崩さないよう値の改行を空白にし、Markdownを解釈する承認クライアントで
        /// リンクとして扱われないよう「](」を「] (」にする。前後の空白は除く。
        /// </summary>
        private static string Plain(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var flattened = value!.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            return flattened.Replace("](", "] (").Trim();
        }
    }
}
