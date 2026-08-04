using EventStore.ViewRegistry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "MVVM Client" (docs/08-build-plan.md, ADR-039)'s
// server-side ViewDefinition registry -- mirrors SchemaRegistryScenario
// Assertions' own "exercise the mechanics directly" pattern. The client
// itself (client-web/) has its own Vitest suite; this file covers only the
// server-side content-addressed registry the client's viewDefinition
// GraphQL query reads from.
internal static class ViewDefinitionScenarioAssertions
{
    public static async Task RegisteringAViewDefinitionMakesItTheActiveOneForItsEntityTypeAndViewKind(ViewDefinitionService registry)
    {
        var result = (RegisterViewDefinitionResult.Success)await registry.RegisterAsync(
            new RegisterViewDefinitionRequest("mvvm-demo-order-1", "Detail", [1], "<div>{{orderId}}</div>"));
        Assert.AreEqual(1, result.Version);

        var active = await registry.GetActiveAsync("mvvm-demo-order-1", "Detail");
        Assert.IsNotNull(active);
        Assert.AreEqual(1, active!.Version);
        Assert.AreEqual(result.Hash, active.Hash);
    }

    public static async Task RegisteringANewVersionDeprecatesThePriorOneButBothStillExist(ViewDefinitionService registry)
    {
        await registry.RegisterAsync(new RegisterViewDefinitionRequest("mvvm-demo-order-2", "Detail", [1], "<div>v1</div>"));
        await registry.RegisterAsync(new RegisterViewDefinitionRequest("mvvm-demo-order-2", "Detail", [1, 2], "<div>v2</div>"));

        var active = await registry.GetActiveAsync("mvvm-demo-order-2", "Detail");
        Assert.IsNotNull(active);
        Assert.AreEqual(2, active!.Version, "the newest registration is the active one");

        // Both versions persist -- never deleted, ADR-038's "deprecated but
        // still served" discipline, reused here.
        var v1Compatible = await registry.GetActiveAsync("mvvm-demo-order-2", "Detail", schemaVersion: 1);
        Assert.IsNotNull(v1Compatible, "version 1 is still a valid fallback target for a client that hasn't seen a schemaVersion-2 event yet");
    }

    public static async Task ASchemaVersionTheActiveTemplateDoesNotDeclareCompatibilityWithFallsBackToTheDeprecatedButCompatibleVersion(ViewDefinitionService registry)
    {
        await registry.RegisterAsync(new RegisterViewDefinitionRequest("mvvm-demo-order-3", "Detail", [1], "<div>v1, understands schema 1 only</div>"));
        await registry.RegisterAsync(new RegisterViewDefinitionRequest("mvvm-demo-order-3", "Detail", [2], "<div>v2, understands schema 2 only</div>"));

        var forOldSchema = await registry.GetActiveAsync("mvvm-demo-order-3", "Detail", schemaVersion: 1);
        Assert.IsNotNull(forOldSchema);
        Assert.AreEqual(1, forOldSchema!.Version, "the active (newest) template doesn't declare schemaVersion 1 compatible -- falls back to the older, still-served one that does");
    }

    public static async Task AnEntityTypeWithNothingRegisteredReturnsNullTheClientsOwnSignalToRenderTheGenericFallback(ViewDefinitionService registry)
    {
        var active = await registry.GetActiveAsync("mvvm-demo-never-registered", "Detail");
        Assert.IsNull(active);
    }

    public static async Task RegistrationValidatesViewKindTemplateContentAndCompatibleSchemaVersions(ViewDefinitionService registry)
    {
        var badViewKind = (RegisterViewDefinitionResult.ValidationFailed)await registry.RegisterAsync(
            new RegisterViewDefinitionRequest("mvvm-demo-order-4", "NotAKind", [1], "<div/>"));
        Assert.IsTrue(badViewKind.Errors.Any(e => e.Contains("viewKind")));

        var emptyTemplate = (RegisterViewDefinitionResult.ValidationFailed)await registry.RegisterAsync(
            new RegisterViewDefinitionRequest("mvvm-demo-order-4", "Detail", [1], ""));
        Assert.IsTrue(emptyTemplate.Errors.Any(e => e.Contains("templateContent")));

        var noVersions = (RegisterViewDefinitionResult.ValidationFailed)await registry.RegisterAsync(
            new RegisterViewDefinitionRequest("mvvm-demo-order-4", "Detail", [], "<div/>"));
        Assert.IsTrue(noVersions.Errors.Any(e => e.Contains("compatibleSchemaVersions")));
    }
}
