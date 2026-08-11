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
        // ADR-087 -- literal text (e.g. plain "v1") is no longer a legal
        // templateContent at all (TranslationKeyValidator rejects it);
        // {{ t:key }} distinguishes the two versions by KEY, never by an
        // embedded literal, the same "reference a translation key, never
        // a hardcoded literal" discipline this item's own exit criteria
        // require of every ViewDefinition template from here on.
        await registry.RegisterAsync(new RegisterViewDefinitionRequest("mvvm-demo-order-2", "Detail", [1], "<div>{{ t:version_1_label }}</div>"));
        await registry.RegisterAsync(new RegisterViewDefinitionRequest("mvvm-demo-order-2", "Detail", [1, 2], "<div>{{ t:version_2_label }}</div>"));

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
        await registry.RegisterAsync(new RegisterViewDefinitionRequest("mvvm-demo-order-3", "Detail", [1], "<div>{{ t:understands_schema_1_only }}</div>"));
        await registry.RegisterAsync(new RegisterViewDefinitionRequest("mvvm-demo-order-3", "Detail", [2], "<div>{{ t:understands_schema_2_only }}</div>"));

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

    // ADR-087's own exit criterion, verified directly: "a rendered string
    // sourced from a hardcoded literal rather than a translation key is
    // confirmed to be rejected/flagged by whatever structural check
    // enforces the requirement."
    public static async Task ATemplateContainingAHardcodedLiteralInsteadOfATranslationKeyIsRejected(ViewDefinitionService registry)
    {
        var hardcoded = (RegisterViewDefinitionResult.ValidationFailed)await registry.RegisterAsync(
            new RegisterViewDefinitionRequest("mvvm-demo-order-5", "Detail", [1], "<div>Carrier: {{carrier}}</div>"));
        Assert.IsTrue(hardcoded.Errors.Any(e => e.Contains("templateContent") && e.Contains("hardcoded text")), string.Join("; ", hardcoded.Errors));

        // The exact same content, with the literal label AND its literal
        // ": " separator both replaced by a translation key (the
        // separator is itself hardcoded text a strict "never a literal"
        // rule correctly also catches -- some locales punctuate a
        // label/value pair differently), registers successfully --
        // proving the rejection above was specifically about the
        // literal content, not some other property of the template.
        var compliant = (RegisterViewDefinitionResult.Success)await registry.RegisterAsync(
            new RegisterViewDefinitionRequest("mvvm-demo-order-5", "Detail", [1], "<div>{{ t:carrier_label_with_separator }}{{carrier}}</div>"));
        Assert.AreEqual(1, compliant.Version);
    }
}
