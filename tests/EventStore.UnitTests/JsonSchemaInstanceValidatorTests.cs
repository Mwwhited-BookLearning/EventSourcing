using System.Text.Json.Nodes;
using EventStore.SchemaRegistry;

namespace EventStore.UnitTests;

// TODO.md, "Field-level validation and datatype rules" -- direct
// coverage for the keywords this session added (pattern/minLength/
// maxLength/minimum/maximum/exclusiveMinimum/exclusiveMaximum/enum/a
// small format subset), alongside the pre-existing type/required/
// properties/items coverage this validator already had (exercised
// indirectly via RouterWorker/UpcastMaterializer's own integration
// tests, not duplicated here).
[TestClass]
public class JsonSchemaInstanceValidatorTests
{
    private static bool Validate(string schemaJson, string payloadJson, out List<string> errors)
    {
        errors = [];
        return JsonSchemaInstanceValidator.Validate(JsonNode.Parse(schemaJson), JsonNode.Parse(payloadJson), errors);
    }

    [TestMethod]
    public void MinLengthRejectsAStringShorterThanRequired()
    {
        var ok = Validate("""{ "type": "string", "minLength": 3 }""", "\"ab\"", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("minLength", errors[0]);
    }

    [TestMethod]
    public void MinLengthAcceptsAStringAtExactlyTheBoundary()
    {
        var ok = Validate("""{ "type": "string", "minLength": 3 }""", "\"abc\"", out _);
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void MaxLengthRejectsAStringLongerThanAllowed()
    {
        var ok = Validate("""{ "type": "string", "maxLength": 3 }""", "\"abcd\"", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("maxLength", errors[0]);
    }

    [TestMethod]
    public void PatternRejectsAStringThatDoesNotMatch()
    {
        var ok = Validate("""{ "type": "string", "pattern": "^[A-Z]{2}[0-9]{4}$" }""", "\"badvalue\"", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("pattern", errors[0]);
    }

    [TestMethod]
    public void PatternAcceptsAMatchingString()
    {
        var ok = Validate("""{ "type": "string", "pattern": "^[A-Z]{2}[0-9]{4}$" }""", "\"AB1234\"", out _);
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void AMalformedPatternInTheSchemaItselfIsReportedAsAnErrorNotThrown()
    {
        var ok = Validate("""{ "type": "string", "pattern": "(unclosed" }""", "\"anything\"", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("not a valid regular expression", errors[0]);
    }

    [TestMethod]
    public void MinimumRejectsANumberBelowTheBound()
    {
        var ok = Validate("""{ "type": "number", "minimum": 0 }""", "-1", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("minimum", errors[0]);
    }

    [TestMethod]
    public void MaximumRejectsANumberAboveTheBound()
    {
        var ok = Validate("""{ "type": "number", "maximum": 1 }""", "1.5", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("maximum", errors[0]);
    }

    [TestMethod]
    public void MinimumAndMaximumAcceptAConfidenceScoreInRange()
    {
        // The real, concrete motivating case (TODO.md/ADR-100's own
        // MatchConfidence/LivenessConfidence fields) -- 0.0-1.0 inclusive.
        var ok = Validate("""{ "type": "number", "minimum": 0, "maximum": 1 }""", "0.87", out _);
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void ExclusiveMinimumRejectsAValueEqualToTheBound()
    {
        var ok = Validate("""{ "type": "number", "exclusiveMinimum": 0 }""", "0", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("exclusiveMinimum", errors[0]);
    }

    [TestMethod]
    public void ExclusiveMaximumRejectsAValueEqualToTheBound()
    {
        var ok = Validate("""{ "type": "number", "exclusiveMaximum": 1 }""", "1", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("exclusiveMaximum", errors[0]);
    }

    [TestMethod]
    public void EnumRejectsAValueNotInTheAllowedList()
    {
        var ok = Validate("""{ "type": "string", "enum": ["Mild", "Moderate", "Severe"] }""", "\"Critical\"", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("enum", errors[0]);
    }

    [TestMethod]
    public void EnumAcceptsAnAllowedValue()
    {
        var ok = Validate("""{ "type": "string", "enum": ["Mild", "Moderate", "Severe"] }""", "\"Severe\"", out _);
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void FormatDateTimeRejectsAnUnparsableValue()
    {
        var ok = Validate("""{ "type": "string", "format": "date-time" }""", "\"not-a-date\"", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("format", errors[0]);
    }

    [TestMethod]
    public void FormatDateTimeAcceptsAnIso8601Value()
    {
        var ok = Validate("""{ "type": "string", "format": "date-time" }""", "\"2026-08-29T12:00:00Z\"", out _);
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void FormatEmailRejectsAnInvalidAddress()
    {
        var ok = Validate("""{ "type": "string", "format": "email" }""", "\"not-an-email\"", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("format", errors[0]);
    }

    [TestMethod]
    public void AnUnrecognizedFormatNameIsToleratedNotFailed()
    {
        // Same "don't fail closed on our own uncertainty" posture
        // MatchesType already takes for an unrecognized `type` keyword.
        var ok = Validate("""{ "type": "string", "format": "some-future-format-this-validator-does-not-know" }""", "\"anything\"", out _);
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void StringConstraintsAreSkippedForANonStringPayload()
    {
        // minLength/pattern/format are only applicable once the payload
        // already matched its declared type -- a number field with no
        // "type" declared at all (or a mismatched one, caught earlier)
        // must not spuriously fail a string-only check.
        var ok = Validate("""{ "minLength": 3 }""", "42", out _);
        Assert.IsTrue(ok);
    }

    [TestMethod]
    public void MultipleConstraintViolationsOnTheSamePropertyAreAllReported()
    {
        var ok = Validate("""{ "type": "string", "minLength": 10, "pattern": "^[0-9]+$" }""", "\"ab\"", out var errors);
        Assert.IsFalse(ok);
        Assert.AreEqual(2, errors.Count, string.Join("; ", errors));
    }

    [TestMethod]
    public void XEnumFallbackExemptsAnOutOfListValueFromBeingReportedAsInvalid()
    {
        // ADR-038's own contract (CompatibilityGraphQlHttpSqliteTests'
        // real, already-existing scenario): "PartiallyRefunded" was never
        // in this field's own declared enum, and must travel through
        // unmodified, not get marked SchemaStatus: "invalid" -- that's
        // the entire point of x-enum-fallback existing at all.
        const string schema = """{ "type": "string", "enum": ["Placed", "Shipped", "Delivered"], "x-enum-fallback": true }""";
        var ok = Validate(schema, "\"PartiallyRefunded\"", out var errors);
        Assert.IsTrue(ok, string.Join("; ", errors));
    }

    [TestMethod]
    public void EnumWithoutTheFallbackFlagStillRejectsAnOutOfListValue()
    {
        // The exemption is opt-in per field, not a blanket softening of
        // enum enforcement everywhere -- a field that never declared
        // x-enum-fallback keeps the strict behavior.
        const string schema = """{ "type": "string", "enum": ["Placed", "Shipped", "Delivered"] }""";
        var ok = Validate(schema, "\"PartiallyRefunded\"", out var errors);
        Assert.IsFalse(ok);
        Assert.Contains("enum", errors[0]);
    }

    [TestMethod]
    public void AClassifiedFieldsCiphertextIsExemptFromTheseConstraintsTooNotJustType()
    {
        // ADR-057's own pre-existing exemption (x-masking.regulatoryClassification
        // -- ciphertext never matches its own declared constraints, and
        // that's expected) must still short-circuit BEFORE any of the new
        // keyword checks run, not just the type check.
        const string schema = """
            { "type": "string", "minLength": 50, "pattern": "^[A-Z]+$",
              "x-masking": { "regulatoryClassification": "PII" } }
            """;
        var ok = Validate(schema, "\"c2hvcnQ=\"", out var errors);
        Assert.IsTrue(ok);
        Assert.AreEqual(0, errors.Count);
    }
}
