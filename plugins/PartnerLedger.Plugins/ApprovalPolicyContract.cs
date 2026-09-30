using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PartnerLedger.Plugins
{
    public enum ApprovalPolicyValidationError
    {
        None,
        PayloadRequired,
        PayloadTooLarge,
        JsonMalformed,
        UnexpectedStructure,
        UnknownProperty,
        DuplicateProperty,
        RequiredPropertyMissing,
        SchemaVersionUnsupported,
        ValueTypeMismatch,
    }

    public enum ApprovalPolicyResolutionError
    {
        None,
        PolicyRequired,
        RequestTypeRequired,
        TargetEntityRequired,
        TargetEntityUnsupported,
        TargetEntityMismatch,
        RequestTypeUnsupported,
        TargetDisabled,
        TargetSchemaUnavailable,
    }

    public sealed class ApprovalPolicy
    {
        public int SchemaVersion { get; internal set; }
        public bool CompanyName { get; internal set; }
        public bool TradingStatus { get; internal set; }
        public bool Address { get; internal set; }
        public bool Phone { get; internal set; }
        public bool ContractUpdate { get; internal set; }
        /// <summary>主担当の変更に承認が必要か。版1の設定には項目が無く、承認が必要として扱う（2026-09-29）。</summary>
        public bool MainOwner { get; internal set; } = true;
    }

    public sealed class ApprovalPolicyResult
    {
        private ApprovalPolicyResult(
            bool isValid,
            ApprovalPolicyValidationError error,
            ApprovalPolicy? policy)
        {
            IsValid = isValid;
            Error = error;
            Policy = policy;
        }

        public bool IsValid { get; }
        public ApprovalPolicyValidationError Error { get; }
        public ApprovalPolicy? Policy { get; }

        public static ApprovalPolicyResult Valid(ApprovalPolicy policy)
            => new ApprovalPolicyResult(true, ApprovalPolicyValidationError.None, policy);

        public static ApprovalPolicyResult Invalid(ApprovalPolicyValidationError error)
            => new ApprovalPolicyResult(false, error, null);
    }

    public sealed class ApprovalPolicyResolution
    {
        public string RequestTypeCode { get; internal set; } = string.Empty;
        public string EntityName { get; internal set; } = string.Empty;
        public ISet<string> AllowedAttributes { get; internal set; }
            = new HashSet<string>(StringComparer.Ordinal);
    }

    public sealed class ApprovalPolicyResolutionResult
    {
        private ApprovalPolicyResolutionResult(
            bool isValid,
            ApprovalPolicyResolutionError error,
            ApprovalPolicyResolution? resolution)
        {
            IsValid = isValid;
            Error = error;
            Resolution = resolution;
        }

        public bool IsValid { get; }
        public ApprovalPolicyResolutionError Error { get; }
        public ApprovalPolicyResolution? Resolution { get; }

        public static ApprovalPolicyResolutionResult Valid(ApprovalPolicyResolution resolution)
            => new ApprovalPolicyResolutionResult(true, ApprovalPolicyResolutionError.None, resolution);

        public static ApprovalPolicyResolutionResult Invalid(ApprovalPolicyResolutionError error)
            => new ApprovalPolicyResolutionResult(false, error, null);
    }

    /// <summary>
    /// pl_Settings.pl_policyjson の厳格な契約（版2。版1も読む）と、申請種別から対象・許可項目を解決する境界。
    /// JSONは既存列の最大長に合わせたフラットな固定スキーマとし、クライアント入力を許可項目へ
    /// 直接変換しない。住所・電話も、設定ONなら承認反映、OFFなら標準Updateの
    /// サーバー境界へ渡す。クライアントは許可属性集合を指定できない。
    /// </summary>
    public static class ApprovalPolicyContract
    {
        // 版2で主担当の変更（mainOwner）を足した（2026-09-29）。版1の保存済み設定も読む。
        public const int SupportedSchemaVersion = 2;
        public const int LegacySchemaVersion = 1;
        public const int MaxPayloadLength = 200;

        /// <summary>
        /// 有効な承認設定の行が無いときに使う既定値（要件PL-001：会社名・取引状態は承認あり、
        /// 住所・代表電話は承認なし。契約の更新・終了判断は承認あり、2026-09-27ユーザー確認）。
        /// アプリの既定値（contractApprovalPolicy.ts）と同じ内容にする。
        /// </summary>
        public const string DefaultPolicyJson =
            "{\"schemaVersion\":2,\"companyName\":true,\"tradingStatus\":true,\"address\":false,\"phone\":false,\"contractUpdate\":true,\"mainOwner\":true}";

        public const string CompanyNameRequestType = "partner.company-name";
        public const string TradingStatusRequestType = "partner.trading-status";
        public const string AddressRequestType = "partner.address";
        public const string PhoneRequestType = "partner.phone";
        public const string ContractUpdateRequestType = "contract.update";
        public const string MainOwnerRequestType = "partner.main-owner";

        private const int MaxJsonDepth = 8;
        private const string PartnerEntityName = ApprovalTargetRepository.PartnerEntityName;
        private const string ContractEntityName = ApprovalTargetRepository.ContractEntityName;

        private static readonly ISet<string> PropertyNames
            = new HashSet<string>(StringComparer.Ordinal)
            {
                "schemaVersion",
                "companyName",
                "tradingStatus",
                "address",
                "phone",
                "contractUpdate",
                "mainOwner",
            };

        private static readonly string[] ContractAttributes
            =
            {
                "pl_name",
                "pl_contractstatuscode",
                "pl_autorenew",
                "pl_decisiondate",
                "pl_enddate",
                "pl_noticedate",
                "pl_link",
            };

        public static ApprovalPolicyResult Validate(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return ApprovalPolicyResult.Invalid(ApprovalPolicyValidationError.PayloadRequired);
            var policyJson = json!;
            if (policyJson.Length > MaxPayloadLength)
                return ApprovalPolicyResult.Invalid(ApprovalPolicyValidationError.PayloadTooLarge);

            try
            {
                var document = new StrictJsonParser(policyJson).Parse();
                var objectResult = ReadExactObject(
                    document,
                    new[]
                    {
                        "schemaVersion",
                        "companyName",
                        "tradingStatus",
                        "address",
                        "phone",
                        "contractUpdate",
                    },
                    out var root);
                if (objectResult != ApprovalPolicyValidationError.None)
                    return ApprovalPolicyResult.Invalid(objectResult);

                if (!TryGetInt32(root["schemaVersion"], out var schemaVersion)
                    || (schemaVersion != SupportedSchemaVersion && schemaVersion != LegacySchemaVersion))
                {
                    return ApprovalPolicyResult.Invalid(ApprovalPolicyValidationError.SchemaVersionUnsupported);
                }

                // 版1には主担当の項目が無い（あれば未知の項目）。版2では必須。
                var mainOwner = true;
                if (schemaVersion == LegacySchemaVersion)
                {
                    if (root.ContainsKey("mainOwner"))
                        return ApprovalPolicyResult.Invalid(ApprovalPolicyValidationError.UnknownProperty);
                }
                else
                {
                    if (!root.ContainsKey("mainOwner"))
                        return ApprovalPolicyResult.Invalid(ApprovalPolicyValidationError.RequiredPropertyMissing);
                    if (!TryGetBoolean(root["mainOwner"], out mainOwner))
                        return ApprovalPolicyResult.Invalid(ApprovalPolicyValidationError.ValueTypeMismatch);
                }

                if (!TryGetBoolean(root["companyName"], out var companyName)
                    || !TryGetBoolean(root["tradingStatus"], out var tradingStatus)
                    || !TryGetBoolean(root["address"], out var address)
                    || !TryGetBoolean(root["phone"], out var phone)
                    || !TryGetBoolean(root["contractUpdate"], out var contractUpdate))
                {
                    return ApprovalPolicyResult.Invalid(ApprovalPolicyValidationError.ValueTypeMismatch);
                }

                return ApprovalPolicyResult.Valid(new ApprovalPolicy
                {
                    SchemaVersion = schemaVersion,
                    CompanyName = companyName,
                    TradingStatus = tradingStatus,
                    Address = address,
                    Phone = phone,
                    ContractUpdate = contractUpdate,
                    MainOwner = mainOwner,
                });
            }
            catch (PolicyJsonParseException exception)
            {
                return ApprovalPolicyResult.Invalid(exception.Error);
            }
        }

        /// <summary>
        /// リクエスト種別、現在の対象Entity、サーバー側ポリシーを組み合わせて許可項目を決める。
        /// targetEntityNameを必須にすることで、申請種別だけで別テーブルを更新する経路を残さない。
        /// </summary>
        public static ApprovalPolicyResolutionResult Resolve(
            ApprovalPolicy? policy,
            string? requestTypeCode,
            string? targetEntityName)
        {
            if (policy == null)
                return ApprovalPolicyResolutionResult.Invalid(ApprovalPolicyResolutionError.PolicyRequired);
            if (string.IsNullOrWhiteSpace(requestTypeCode))
                return ApprovalPolicyResolutionResult.Invalid(ApprovalPolicyResolutionError.RequestTypeRequired);
            if (string.IsNullOrWhiteSpace(targetEntityName))
                return ApprovalPolicyResolutionResult.Invalid(ApprovalPolicyResolutionError.TargetEntityRequired);
            if (targetEntityName != PartnerEntityName && targetEntityName != ContractEntityName)
                return ApprovalPolicyResolutionResult.Invalid(ApprovalPolicyResolutionError.TargetEntityUnsupported);

            switch (requestTypeCode)
            {
                case CompanyNameRequestType:
                    return ResolvePartnerPolicy(
                        policy.CompanyName,
                        requestTypeCode,
                        targetEntityName,
                        new[] { "pl_name" });
                case TradingStatusRequestType:
                    return ResolvePartnerPolicy(
                        policy.TradingStatus,
                        requestTypeCode,
                        targetEntityName,
                        new[] { "pl_tradingstatuscode" });
                case AddressRequestType:
                    return ResolvePartnerPolicy(
                        policy.Address,
                        requestTypeCode,
                        targetEntityName,
                        new[] { "pl_address" });
                case PhoneRequestType:
                    return ResolvePartnerPolicy(
                        policy.Phone,
                        requestTypeCode,
                        targetEntityName,
                        new[] { "pl_phone" });
                case MainOwnerRequestType:
                    return ResolvePartnerPolicy(
                        policy.MainOwner,
                        requestTypeCode,
                        targetEntityName,
                        new[] { "pl_mainownerlookup" });
                case ContractUpdateRequestType:
                    if (targetEntityName != ContractEntityName)
                    {
                        return ApprovalPolicyResolutionResult.Invalid(ApprovalPolicyResolutionError.TargetEntityMismatch);
                    }
                    if (!policy.ContractUpdate)
                        return ApprovalPolicyResolutionResult.Invalid(ApprovalPolicyResolutionError.TargetDisabled);
                    return ApprovalPolicyResolutionResult.Valid(CreateResolution(
                        requestTypeCode,
                        ContractEntityName,
                        ContractAttributes));
                default:
                    return ApprovalPolicyResolutionResult.Invalid(ApprovalPolicyResolutionError.RequestTypeUnsupported);
            }
        }

        private static ApprovalPolicyResolutionResult ResolvePartnerPolicy(
            bool enabled,
            string requestTypeCode,
            string targetEntityName,
            IEnumerable<string> allowedAttributes)
        {
            if (targetEntityName != PartnerEntityName)
                return ApprovalPolicyResolutionResult.Invalid(ApprovalPolicyResolutionError.TargetEntityMismatch);
            if (!enabled)
                return ApprovalPolicyResolutionResult.Invalid(ApprovalPolicyResolutionError.TargetDisabled);
            return ApprovalPolicyResolutionResult.Valid(CreateResolution(
                requestTypeCode,
                PartnerEntityName,
                allowedAttributes));
        }

        private static ApprovalPolicyResolution CreateResolution(
            string requestTypeCode,
            string entityName,
            IEnumerable<string> allowedAttributes)
            => new ApprovalPolicyResolution
            {
                RequestTypeCode = requestTypeCode,
                EntityName = entityName,
                AllowedAttributes = new HashSet<string>(allowedAttributes, StringComparer.Ordinal),
            };

        private static ApprovalPolicyValidationError ReadExactObject(
            JsonValue document,
            IEnumerable<string> requiredProperties,
            out IDictionary<string, JsonValue> properties)
        {
            properties = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            if (document.Kind != JsonNodeKind.Object || document.ObjectValue == null)
                return ApprovalPolicyValidationError.UnexpectedStructure;

            foreach (var property in document.ObjectValue)
            {
                if (!PropertyNames.Contains(property.Key))
                    return ApprovalPolicyValidationError.UnknownProperty;
                properties.Add(property.Key, property.Value);
            }
            foreach (var requiredProperty in requiredProperties)
            {
                if (!properties.ContainsKey(requiredProperty))
                    return ApprovalPolicyValidationError.RequiredPropertyMissing;
            }
            return ApprovalPolicyValidationError.None;
        }

        private static bool TryGetBoolean(JsonValue value, out bool result)
        {
            result = false;
            if (value.Kind != JsonNodeKind.True && value.Kind != JsonNodeKind.False)
                return false;
            result = value.BooleanValue!.Value;
            return true;
        }

        private static bool TryGetInt32(JsonValue value, out int result)
        {
            result = 0;
            return value.Kind == JsonNodeKind.Number
                && int.TryParse(
                    value.NumberText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out result);
        }

        private enum JsonNodeKind
        {
            Object,
            Array,
            String,
            Number,
            True,
            False,
            Null,
        }

        private sealed class JsonValue
        {
            private JsonValue(JsonNodeKind kind)
            {
                Kind = kind;
            }

            public JsonNodeKind Kind { get; }
            public IDictionary<string, JsonValue>? ObjectValue { get; private set; }
            public IList<JsonValue>? ArrayValue { get; private set; }
            public string? StringValue { get; private set; }
            public string? NumberText { get; private set; }
            public bool? BooleanValue { get; private set; }

            public static JsonValue Object(IDictionary<string, JsonValue> value)
                => new JsonValue(JsonNodeKind.Object) { ObjectValue = value };

            public static JsonValue Array(IList<JsonValue> value)
                => new JsonValue(JsonNodeKind.Array) { ArrayValue = value };

            public static JsonValue String(string value)
                => new JsonValue(JsonNodeKind.String) { StringValue = value };

            public static JsonValue Number(string value)
                => new JsonValue(JsonNodeKind.Number) { NumberText = value };

            public static JsonValue Boolean(bool value)
                => new JsonValue(value ? JsonNodeKind.True : JsonNodeKind.False) { BooleanValue = value };

            public static JsonValue Null()
                => new JsonValue(JsonNodeKind.Null);
        }

        private sealed class PolicyJsonParseException : Exception
        {
            public PolicyJsonParseException(ApprovalPolicyValidationError error)
            {
                Error = error;
            }

            public ApprovalPolicyValidationError Error { get; }
        }

        private sealed class StrictJsonParser
        {
            private readonly string json;
            private int position;

            public StrictJsonParser(string json)
            {
                this.json = json;
            }

            public JsonValue Parse()
            {
                SkipWhitespace();
                var value = ParseValue(0);
                SkipWhitespace();
                if (position != json.Length)
                    ThrowMalformed();
                return value;
            }

            private JsonValue ParseValue(int depth)
            {
                if (depth > MaxJsonDepth || position >= json.Length)
                    ThrowMalformed();

                switch (json[position])
                {
                    case '{':
                        return ParseObject(depth);
                    case '[':
                        return ParseArray(depth);
                    case '"':
                        return JsonValue.String(ParseString());
                    case 't':
                        ParseLiteral("true");
                        return JsonValue.Boolean(true);
                    case 'f':
                        ParseLiteral("false");
                        return JsonValue.Boolean(false);
                    case 'n':
                        ParseLiteral("null");
                        return JsonValue.Null();
                    default:
                        if (json[position] == '-' || IsAsciiDigit(json[position]))
                            return JsonValue.Number(ParseNumber());
                        ThrowMalformed();
                        return JsonValue.Null();
                }
            }

            private JsonValue ParseObject(int depth)
            {
                position++;
                SkipWhitespace();
                var properties = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                if (TryConsume('}'))
                    return JsonValue.Object(properties);

                while (true)
                {
                    if (position >= json.Length || json[position] != '"')
                        ThrowMalformed();
                    var name = ParseString();
                    SkipWhitespace();
                    Require(':');
                    SkipWhitespace();
                    var value = ParseValue(depth + 1);
                    if (properties.ContainsKey(name))
                        throw new PolicyJsonParseException(ApprovalPolicyValidationError.DuplicateProperty);
                    properties.Add(name, value);
                    SkipWhitespace();
                    if (TryConsume('}'))
                        return JsonValue.Object(properties);
                    Require(',');
                    SkipWhitespace();
                    if (position >= json.Length || json[position] != '"')
                        ThrowMalformed();
                }
            }

            private JsonValue ParseArray(int depth)
            {
                position++;
                SkipWhitespace();
                var values = new List<JsonValue>();
                if (TryConsume(']'))
                    return JsonValue.Array(values);

                while (true)
                {
                    values.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (TryConsume(']'))
                        return JsonValue.Array(values);
                    Require(',');
                    SkipWhitespace();
                    if (position >= json.Length || json[position] == ']')
                        ThrowMalformed();
                }
            }

            private string ParseString()
            {
                Require('"');
                var builder = new StringBuilder();
                while (position < json.Length)
                {
                    var character = json[position++];
                    if (character == '"')
                        return builder.ToString();
                    if (character < 0x20)
                        ThrowMalformed();
                    if (character != '\\')
                    {
                        builder.Append(character);
                        continue;
                    }

                    if (position >= json.Length)
                        ThrowMalformed();
                    var escape = json[position++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u': AppendUnicodeEscape(builder); break;
                        default: ThrowMalformed(); break;
                    }
                }

                ThrowMalformed();
                return string.Empty;
            }

            private void AppendUnicodeEscape(StringBuilder builder)
            {
                var codeUnit = ParseHexCodeUnit();
                if (char.IsHighSurrogate((char)codeUnit))
                {
                    if (position + 6 > json.Length
                        || json[position] != '\\'
                        || json[position + 1] != 'u')
                    {
                        ThrowMalformed();
                    }
                    position += 2;
                    var lowCodeUnit = ParseHexCodeUnit();
                    if (!char.IsLowSurrogate((char)lowCodeUnit))
                        ThrowMalformed();
                    builder.Append(char.ConvertFromUtf32(char.ConvertToUtf32((char)codeUnit, (char)lowCodeUnit)));
                    return;
                }
                if (char.IsLowSurrogate((char)codeUnit))
                    ThrowMalformed();
                builder.Append((char)codeUnit);
            }

            private int ParseHexCodeUnit()
            {
                if (position + 4 > json.Length)
                    ThrowMalformed();
                var value = 0;
                for (var index = 0; index < 4; index++)
                {
                    var digit = HexValue(json[position++]);
                    if (digit < 0)
                        ThrowMalformed();
                    value = (value * 16) + digit;
                }
                return value;
            }

            private string ParseNumber()
            {
                var start = position;
                if (TryConsume('-') && position >= json.Length)
                    ThrowMalformed();

                if (TryConsume('0'))
                {
                    if (position < json.Length && IsAsciiDigit(json[position]))
                        ThrowMalformed();
                }
                else
                {
                    if (position >= json.Length || json[position] < '1' || json[position] > '9')
                        ThrowMalformed();
                    while (position < json.Length && IsAsciiDigit(json[position]))
                        position++;
                }

                if (TryConsume('.'))
                {
                    if (position >= json.Length || !IsAsciiDigit(json[position]))
                        ThrowMalformed();
                    while (position < json.Length && IsAsciiDigit(json[position]))
                        position++;
                }

                if (position < json.Length && (json[position] == 'e' || json[position] == 'E'))
                {
                    position++;
                    if (position < json.Length && (json[position] == '+' || json[position] == '-'))
                        position++;
                    if (position >= json.Length || !IsAsciiDigit(json[position]))
                        ThrowMalformed();
                    while (position < json.Length && IsAsciiDigit(json[position]))
                        position++;
                }

                return json.Substring(start, position - start);
            }

            private void ParseLiteral(string literal)
            {
                if (position + literal.Length > json.Length
                    || !string.Equals(json.Substring(position, literal.Length), literal, StringComparison.Ordinal))
                {
                    ThrowMalformed();
                }
                position += literal.Length;
            }

            private void SkipWhitespace()
            {
                while (position < json.Length)
                {
                    var character = json[position];
                    if (character != ' ' && character != '\t' && character != '\r' && character != '\n')
                        return;
                    position++;
                }
            }

            private void Require(char expected)
            {
                if (!TryConsume(expected))
                    ThrowMalformed();
            }

            private bool TryConsume(char expected)
            {
                if (position < json.Length && json[position] == expected)
                {
                    position++;
                    return true;
                }
                return false;
            }

            private static bool IsAsciiDigit(char character)
                => character >= '0' && character <= '9';

            private static int HexValue(char character)
            {
                if (character >= '0' && character <= '9') return character - '0';
                if (character >= 'a' && character <= 'f') return character - 'a' + 10;
                if (character >= 'A' && character <= 'F') return character - 'A' + 10;
                return -1;
            }

            private static void ThrowMalformed()
                => throw new PolicyJsonParseException(ApprovalPolicyValidationError.JsonMalformed);
        }
    }
}
