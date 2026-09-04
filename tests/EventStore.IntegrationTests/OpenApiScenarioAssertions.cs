using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Publish API's own exit criterion: "/openapi.json includes /publish/{event-type}
// with the full envelope shape, served anonymously, cache-invalidated on the next
// registration" (docs/08-build-plan.md).
internal static class OpenApiScenarioAssertions
{
    public static async Task OpenApiDocumentIncludesRegisteredPublishPaths(SchemaRegistryService registry, OpenApiDocumentBuilder specBuilder)
    {
        const string appId = "openapi-demo";
        await registry.RegisterAsync("WidgetCreated", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Name": { "type": "string" } }, "required": ["Name"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.WidgetId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var json = await specBuilder.GetOrBuildJsonAsync();
        StringAssert.Contains(json, "/publish/widgetcreated");
        StringAssert.Contains(json, "schemaVersion");
    }

    public static async Task RegisteringANewTypeInvalidatesTheCachedDocument(SchemaRegistryService registry, OpenApiDocumentBuilder specBuilder)
    {
        const string appId = "openapi-demo-2";
        var beforeJson = await specBuilder.GetOrBuildJsonAsync(); // populates the cache

        await registry.RegisterAsync("GadgetCreated", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Name": { "type": "string" } }, "required": ["Name"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.GadgetId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var afterJson = await specBuilder.GetOrBuildJsonAsync();
        Assert.AreNotEqual(beforeJson, afterJson, "expected the cached document to be invalidated and rebuilt after registration");
        StringAssert.Contains(afterJson, "/publish/gadgetcreated");
    }

    // ADR-050 -- "x-required-claims at the schema/operation level," this
    // ADR's own entity-level counterpart to ADR-009's already-emitted
    // property-level x-masking. Publish-direction only, matching this
    // operation's own direction (a Read-direction entry on the same type
    // must NOT leak into the Publish operation's own extension).
    public static async Task ARegisteredPublishDirectionClaimAppearsAsAnXRequiredClaimsExtension(
        SchemaRegistryService registry, OpenApiDocumentBuilder specBuilder)
    {
        const string appId = "openapi-demo-3";
        await registry.RegisterAsync("SprocketCreated", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Name": { "type": "string" } }, "required": ["Name"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.SprocketId",
            ParentValidationMode: null,
            RequiredClaims: [new RequiredClaimRequest("Publish", "scope:sprockets:create"), new RequiredClaimRequest("Read", "scope:sprockets:read")],
            UpcastFromPrevious: null, DowncastToPrevious: null));

        var json = await specBuilder.GetOrBuildJsonAsync();
        StringAssert.Contains(json, "\"x-required-claims\"");
        StringAssert.Contains(json, "scope:sprockets:create");
        Assert.IsFalse(json.Contains("scope:sprockets:read"), "the Publish operation's own extension must carry only Publish-direction claims, never Read-direction ones");
    }

    // Regression test for a real bug found while proving the SDK-codegen
    // story end to end (docs/changes/2026-09-04.md): `payload` used to be
    // described as the event type's own JSON Schema inlined as a nested
    // object, but src/EventStore.Inbox/PublishEventRequest.cs's real model
    // binder deserializes it as raw JSON TEXT -- an OpenAPI-driven codegen
    // tool (Kiota, confirmed for real) generates a client whose typed
    // request body doesn't match what the real endpoint accepts, a genuine
    // 500 every publish call. Asserts the spec now describes `payload` as
    // `"type":"string"`, with the real per-event-type schema preserved
    // (only) via the `x-payload-schema` extension.
    public static async Task PublishPayloadIsDescribedAsAStringNotANestedObject(SchemaRegistryService registry, OpenApiDocumentBuilder specBuilder)
    {
        const string appId = "openapi-demo-4";
        await registry.RegisterAsync("GizmoCreated", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Name": { "type": "string" } }, "required": ["Name"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.GizmoId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var json = await specBuilder.GetOrBuildJsonAsync();
        StringAssert.Contains(json, "\"x-payload-schema\"");

        var document = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var payloadSchema = document["paths"]!["/publish/gizmocreated"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!["properties"]!["payload"]!;
        Assert.AreEqual("string", payloadSchema["type"]!.GetValue<string>(), "payload must be described as a raw string, matching PublishEventRequest.Payload's real type -- not the nested event-type schema, which produces a broken request body for any OpenAPI-driven codegen tool");
    }
}
