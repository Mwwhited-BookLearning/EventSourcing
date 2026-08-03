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
}
