using EventStore.SchemaRegistry;
using EventStore.SpecGeneration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Follow API + Filter Pushdown"'s own exit criteria (docs/08-build-plan.md):
// "/asyncapi.json includes the Follow channel, served anonymously,
// cache-invalidated on the next registration; a maskable property already
// appears wrapped as oneOf[value,masked,erased] in the generated document."
internal static class AsyncApiScenarioAssertions
{
    public static async Task AsyncApiDocumentIncludesTheFollowChannelForARegisteredType(SchemaRegistryService registry, AsyncApiDocumentBuilder specBuilder)
    {
        const string appId = "asyncapi-demo-1";
        await registry.RegisterAsync("WidgetFollowed", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Name": { "type": "string" } }, "required": ["Name"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.WidgetId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var json = await specBuilder.GetOrBuildJsonAsync();
        StringAssert.Contains(json, "\"asyncapi\"");
        StringAssert.Contains(json, "/follow/widgetfollowed");
    }

    public static async Task RegisteringANewTypeInvalidatesTheCachedAsyncApiDocument(SchemaRegistryService registry, AsyncApiDocumentBuilder specBuilder)
    {
        const string appId = "asyncapi-demo-2";
        var beforeJson = await specBuilder.GetOrBuildJsonAsync(); // populates the cache

        await registry.RegisterAsync("GizmoFollowed", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: """{ "type": "object", "properties": { "Name": { "type": "string" } }, "required": ["Name"] }""",
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.GizmoId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var afterJson = await specBuilder.GetOrBuildJsonAsync();
        Assert.AreNotEqual(beforeJson, afterJson, "expected the cached document to be invalidated and rebuilt after registration");
        StringAssert.Contains(afterJson, "/follow/gizmofollowed");
    }

    public static async Task AMaskablePropertyAppearsWrappedAsOneOfValueMaskedErasedInTheGeneratedDocument(SchemaRegistryService registry, AsyncApiDocumentBuilder specBuilder)
    {
        const string appId = "asyncapi-demo-3";
        const string schema = """
            {
              "type": "object",
              "properties": {
                "Ssn": { "type": "string", "x-masking": { "strategy": "FixedValue", "requiredClaim": "role:admin" } }
              },
              "required": ["Ssn"]
            }
            """;
        await registry.RegisterAsync("SensitiveThingHappened", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: schema,
            FilterableFields: [], ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var json = await specBuilder.GetOrBuildJsonAsync();
        StringAssert.Contains(json, "\"oneOf\"");
        StringAssert.Contains(json, "\"masked\"");
        StringAssert.Contains(json, "\"erased\"");
    }
}
