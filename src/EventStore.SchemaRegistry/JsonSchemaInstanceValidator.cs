using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EventStore.SchemaRegistry;

// Hand-written, not JsonSchema.Net -- same reasoning as
// EventStore.SchemaRegistry.MaskingSchemaValidator's own structural check
// (see docs/changes/2026-08-03.md): every real schema in this design carries
// an undeclared "x-masking" vendor extension, which JsonSchema.Net's default
// dialect rejects outright. This validates a payload INSTANCE against a
// schema (required/type/properties/items) -- a different job from
// SchemaRegistryService's schema-DOCUMENT well-formedness check, but the
// same "tolerate any unrecognized keyword" posture, for the same reason.
// Public and living here (not EventStore.Inbox, its original home) so both
// EventStore.Inbox and EventStore.Router can use the identical check without
// either depending on the other (build-plan item 12, "Entity-Centric Core
// Rebuild": the Router, not the Inbox, is what actually calls this now).
public static class JsonSchemaInstanceValidator
{
    public static bool Validate(JsonNode? schema, JsonNode? payload, List<string> errors, string path = "$")
    {
        if (schema is not JsonObject schemaObject)
            return true; // no constraints (or a bare `true` schema) -- nothing to check

        // ADR-057 -- a property carrying x-masking.regulatoryClassification is
        // stored (and, for an upcast result, re-derived) as ciphertext, always
        // a base64 string, regardless of its originally-declared type -- both
        // this validator's callers (RouterWorker's own fold-time re-validation,
        // UpcastMaterializer's own upcast-result check) only ever see the
        // ALREADY-STORED/re-derived form, never the plaintext PublishService
        // encrypted before persisting. Exempting type-checking here, not by
        // teaching every caller to skip classified leaves individually, keeps
        // this the one place that knows "ciphertext never matches its own
        // declared type, and that's expected, not a validation failure."
        if (schemaObject.TryGetPropertyValue("x-masking", out var maskingNode) &&
            maskingNode is JsonObject { } masking && masking["regulatoryClassification"] is not null)
            return true;

        var ok = true;

        if (schemaObject["type"] is { } typeNode)
        {
            var expectedTypes = typeNode is JsonArray typeArray
                ? typeArray.Select(t => t!.GetValue<string>())
                : [typeNode.GetValue<string>()];
            if (!expectedTypes.Any(t => MatchesType(t, payload)))
            {
                errors.Add($"{path}: expected type {string.Join(" or ", expectedTypes)}, got {DescribeActualType(payload)}");
                return false; // no point checking properties/items against the wrong shape
            }
        }

        if (schemaObject["required"] is JsonArray required)
        {
            if (payload is not JsonObject payloadObject)
                return false;
            foreach (var requiredName in required)
            {
                if (!payloadObject.ContainsKey(requiredName!.GetValue<string>()))
                {
                    errors.Add($"{path}: missing required property '{requiredName.GetValue<string>()}'");
                    ok = false;
                }
            }
        }

        if (schemaObject["properties"] is JsonObject properties && payload is JsonObject payloadObj)
        {
            foreach (var (propertyName, propertySchema) in properties)
            {
                if (payloadObj.TryGetPropertyValue(propertyName, out var propertyValue) && propertyValue is not null &&
                    !Validate(propertySchema, propertyValue, errors, $"{path}.{propertyName}"))
                    ok = false;
            }
        }

        if (schemaObject["items"] is { } itemsSchema && payload is JsonArray payloadArray)
        {
            for (var i = 0; i < payloadArray.Count; i++)
                if (!Validate(itemsSchema, payloadArray[i], errors, $"{path}[{i}]"))
                    ok = false;
        }

        // TODO.md, "Field-level validation and datatype rules" -- this
        // validator's own real, previously-open gap: type/required/
        // properties/items only, no pattern/length/range/enum/format at
        // all. Extends the SAME hand-written, x-masking-tolerant approach
        // (see the class comment for why JsonSchema.Net's own dialect
        // isn't used) rather than switching implementations -- adding
        // keywords is additive, doesn't touch the x-masking-exemption
        // reasoning that made this hand-written in the first place. Only
        // ever runs once the payload already matched its declared `type`
        // above (a string-only keyword against a non-string payload is
        // simply not applicable, not a separate failure).
        if (!CheckStringConstraints(schemaObject, payload, errors, path)) ok = false;
        if (!CheckNumberConstraints(schemaObject, payload, errors, path)) ok = false;
        if (!CheckEnum(schemaObject, payload, errors, path)) ok = false;

        return ok;
    }

    private static bool CheckStringConstraints(JsonObject schemaObject, JsonNode? payload, List<string> errors, string path)
    {
        if (payload is not JsonNode { } node || node.GetValueKind() != JsonValueKind.String)
            return true;
        var value = node.GetValue<string>();
        var ok = true;

        if (schemaObject["minLength"] is { } minLengthNode && value.Length < minLengthNode.GetValue<int>())
        {
            errors.Add($"{path}: length {value.Length} is below minLength {minLengthNode.GetValue<int>()}");
            ok = false;
        }
        if (schemaObject["maxLength"] is { } maxLengthNode && value.Length > maxLengthNode.GetValue<int>())
        {
            errors.Add($"{path}: length {value.Length} exceeds maxLength {maxLengthNode.GetValue<int>()}");
            ok = false;
        }
        if (schemaObject["pattern"] is { } patternNode)
        {
            var pattern = patternNode.GetValue<string>();
            // A malformed `pattern` in the schema itself is a schema-
            // authoring bug, not something this payload can be blamed
            // for -- reported as a validation error against THIS payload
            // (so it's visible at all, via the same SchemaStatus channel
            // every other failure already surfaces through) rather than
            // thrown, which would take down the whole fold for every
            // future event of this type until the schema is fixed.
            try
            {
                if (!Regex.IsMatch(value, pattern))
                {
                    errors.Add($"{path}: value does not match pattern '{pattern}'");
                    ok = false;
                }
            }
            catch (ArgumentException)
            {
                errors.Add($"{path}: schema's own pattern '{pattern}' is not a valid regular expression");
                ok = false;
            }
        }
        if (schemaObject["format"] is { } formatNode && !MatchesFormat(formatNode.GetValue<string>(), value))
        {
            errors.Add($"{path}: value does not match format '{formatNode.GetValue<string>()}'");
            ok = false;
        }

        return ok;
    }

    // A deliberately small, real-standard-library-backed set, not a
    // bespoke reimplementation of the full JSON Schema format vocabulary
    // (buy over build -- .NET's own Uri/MailAddress parsers, not a hand-
    // rolled regex, for the two formats this project has an actual use
    // for so far). An unrecognized format name is tolerated, not failed
    // -- the same "don't fail closed on our own uncertainty" posture
    // MatchesType already takes for an unrecognized `type` value.
    private static bool MatchesFormat(string format, string value) => format switch
    {
        "date-time" => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _),
        "email" => MailAddress.TryCreate(value, out _),
        "uri" => Uri.TryCreate(value, UriKind.Absolute, out _),
        _ => true,
    };

    private static bool CheckNumberConstraints(JsonObject schemaObject, JsonNode? payload, List<string> errors, string path)
    {
        if (payload is not JsonNode { } node || node.GetValueKind() != JsonValueKind.Number)
            return true;
        var value = node.GetValue<double>();
        var ok = true;

        if (schemaObject["minimum"] is { } minNode && value < minNode.GetValue<double>())
        {
            errors.Add($"{path}: value {value} is below minimum {minNode.GetValue<double>()}");
            ok = false;
        }
        if (schemaObject["maximum"] is { } maxNode && value > maxNode.GetValue<double>())
        {
            errors.Add($"{path}: value {value} exceeds maximum {maxNode.GetValue<double>()}");
            ok = false;
        }
        if (schemaObject["exclusiveMinimum"] is { } exMinNode && value <= exMinNode.GetValue<double>())
        {
            errors.Add($"{path}: value {value} does not exceed exclusiveMinimum {exMinNode.GetValue<double>()}");
            ok = false;
        }
        if (schemaObject["exclusiveMaximum"] is { } exMaxNode && value >= exMaxNode.GetValue<double>())
        {
            errors.Add($"{path}: value {value} does not fall below exclusiveMaximum {exMaxNode.GetValue<double>()}");
            ok = false;
        }

        return ok;
    }

    // Applies regardless of type (JSON Schema's own `enum` is valid
    // alongside any `type`, including mixed-type enums) -- deep JSON
    // equality via a normalized string comparison (JsonNode has no
    // built-in structural equality), sufficient for the scalar/short-
    // array enum values this design's own schemas actually use.
    private static bool CheckEnum(JsonObject schemaObject, JsonNode? payload, List<string> errors, string path)
    {
        if (schemaObject["enum"] is not JsonArray allowedValues)
            return true;

        // ADR-038's own explicit contract: "every enum-like field...
        // declares a fallback... the raw string travels through
        // unmodified, never substituted or dropped" -- a value outside
        // the declared list is the EXPECTED forward-compatibility case
        // this flag exists for (a newer publisher, an older schema
        // registration this Router hasn't caught up to yet), not a real
        // schema violation. Found before shipping, not after: a real
        // existing test (CompatibilityGraphQlHttpSqliteTests) already
        // published exactly this scenario and asserts the event travels
        // through unmodified -- this check would have started marking
        // every one of those "invalid" the moment enum enforcement was
        // added, silently contradicting ADR-038's own contract. Same
        // "vendor extension flag changes how validation behaves for this
        // field" shape as the x-masking exemption at the top of Validate.
        if (schemaObject["x-enum-fallback"]?.GetValue<bool>() == true)
            return true;

        var payloadJson = payload?.ToJsonString();
        if (allowedValues.Any(allowed => allowed?.ToJsonString() == payloadJson))
            return true;
        errors.Add($"{path}: value is not one of the allowed enum values");
        return false;
    }

    private static bool MatchesType(string type, JsonNode? value)
    {
        if (value is null)
            return type == "null";

        var kind = value.GetValueKind();
        return type switch
        {
            "object" => kind == JsonValueKind.Object,
            "array" => kind == JsonValueKind.Array,
            "string" => kind == JsonValueKind.String,
            "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
            "number" => kind == JsonValueKind.Number,
            "integer" => kind == JsonValueKind.Number && IsIntegerValued(value),
            "null" => kind == JsonValueKind.Null,
            _ => true, // unrecognized type keyword value -- tolerate, don't fail closed on our own uncertainty
        };
    }

    private static bool IsIntegerValued(JsonNode value)
    {
        var d = value.GetValue<double>();
        return d == Math.Floor(d) && !double.IsInfinity(d);
    }

    private static string DescribeActualType(JsonNode? value) =>
        value is null ? "null" : value.GetValueKind().ToString();
}
