using EventStore.SchemaRegistry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Schema Registry" (docs/08-build-plan.md), mirroring
// docs/features/schema-registry.md's Gherkin -- excluding its GraphQL
// listing scenario, superseded by "GraphQL-Only Query Layer" (not yet
// built; see the correction note on this build-plan item) -- plus the four
// masking-registration scenarios docs/08-build-plan.md's exit criteria cite
// from docs/features/masking.md.
internal static class SchemaRegistryScenarioAssertions
{
    private const string OrderPlacedSchema = """
        { "type": "object", "properties": { "Amount": { "type": "number" } }, "required": ["Amount"] }
        """;

    public static async Task RegisteringANewEventTypeCreatesVersion1(SchemaRegistryService service)
    {
        var result = await service.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: "demo", JsonSchema: OrderPlacedSchema,
            FilterableFields: [new FilterableFieldRequest("$.Amount", "Number", IsIndexed: true)],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        Assert.IsInstanceOfType<RegisterEventTypeResult.Success>(result);
        Assert.AreEqual(1, ((RegisterEventTypeResult.Success)result).Version);

        var active = await service.GetActiveAsync("demo", "OrderPlaced");
        Assert.IsNotNull(active);
        Assert.AreEqual(1, active.Version);
        Assert.IsTrue(active.IsActive);
    }

    public static async Task RegisteringSameNameUnderDifferentAppIdIsIndependent(SchemaRegistryService service)
    {
        // A fresh type name, distinct from every other scenario's -- this scenario's
        // own job is AppId isolation, not re-proving version-1 registration, so it
        // must not collide with "OrderPlaced" under "demo" from an earlier scenario
        // sharing this same service/database.
        await service.RegisterAsync("IsolationCheck", new RegisterEventTypeRequest(
            AppId: "demo", JsonSchema: OrderPlacedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var acmeSchema = """{ "type": "object", "properties": { "TotalCents": { "type": "integer" } }, "required": ["TotalCents"] }""";
        var result = await service.RegisterAsync("IsolationCheck", new RegisterEventTypeRequest(
            AppId: "acme", JsonSchema: acmeSchema, FilterableFields: [],
            ChangeKind: "Partial", EntityIdField: "$.OrderRef",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        Assert.IsInstanceOfType<RegisterEventTypeResult.Success>(result);
        var acmeActive = await service.GetActiveAsync("acme", "IsolationCheck");
        var demoActive = await service.GetActiveAsync("demo", "IsolationCheck");
        Assert.AreEqual(1, acmeActive!.Version);
        Assert.AreEqual(1, demoActive!.Version);
        Assert.AreNotEqual(demoActive.JsonSchema, acmeActive.JsonSchema);
    }

    public static async Task RegisteringAnUpdatedSchemaCreatesNewVersionAndDeactivatesPrevious(SchemaRegistryService service)
    {
        await service.RegisterAsync("OrderUpdated", new RegisterEventTypeRequest(
            AppId: "demo", JsonSchema: OrderPlacedSchema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var v2Schema = """{ "type": "object", "properties": { "Amount": { "type": "number" }, "Status": { "type": "string" } }, "required": ["Amount"] }""";
        var result = await service.RegisterAsync("OrderUpdated", new RegisterEventTypeRequest(
            AppId: "demo", JsonSchema: v2Schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: "event.Amount as Amount, 'Unknown' as Status", DowncastToPrevious: "Amount"));

        Assert.IsInstanceOfType<RegisterEventTypeResult.Success>(result);
        Assert.AreEqual(2, ((RegisterEventTypeResult.Success)result).Version);

        var v1 = await service.GetVersionAsync("demo", "OrderUpdated", 1);
        var active = await service.GetActiveAsync("demo", "OrderUpdated");
        Assert.IsNotNull(v1);
        Assert.IsFalse(v1.IsActive);
        Assert.AreEqual(2, active!.Version);
    }

    public static async Task RegisteringWithoutChangeKindIsRejected(SchemaRegistryService service)
    {
        var result = await service.RegisterAsync("BadType1", new RegisterEventTypeRequest(
            AppId: "demo", JsonSchema: OrderPlacedSchema, FilterableFields: [],
            ChangeKind: null!, EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        Assert.IsInstanceOfType<RegisterEventTypeResult.ValidationFailed>(result);
    }

    public static async Task RegisteringAFilterableFieldNotInSchemaIsRejected(SchemaRegistryService service)
    {
        var result = await service.RegisterAsync("BadType2", new RegisterEventTypeRequest(
            AppId: "demo", JsonSchema: OrderPlacedSchema,
            FilterableFields: [new FilterableFieldRequest("$.DoesNotExist", "String", IsIndexed: false)],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        Assert.IsInstanceOfType<RegisterEventTypeResult.ValidationFailed>(result);
    }

    public static async Task RegisteringXMaskingDirectlyOnObjectTypedPropertyIsRejected(SchemaRegistryService service)
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "Address": { "type": "object", "x-masking": { "strategy": "FixedValue" }, "properties": { "City": { "type": "string" } } }
              }
            }
            """;
        var result = await service.RegisterAsync("BadMasking1", new RegisterEventTypeRequest(
            AppId: "demo", JsonSchema: schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        Assert.IsInstanceOfType<RegisterEventTypeResult.ValidationFailed>(result);
    }

    public static async Task RegisteringAnUnsupportedMaskingStrategyIsRejected(SchemaRegistryService service)
    {
        var schema = """
            { "type": "object", "properties": { "Ssn": { "type": "string", "x-masking": { "strategy": "Bucketing" } } } }
            """;
        var result = await service.RegisterAsync("BadMasking2", new RegisterEventTypeRequest(
            AppId: "demo", JsonSchema: schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        Assert.IsInstanceOfType<RegisterEventTypeResult.ValidationFailed>(result);
    }

    public static async Task RegisteringPartialRevealAndHashStrategiesSucceeds(SchemaRegistryService service)
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "CardNumber": { "type": "string", "x-masking": { "strategy": "PartialReveal", "requiredClaim": "pci:view" } },
                "Ssn": { "type": "string", "x-masking": { "strategy": "Hash", "requiredClaim": "pii:view" } }
              }
            }
            """;
        var result = await service.RegisterAsync("GoodMasking1", new RegisterEventTypeRequest(
            AppId: "demo", JsonSchema: schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        if (result is RegisterEventTypeResult.ValidationFailed vf)
            Assert.Fail("Unexpected validation errors: " + string.Join(" | ", vf.Errors));
        Assert.IsInstanceOfType<RegisterEventTypeResult.Success>(result);
    }

    public static async Task RegulatoryMetadataFieldsAreOptional(SchemaRegistryService service)
    {
        var schema = """
            { "type": "object", "properties": { "Notes": { "type": "string", "x-masking": { "strategy": "FixedValue" } } } }
            """;
        var result = await service.RegisterAsync("GoodMasking2", new RegisterEventTypeRequest(
            AppId: "demo", JsonSchema: schema, FilterableFields: [],
            ChangeKind: "Full", EntityIdField: "$.Id",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        Assert.IsInstanceOfType<RegisterEventTypeResult.Success>(result);
    }

    public static async Task ListingSupportsTopAndSkipPagination(SchemaRegistryService service)
    {
        foreach (var name in new[] { "TypeA", "TypeB", "TypeC" })
            await service.RegisterAsync(name, new RegisterEventTypeRequest(
                AppId: "paging-demo", JsonSchema: OrderPlacedSchema, FilterableFields: [],
                ChangeKind: "Full", EntityIdField: "$.OrderId",
                ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));

        var page1 = await service.ListAsync("paging-demo", top: 2, skip: 0);
        var page2 = await service.ListAsync("paging-demo", top: 2, skip: 2);
        Assert.HasCount(2, page1);
        Assert.HasCount(1, page2);

        var all = await service.ListAsync("paging-demo", top: null, skip: null);
        Assert.HasCount(3, all);
    }
}
