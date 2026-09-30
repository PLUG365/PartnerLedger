using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PartnerLedger.Plugins
{
    public enum ApprovalChangeSetValidationError
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
        TargetEntityUnsupported,
        TargetEntityMismatch,
        TargetIdInvalid,
        TargetIdMismatch,
        ChangesRequired,
        TooManyChanges,
        AttributeUnsupported,
        AttributeNotPermitted,
        DuplicateAttribute,
        ValueRequired,
        ValueTypeMismatch,
        ValueOutOfRange,
        ValueTooLong,
        DateTimeOffsetRequired,
    }

    public enum ApprovalChangeValueKind
    {
        Text,
        Choice,
        Boolean,
        DateTime,
        /// <summary>ユーザーへの参照（主担当、2026-09-29）。値はsystemuseridのGUID文字列。</summary>
        SystemUser,
    }

    public sealed class ApprovalChangeSetInput
    {
        public string Json { get; set; } = string.Empty;
        public string ExpectedEntityName { get; set; } = string.Empty;
        public Guid ExpectedTargetId { get; set; }

        // This set must be resolved from the server-side request type and policy.
        // It is deliberately not a Custom API input.
        public ISet<string> AllowedAttributes { get; set; }
            = new HashSet<string>(StringComparer.Ordinal);
    }

    public sealed class ApprovalChangeSetResult
    {
        private ApprovalChangeSetResult(
            bool isValid,
            ApprovalChangeSetValidationError error,
            ApprovalChangeSet? changeSet)
        {
            IsValid = isValid;
            Error = error;
            ChangeSet = changeSet;
        }

        public bool IsValid { get; }
        public ApprovalChangeSetValidationError Error { get; }
        public ApprovalChangeSet? ChangeSet { get; }

        public static ApprovalChangeSetResult Valid(ApprovalChangeSet changeSet)
            => new ApprovalChangeSetResult(true, ApprovalChangeSetValidationError.None, changeSet);

        public static ApprovalChangeSetResult Invalid(ApprovalChangeSetValidationError error)
            => new ApprovalChangeSetResult(false, error, null);
    }

    public sealed class ApprovalChangeSet
    {
        public int SchemaVersion { get; internal set; }
        public string EntityName { get; internal set; } = string.Empty;
        public Guid TargetId { get; internal set; }
        public IReadOnlyList<ApprovalChangeOperation> Operations { get; internal set; }
            = Array.Empty<ApprovalChangeOperation>();
        public string Fingerprint { get; internal set; } = string.Empty;
    }

    public sealed class ApprovalChangeOperation
    {
        public string AttributeName { get; internal set; } = string.Empty;
        public ApprovalChangeValue Value { get; internal set; } = new ApprovalChangeValue();
    }

    public sealed class ApprovalChangeValue
    {
        public ApprovalChangeValueKind Kind { get; internal set; }
        public bool IsNull { get; internal set; }
        public string? TextValue { get; internal set; }
        public int? ChoiceValue { get; internal set; }
        public bool? BooleanValue { get; internal set; }
        public DateTime? DateTimeValue { get; internal set; }
        public Guid? UserIdValue { get; internal set; }
    }

    /// <summary>
    /// 提出版の変更スナップショットを、反映処理へ渡す前に厳格に検証する。
    /// JSONはDataverseへ保存されるため、保存済みであることだけを信頼せず、対象行・項目・型を
    /// サーバー側で再検証する。ここではEntityを更新せず、正規化した値と指紋だけを返す。
    ///
    /// このクラスはDataverseの署名済み個別プラグイン登録から外部依存DLLを増やさないため、
    /// JSONの最小パーサーをBCLだけで実装している。System.Text.Jsonは設計時には参照できても、
    /// サンドボックスの版と一致する保証がなく、依存アセンブリをプラグインパッケージへ含める
    /// 追加運用が必要になるため、ここでは使用しない。
    /// </summary>
    public static class ApprovalChangeSetContract
    {
        public const int SupportedSchemaVersion = 1;
        public const int MaxPayloadLength = 64 * 1024;
        public const int MaxOperationCount = 8;

        private const int MaxJsonDepth = 16;
        private const string PartnerEntityName = "pl_partner";
        private const string ContractEntityName = "pl_contract";

        private static readonly ISet<string> SupportedEntities
            = new HashSet<string>(StringComparer.Ordinal)
            {
                PartnerEntityName,
                ContractEntityName,
            };

        private static readonly IDictionary<string, FieldDefinition> Fields
            = new Dictionary<string, FieldDefinition>(StringComparer.Ordinal)
            {
                [FieldKey(PartnerEntityName, "pl_name")] = FieldDefinition.Text(PartnerEntityName, "pl_name", 200, allowNull: false, allowEmpty: false),
                [FieldKey(PartnerEntityName, "pl_tradingstatuscode")] = FieldDefinition.Choice(
                    PartnerEntityName,
                    "pl_tradingstatuscode",
                    new[] { 100000000, 100000001, 100000002, 100000003 }),
                [FieldKey(PartnerEntityName, "pl_address")] = FieldDefinition.Text(
                    PartnerEntityName,
                    "pl_address",
                    200,
                    allowNull: true,
                    allowEmpty: true),
                [FieldKey(PartnerEntityName, "pl_mainownerlookup")] = FieldDefinition.SystemUser(PartnerEntityName, "pl_mainownerlookup"),
                [FieldKey(PartnerEntityName, "pl_phone")] = FieldDefinition.Text(
                    PartnerEntityName,
                    "pl_phone",
                    200,
                    allowNull: true,
                    allowEmpty: true),
                [FieldKey(ContractEntityName, "pl_name")] = FieldDefinition.Text(ContractEntityName, "pl_name", 200, allowNull: false, allowEmpty: false),
                [FieldKey(ContractEntityName, "pl_contractstatuscode")] = FieldDefinition.Choice(
                    ContractEntityName,
                    "pl_contractstatuscode",
                    new[] { 100000000, 100000001 }),
                [FieldKey(ContractEntityName, "pl_autorenew")] = FieldDefinition.Boolean(ContractEntityName, "pl_autorenew"),
                [FieldKey(ContractEntityName, "pl_decisiondate")] = FieldDefinition.DateTime(ContractEntityName, "pl_decisiondate"),
                [FieldKey(ContractEntityName, "pl_enddate")] = FieldDefinition.DateTime(ContractEntityName, "pl_enddate"),
                [FieldKey(ContractEntityName, "pl_noticedate")] = FieldDefinition.DateTime(ContractEntityName, "pl_noticedate"),
                [FieldKey(ContractEntityName, "pl_link")] = FieldDefinition.Text(ContractEntityName, "pl_link", 200, allowNull: true, allowEmpty: true),
            };

        public static ApprovalChangeSetResult Validate(ApprovalChangeSetInput input)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.Json))
                return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.PayloadRequired);
            if (input.Json.Length > MaxPayloadLength)
                return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.PayloadTooLarge);
            if (input.ExpectedTargetId == Guid.Empty)
                return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.TargetIdInvalid);
            if (!SupportedEntities.Contains(input.ExpectedEntityName))
                return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.TargetEntityUnsupported);
            if (input.AllowedAttributes == null)
                return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.AttributeNotPermitted);

            try
            {
                var document = new StrictJsonParser(input.Json).Parse();
                var rootResult = ReadExactObject(
                    document,
                    new[] { "schemaVersion", "target", "changes" },
                    new[] { "schemaVersion", "target", "changes" },
                    out var root);
                if (rootResult != ApprovalChangeSetValidationError.None)
                    return ApprovalChangeSetResult.Invalid(rootResult);

                if (!TryGetInt32(root["schemaVersion"], out var schemaVersion)
                    || schemaVersion != SupportedSchemaVersion)
                {
                    return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.SchemaVersionUnsupported);
                }

                var targetResult = ReadExactObject(
                    root["target"],
                    new[] { "entity", "id" },
                    new[] { "entity", "id" },
                    out var target);
                if (targetResult != ApprovalChangeSetValidationError.None)
                    return ApprovalChangeSetResult.Invalid(targetResult);

                if (!TryGetString(target["entity"], out var entityName)
                    || !SupportedEntities.Contains(entityName))
                {
                    return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.TargetEntityUnsupported);
                }
                if (!string.Equals(entityName, input.ExpectedEntityName, StringComparison.Ordinal))
                    return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.TargetEntityMismatch);

                if (!TryGetString(target["id"], out var targetIdText)
                    || !Guid.TryParse(targetIdText, out var targetId)
                    || targetId == Guid.Empty)
                {
                    return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.TargetIdInvalid);
                }
                if (targetId != input.ExpectedTargetId)
                    return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.TargetIdMismatch);

                if (root["changes"].Kind != JsonNodeKind.Array)
                    return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.UnexpectedStructure);

                var operations = new List<ApprovalChangeOperation>();
                var seenAttributes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var change in root["changes"].ArrayValue!)
                {
                    if (operations.Count >= MaxOperationCount)
                        return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.TooManyChanges);

                    var changeResult = ReadExactObject(
                        change,
                        new[] { "attribute", "value" },
                        new[] { "attribute", "value" },
                        out var operationJson);
                    if (changeResult != ApprovalChangeSetValidationError.None)
                        return ApprovalChangeSetResult.Invalid(changeResult);
                    if (!TryGetString(operationJson["attribute"], out var attributeName)
                        || string.IsNullOrWhiteSpace(attributeName))
                    {
                        return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.AttributeUnsupported);
                    }
                    if (!seenAttributes.Add(attributeName))
                        return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.DuplicateAttribute);
                    if (!Fields.TryGetValue(FieldKey(entityName, attributeName), out var definition))
                        return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.AttributeUnsupported);
                    if (!input.AllowedAttributes.Contains(attributeName))
                        return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.AttributeNotPermitted);

                    var valueResult = ParseValue(definition, operationJson["value"], out var parsedValue);
                    if (valueResult != ApprovalChangeSetValidationError.None)
                        return ApprovalChangeSetResult.Invalid(valueResult);

                    operations.Add(new ApprovalChangeOperation
                    {
                        AttributeName = attributeName,
                        Value = parsedValue!,
                    });
                }

                if (operations.Count == 0)
                    return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.ChangesRequired);

                operations = operations
                    .OrderBy(operation => operation.AttributeName, StringComparer.Ordinal)
                    .ToList();
                var changeSet = new ApprovalChangeSet
                {
                    SchemaVersion = schemaVersion,
                    EntityName = entityName,
                    TargetId = targetId,
                    Operations = operations,
                };
                changeSet.Fingerprint = CreateFingerprint(changeSet);
                return ApprovalChangeSetResult.Valid(changeSet);
            }
            catch (JsonParseException ex)
            {
                return ApprovalChangeSetResult.Invalid(ex.Error);
            }
            catch (InvalidOperationException)
            {
                return ApprovalChangeSetResult.Invalid(ApprovalChangeSetValidationError.UnexpectedStructure);
            }
        }

        private static ApprovalChangeSetValidationError ReadExactObject(
            JsonValue element,
            IEnumerable<string> allowedNames,
            IEnumerable<string> requiredNames,
            out Dictionary<string, JsonValue> properties)
        {
            properties = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            if (element.Kind != JsonNodeKind.Object)
                return ApprovalChangeSetValidationError.UnexpectedStructure;

            var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
            foreach (var property in element.ObjectValue!)
            {
                if (!allowed.Contains(property.Key))
                    return ApprovalChangeSetValidationError.UnknownProperty;
                if (properties.ContainsKey(property.Key))
                    return ApprovalChangeSetValidationError.DuplicateProperty;
                properties.Add(property.Key, property.Value);
            }

            foreach (var requiredName in requiredNames)
            {
                if (!properties.ContainsKey(requiredName))
                    return ApprovalChangeSetValidationError.RequiredPropertyMissing;
            }
            return ApprovalChangeSetValidationError.None;
        }

        private static ApprovalChangeSetValidationError ParseValue(
            FieldDefinition definition,
            JsonValue element,
            out ApprovalChangeValue? value)
        {
            value = null;
            if (element.Kind == JsonNodeKind.Null)
            {
                return definition.AllowNull
                    ? SetValue(out value, new ApprovalChangeValue { Kind = definition.Kind, IsNull = true })
                    : ApprovalChangeSetValidationError.ValueRequired;
            }

            switch (definition.Kind)
            {
                case ApprovalChangeValueKind.Text:
                    if (!TryGetString(element, out var text))
                        return ApprovalChangeSetValidationError.ValueTypeMismatch;
                    if (!definition.AllowEmpty && string.IsNullOrWhiteSpace(text))
                        return ApprovalChangeSetValidationError.ValueRequired;
                    if (text.Length > definition.MaxLength)
                        return ApprovalChangeSetValidationError.ValueTooLong;
                    return SetValue(out value, new ApprovalChangeValue
                    {
                        Kind = definition.Kind,
                        TextValue = text,
                    });

                case ApprovalChangeValueKind.Choice:
                    if (!TryGetInt32(element, out var choice)
                        || !definition.AllowedChoices!.Contains(choice))
                    {
                        return ApprovalChangeSetValidationError.ValueOutOfRange;
                    }
                    return SetValue(out value, new ApprovalChangeValue
                    {
                        Kind = definition.Kind,
                        ChoiceValue = choice,
                    });

                case ApprovalChangeValueKind.Boolean:
                    if (element.Kind != JsonNodeKind.True && element.Kind != JsonNodeKind.False)
                        return ApprovalChangeSetValidationError.ValueTypeMismatch;
                    return SetValue(out value, new ApprovalChangeValue
                    {
                        Kind = definition.Kind,
                        BooleanValue = element.BooleanValue,
                    });

                case ApprovalChangeValueKind.SystemUser:
                    if (!TryGetString(element, out var userText) || !Guid.TryParse(userText, out var userId))
                        return ApprovalChangeSetValidationError.ValueTypeMismatch;
                    if (userId == Guid.Empty)
                        return ApprovalChangeSetValidationError.ValueRequired;
                    return SetValue(out value, new ApprovalChangeValue
                    {
                        Kind = definition.Kind,
                        UserIdValue = userId,
                    });

                case ApprovalChangeValueKind.DateTime:
                    if (!TryParseDateTime(element, out var dateTime, out var dateError))
                        return dateError;
                    return SetValue(out value, new ApprovalChangeValue
                    {
                        Kind = definition.Kind,
                        DateTimeValue = dateTime,
                    });

                default:
                    return ApprovalChangeSetValidationError.ValueTypeMismatch;
            }
        }

        private static ApprovalChangeSetValidationError SetValue(
            out ApprovalChangeValue? destination,
            ApprovalChangeValue value)
        {
            destination = value;
            return ApprovalChangeSetValidationError.None;
        }

        private static bool TryParseDateTime(
            JsonValue element,
            out DateTime? dateTime,
            out ApprovalChangeSetValidationError error)
        {
            dateTime = null;
            error = ApprovalChangeSetValidationError.None;
            if (!TryGetString(element, out var text))
            {
                error = ApprovalChangeSetValidationError.ValueTypeMismatch;
                return false;
            }
            if (!HasExplicitOffset(text))
            {
                error = ApprovalChangeSetValidationError.DateTimeOffsetRequired;
                return false;
            }
            if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
            {
                error = ApprovalChangeSetValidationError.ValueTypeMismatch;
                return false;
            }
            dateTime = parsed.UtcDateTime;
            return true;
        }

        private static bool HasExplicitOffset(string value)
        {
            if (value.EndsWith("Z", StringComparison.Ordinal))
                return true;

            var plus = value.LastIndexOf('+');
            var minus = value.LastIndexOf('-');
            var sign = Math.Max(plus, minus);
            return sign >= 10
                && value.Length - sign == 6
                && value[sign + 3] == ':'
                && IsAsciiDigits(value, sign + 1, 2)
                && IsAsciiDigits(value, sign + 4, 2);
        }

        private static bool IsAsciiDigits(string value, int start, int count)
        {
            if (start < 0 || start + count > value.Length)
                return false;
            for (var index = start; index < start + count; index++)
            {
                if (value[index] < '0' || value[index] > '9')
                    return false;
            }
            return true;
        }

        private static bool TryGetString(JsonValue element, out string value)
        {
            value = string.Empty;
            if (element.Kind != JsonNodeKind.String)
                return false;
            value = element.StringValue ?? string.Empty;
            return true;
        }

        private static bool TryGetInt32(JsonValue element, out int value)
        {
            value = 0;
            return element.Kind == JsonNodeKind.Number
                && int.TryParse(
                    element.NumberText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value);
        }

        private static string CreateFingerprint(ApprovalChangeSet changeSet)
        {
            var canonical = new StringBuilder();
            AppendCanonicalPart(canonical, changeSet.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            AppendCanonicalPart(canonical, changeSet.EntityName);
            AppendCanonicalPart(canonical, changeSet.TargetId.ToString("D"));
            foreach (var operation in changeSet.Operations)
            {
                AppendCanonicalPart(canonical, operation.AttributeName);
                AppendCanonicalPart(canonical, ((int)operation.Value.Kind).ToString(CultureInfo.InvariantCulture));
                AppendCanonicalPart(canonical, operation.Value.IsNull ? "null" : CanonicalValue(operation.Value));
            }

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string CanonicalValue(ApprovalChangeValue value)
        {
            switch (value.Kind)
            {
                case ApprovalChangeValueKind.Text:
                    return "text:" + value.TextValue;
                case ApprovalChangeValueKind.Choice:
                    return "choice:" + value.ChoiceValue!.Value.ToString(CultureInfo.InvariantCulture);
                case ApprovalChangeValueKind.Boolean:
                    return "boolean:" + value.BooleanValue!.Value.ToString().ToLowerInvariant();
                case ApprovalChangeValueKind.DateTime:
                    return "datetime:" + value.DateTimeValue!.Value.ToString("o", CultureInfo.InvariantCulture);
                case ApprovalChangeValueKind.SystemUser:
                    return "systemuser:" + value.UserIdValue!.Value.ToString("D");
                default:
                    throw new InvalidOperationException("変更値の型が不正です。");
            }
        }

        private static void AppendCanonicalPart(StringBuilder builder, string value)
        {
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append('|');
        }

        private static string FieldKey(string entityName, string attributeName)
            => entityName + ":" + attributeName;

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

        private sealed class JsonParseException : Exception
        {
            public JsonParseException(ApprovalChangeSetValidationError error)
            {
                Error = error;
            }

            public ApprovalChangeSetValidationError Error { get; }
        }

        private sealed class StrictJsonParser
        {
            private readonly string _json;
            private int _position;

            public StrictJsonParser(string json)
            {
                _json = json;
            }

            public JsonValue Parse()
            {
                SkipWhitespace();
                var value = ParseValue(0);
                SkipWhitespace();
                if (_position != _json.Length)
                    ThrowMalformed();
                return value;
            }

            private JsonValue ParseValue(int depth)
            {
                if (depth > MaxJsonDepth)
                    ThrowMalformed();
                if (_position >= _json.Length)
                    ThrowMalformed();

                switch (_json[_position])
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
                        if (_json[_position] == '-' || IsAsciiDigit(_json[_position]))
                            return JsonValue.Number(ParseNumber());
                        ThrowMalformed();
                        return JsonValue.Null();
                }
            }

            private JsonValue ParseObject(int depth)
            {
                Advance();
                SkipWhitespace();
                var properties = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                if (TryConsume('}'))
                    return JsonValue.Object(properties);

                while (true)
                {
                    if (_position >= _json.Length || _json[_position] != '"')
                        ThrowMalformed();
                    var name = ParseString();
                    SkipWhitespace();
                    Require(':');
                    SkipWhitespace();
                    var value = ParseValue(depth + 1);
                    if (properties.ContainsKey(name))
                        throw new JsonParseException(ApprovalChangeSetValidationError.DuplicateProperty);
                    properties.Add(name, value);
                    SkipWhitespace();
                    if (TryConsume('}'))
                        return JsonValue.Object(properties);
                    Require(',');
                    SkipWhitespace();
                    if (_position >= _json.Length || _json[_position] != '"')
                        ThrowMalformed();
                }
            }

            private JsonValue ParseArray(int depth)
            {
                Advance();
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
                    if (_position >= _json.Length || _json[_position] == ']')
                        ThrowMalformed();
                }
            }

            private string ParseString()
            {
                Require('"');
                var builder = new StringBuilder();
                while (_position < _json.Length)
                {
                    var character = _json[_position++];
                    if (character == '"')
                        return builder.ToString();
                    if (character < 0x20)
                        ThrowMalformed();
                    if (character != '\\')
                    {
                        builder.Append(character);
                        continue;
                    }

                    if (_position >= _json.Length)
                        ThrowMalformed();
                    var escape = _json[_position++];
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
                    if (_position + 6 > _json.Length
                        || _json[_position] != '\\'
                        || _json[_position + 1] != 'u')
                    {
                        ThrowMalformed();
                    }
                    _position += 2;
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
                if (_position + 4 > _json.Length)
                    ThrowMalformed();
                var value = 0;
                for (var index = 0; index < 4; index++)
                {
                    var digit = HexValue(_json[_position++]);
                    if (digit < 0)
                        ThrowMalformed();
                    value = (value * 16) + digit;
                }
                return value;
            }

            private string ParseNumber()
            {
                var start = _position;
                if (TryConsume('-') && _position >= _json.Length)
                    ThrowMalformed();

                if (TryConsume('0'))
                {
                    if (_position < _json.Length && IsAsciiDigit(_json[_position]))
                        ThrowMalformed();
                }
                else
                {
                    if (_position >= _json.Length || _json[_position] < '1' || _json[_position] > '9')
                        ThrowMalformed();
                    while (_position < _json.Length && IsAsciiDigit(_json[_position]))
                        _position++;
                }

                if (TryConsume('.'))
                {
                    if (_position >= _json.Length || !IsAsciiDigit(_json[_position]))
                        ThrowMalformed();
                    while (_position < _json.Length && IsAsciiDigit(_json[_position]))
                        _position++;
                }

                if (_position < _json.Length && (_json[_position] == 'e' || _json[_position] == 'E'))
                {
                    _position++;
                    if (_position < _json.Length && (_json[_position] == '+' || _json[_position] == '-'))
                        _position++;
                    if (_position >= _json.Length || !IsAsciiDigit(_json[_position]))
                        ThrowMalformed();
                    while (_position < _json.Length && IsAsciiDigit(_json[_position]))
                        _position++;
                }

                return _json.Substring(start, _position - start);
            }

            private void ParseLiteral(string literal)
            {
                if (_position + literal.Length > _json.Length
                    || !string.Equals(_json.Substring(_position, literal.Length), literal, StringComparison.Ordinal))
                {
                    ThrowMalformed();
                }
                _position += literal.Length;
            }

            private void SkipWhitespace()
            {
                while (_position < _json.Length)
                {
                    var character = _json[_position];
                    if (character != ' ' && character != '\t' && character != '\r' && character != '\n')
                        return;
                    _position++;
                }
            }

            private void Require(char expected)
            {
                if (!TryConsume(expected))
                    ThrowMalformed();
            }

            private bool TryConsume(char expected)
            {
                if (_position < _json.Length && _json[_position] == expected)
                {
                    _position++;
                    return true;
                }
                return false;
            }

            private void Advance()
            {
                _position++;
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
                => throw new JsonParseException(ApprovalChangeSetValidationError.JsonMalformed);
        }

        private sealed class FieldDefinition
        {
            private FieldDefinition(
                string entityName,
                string attributeName,
                ApprovalChangeValueKind kind,
                bool allowNull,
                bool allowEmpty,
                int maxLength,
                ISet<int>? allowedChoices)
            {
                EntityName = entityName;
                AttributeName = attributeName;
                Kind = kind;
                AllowNull = allowNull;
                AllowEmpty = allowEmpty;
                MaxLength = maxLength;
                AllowedChoices = allowedChoices;
            }

            public string EntityName { get; }
            public string AttributeName { get; }
            public ApprovalChangeValueKind Kind { get; }
            public bool AllowNull { get; }
            public bool AllowEmpty { get; }
            public int MaxLength { get; }
            public ISet<int>? AllowedChoices { get; }

            public static FieldDefinition Text(
                string entityName,
                string attributeName,
                int maxLength,
                bool allowNull,
                bool allowEmpty)
                => new FieldDefinition(
                    entityName,
                    attributeName,
                    ApprovalChangeValueKind.Text,
                    allowNull,
                    allowEmpty,
                    maxLength,
                    null);

            public static FieldDefinition Choice(
                string entityName,
                string attributeName,
                IEnumerable<int> allowedChoices)
                => new FieldDefinition(
                    entityName,
                    attributeName,
                    ApprovalChangeValueKind.Choice,
                    allowNull: false,
                    allowEmpty: false,
                    maxLength: 0,
                    new HashSet<int>(allowedChoices));

            public static FieldDefinition Boolean(string entityName, string attributeName)
                => new FieldDefinition(
                    entityName,
                    attributeName,
                    ApprovalChangeValueKind.Boolean,
                    allowNull: false,
                    allowEmpty: false,
                    maxLength: 0,
                    null);

            public static FieldDefinition SystemUser(string entityName, string attributeName)
                => new FieldDefinition(
                    entityName,
                    attributeName,
                    ApprovalChangeValueKind.SystemUser,
                    allowNull: false,
                    allowEmpty: false,
                    maxLength: 0,
                    null);

            public static FieldDefinition DateTime(string entityName, string attributeName)
                => new FieldDefinition(
                    entityName,
                    attributeName,
                    ApprovalChangeValueKind.DateTime,
                    allowNull: true,
                    allowEmpty: false,
                    maxLength: 0,
                    null);
        }
    }
}
