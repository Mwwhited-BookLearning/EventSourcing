using System.Security.Claims;
using System.Text.Json.Nodes;
using EventStore.Follow.Api;
using EventStore.Inbox;
using EventStore.SchemaRegistry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Property-Level Masking (data enforcement)"
// (docs/08-build-plan.md, ADR-009). The *schema* half (x-masking structural
// validation, the oneOf wrapper in generated docs) is already covered by
// SchemaRegistryScenarioAssertions/AsyncApiScenarioAssertions, built in
// earlier items -- this file covers only the data half this item actually
// adds: IPayloadMasker's real masking behavior through the Follow pipeline.
internal static class MaskingScenarioAssertions
{
    private static readonly TimeSpan PerItemTimeout = TimeSpan.FromSeconds(10);

    private static async Task<List<FollowedEvent>> Collect(IAsyncEnumerator<FollowedEvent> enumerator, int count, CancellationTokenSource cts)
    {
        var results = new List<FollowedEvent>();
        for (var i = 0; i < count; i++)
        {
            var moveNext = enumerator.MoveNextAsync().AsTask();
            var winner = await Task.WhenAny(moveNext, Task.Delay(PerItemTimeout, cts.Token));
            if (winner != moveNext)
            {
                cts.Cancel();
                Assert.Fail($"Timed out waiting for item {i + 1} of {count}");
            }
            Assert.IsTrue(await moveNext, $"stream ended after {i} of {count} expected items");
            results.Add(enumerator.Current);
        }
        return results;
    }

    private static async Task<JsonNode> FollowOneEvent(FollowService follow, string appId, string typeName, ClaimsPrincipal user)
    {
        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: 0), user, cts.Token);
        await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);
        var events = await Collect(enumerator, 1, cts);
        cts.Cancel();
        return events.Single().MaskedPayload!;
    }

    public static async Task AFollowerWithoutTheMaskingClaimSeesMaskedAndWithItSeesValue(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "masking-demo-1";
        const string typeName = "PatientRecorded1";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "Diagnosis": { "type": "string", "x-masking": {
                        "strategy": "FixedValue", "requiredClaim": "clearance:phi", "maskedValue": "REDACTED",
                        "regulatoryClassification": "PHI" } }
                  }, "required": ["Diagnosis"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Diagnosis": "Hypertension" }""", null, null), TestClaimsPrincipal.None);

        var withoutClaim = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.None);
        Assert.IsNull(withoutClaim["Diagnosis"]!["value"]);
        Assert.AreEqual("REDACTED", withoutClaim["Diagnosis"]!["masked"]!.GetValue<string>());
        Assert.IsFalse(((JsonObject)withoutClaim["Diagnosis"]!).ContainsKey("regulatoryClassification"),
            "regulatoryClassification is schema-only documentation -- it must never appear in the runtime wrapper");

        var withClaim = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.With("clearance:phi"));
        Assert.AreEqual("Hypertension", withClaim["Diagnosis"]!["value"]!.GetValue<string>());
        Assert.IsNull(withClaim["Diagnosis"]!["masked"]);
    }

    public static async Task MaskingAppliesEvenWhenTheEventTypeHasNoRequiredReadClaimAtAll(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "masking-demo-2";
        const string typeName = "OrderPlacedMasked2";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "CardNumber": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "pci:view" } },
                    "Amount": { "type": "number" }
                  }, "required": ["CardNumber", "Amount"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null)); // no RequiredClaims at all -- anyone can Follow this type
        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "CardNumber": "4111111111111111", "Amount": 50 }""", null, null), TestClaimsPrincipal.None);

        var payload = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.None);

        Assert.AreEqual("***", payload["CardNumber"]!["masked"]!.GetValue<string>()); // FixedValue's own default
        Assert.AreEqual(50, payload["Amount"]!.GetValue<double>()); // a property without x-masking is never wrapped
    }

    public static async Task PartialRevealShowsOnlyTheConfiguredFirstAndLastCharactersPreservingSeparators(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "masking-demo-3";
        const string typeName = "PaymentRecorded3";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "Ssn": { "type": "string", "x-masking": {
                        "strategy": "PartialReveal", "requiredClaim": "pii:view",
                        "showFirst": 0, "showLast": 4, "maskChar": "X", "preserveSeparators": true } }
                  }, "required": ["Ssn"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Ssn": "123-45-6789" }""", null, null), TestClaimsPrincipal.None);

        var payload = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.None);

        Assert.AreEqual("XXX-XX-6789", payload["Ssn"]!["masked"]!.GetValue<string>());
    }

    public static async Task HashMaskingIsCorrelatableAcrossEventsWithoutRevealingTheRealValue(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "masking-demo-4";
        const string typeName = "LabResultRecorded4";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: $$"""
                { "type": "object", "properties": {
                    "Ssn": { "type": "string", "x-masking": { "strategy": "Hash", "requiredClaim": "pii:view", "keyId": "{{MaskingTestSupport.TestHmacKeyId}}" } }
                  }, "required": ["Ssn"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Ssn": "123-45-6789" }""", null, null), TestClaimsPrincipal.None);
        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Ssn": "123-45-6789" }""", null, null), TestClaimsPrincipal.None);
        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Ssn": "999-99-9999" }""", null, null), TestClaimsPrincipal.None);

        using var cts = new CancellationTokenSource();
        var connected = (FollowResult.Connected)await follow.ConnectAsync(
            typeName, new FollowRequest(appId, Filter: null, Mode: "Replay", FromSequenceNumber: 0), TestClaimsPrincipal.None, cts.Token);
        await using var enumerator = connected.Events.GetAsyncEnumerator(cts.Token);
        var events = await Collect(enumerator, 3, cts);
        cts.Cancel();

        var hashes = events.Select(e => e.MaskedPayload!["Ssn"]!["masked"]!.GetValue<string>()).ToList();
        Assert.AreEqual(hashes[0], hashes[1], "two events sharing the same real value must produce identical masked hashes");
        Assert.AreNotEqual(hashes[0], hashes[2], "different real values must not collide");
        Assert.DoesNotContain("123-45-6789", hashes[0], "the hash must never contain or reveal the real value");
    }

    public static async Task ARequiredNonNullableFieldIsStillMaskableWithNoNullWorkaround(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "masking-demo-5";
        const string typeName = "OrderPlacedRequiredMasked5";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "Amount": { "type": "number", "x-masking": { "strategy": "FixedValue", "requiredClaim": "finance:view" } }
                  }, "required": ["Amount"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "Amount": 0 }""", null, null), TestClaimsPrincipal.None); // 0, not null -- a required, non-nullable field

        var payload = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.None);

        Assert.AreEqual("***", payload["Amount"]!["masked"]!.GetValue<string>());
    }

    public static async Task ALegitimatelyAbsentFieldStaysAbsentNotWrapped(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "masking-demo-6";
        const string typeName = "OrderPlacedOptionalMasked6";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "OrderId": { "type": "string" },
                    "Coupon": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "promo:view" } }
                  }, "required": ["OrderId"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.OrderId", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """{ "OrderId": "ord-6" }""", null, null), TestClaimsPrincipal.None); // Coupon omitted entirely

        var payload = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.None);

        Assert.AreEqual("ord-6", payload["OrderId"]!.GetValue<string>());
        Assert.IsFalse(((JsonObject)payload).ContainsKey("Coupon"), "an absent field must stay absent, never wrapped as {masked:...}");
    }

    public static async Task ScalarArrayWrapsEachElementAndComplexArrayWrapsOnlyTheMaskedPropertyPerElement(
        SchemaRegistryService registry, PublishService publish, FollowService follow)
    {
        const string appId = "masking-demo-7";
        const string typeName = "PanelRecorded7";
        await registry.RegisterAsync(typeName, new RegisterEventTypeRequest(
            AppId: appId,
            JsonSchema: """
                { "type": "object", "properties": {
                    "Tags": { "type": "array", "items": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "tag:view" } } },
                    "Results": { "type": "array", "items": { "type": "object", "properties": {
                        "TestName": { "type": "string" },
                        "Value": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "pii:view" } }
                      } } }
                  }, "required": ["Tags", "Results"] }
                """,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id", ParentValidationMode: "Permissive",
            RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        await publish.PublishAsync(typeName, new PublishEventRequest(
            appId, 1, """
                { "Tags": ["urgent", "flagged"],
                  "Results": [ { "TestName": "Glucose", "Value": "110" }, { "TestName": "A1C", "Value": "5.4" } ] }
                """, null, null), TestClaimsPrincipal.None);

        var payload = await FollowOneEvent(follow, appId, typeName, TestClaimsPrincipal.None);

        var tags = (JsonArray)payload["Tags"]!;
        Assert.HasCount(2, tags);
        Assert.IsTrue(tags.All(t => t!["masked"] is not null), "a scalar array's items schema wraps each element");

        var results = (JsonArray)payload["Results"]!;
        Assert.AreEqual("Glucose", results[0]!["TestName"]!.GetValue<string>());
        Assert.IsNotNull(results[0]!["Value"]!["masked"]);
        Assert.AreEqual("A1C", results[1]!["TestName"]!.GetValue<string>());
        Assert.IsNotNull(results[1]!["Value"]!["masked"]);
    }

    public static Task ALogCallTouchingAClassifiedFieldIsVerifiedRedactedNotJustTheResponsePath(CapturingLoggerProvider logs)
    {
        Assert.IsTrue(
            logs.Messages.Any(m => m.Contains("PHI-classified field")),
            "expected at least one log message noting a PHI-classified field was evaluated");
        Assert.IsFalse(
            logs.Messages.Any(m => m.Contains("Hypertension")),
            "the real value of a regulatoryClassification-tagged field must never reach a log message unredacted");
        return Task.CompletedTask;
    }
}
