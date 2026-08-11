using EventStore.FeatureFlags;
using EventStore.Inbox;
using EventStore.Lineage.Api;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// Shared scenarios for "Dynamic Feature-Flag Configuration Provider"
// (docs/08-build-plan.md, ADR-077) -- the write side (FeatureFlagService,
// publishing the reserved FeatureFlagSet event and folding FeatureFlagState
// synchronously, since both live in the SAME process, unlike ADR-067's own
// RBAC events which needed a cross-process Follow fold into DevIdp). The
// polling EventLogFeatureFlagConfigurationProvider itself is provider-
// agnostic ADO.NET, covered once, separately, in
// EventLogFeatureFlagConfigurationProviderTests.cs (Sqlite only).
internal static class FeatureFlagScenarioAssertions
{
    public static async Task SettingAFlagPublishesAHashChainedEventAndFoldsFeatureFlagStateSynchronously(
        EventStoreContext db, FeatureFlagService featureFlags, LineageService lineage)
    {
        const string appId = "feature-flags-demo-1";

        var result = await featureFlags.SetFlagAsync(appId, "new-checkout-flow", "true", TestClaimsPrincipal.None);
        var accepted = (PublishResult.Accepted)result;

        var state = await db.FeatureFlags.SingleAsync(f => f.AppId == appId && f.Key == "new-checkout-flow");
        Assert.AreEqual("true", state.Value);
        Assert.AreEqual(accepted.SequenceNumber, state.LastAppliedSequenceNumber);

        var storedEvent = await db.Events.AsNoTracking().SingleAsync(e => e.EventId == accepted.CorrelationId);
        Assert.AreEqual("featureflagset", storedEvent.EventType);
        Assert.AreEqual(appId, storedEvent.AppId);
        Assert.IsFalse(string.IsNullOrEmpty(storedEvent.ChainHash), "the event is hash-chained like any other business event");
        Assert.IsFalse(string.IsNullOrEmpty(storedEvent.ActorId), "the operator who made the change is recorded, not a side-channel audit table");

        // "queryable through the ordinary Lineage API" -- this item's own
        // exit criterion, not a bespoke read path for this event type.
        Assert.AreEqual(LineageRootCheck.Ok, await lineage.CheckRootAsync(accepted.CorrelationId, TestClaimsPrincipal.None));
    }

    public static async Task TwoAppIdsHoldIndependentValuesForTheSameFlagKey(EventStoreContext db, FeatureFlagService featureFlags)
    {
        const string appIdA = "feature-flags-demo-2a";
        const string appIdB = "feature-flags-demo-2b";

        await featureFlags.SetFlagAsync(appIdA, "shared-key-name", "\"value-a\"", TestClaimsPrincipal.None);
        await featureFlags.SetFlagAsync(appIdB, "shared-key-name", "\"value-b\"", TestClaimsPrincipal.None);

        var stateA = await db.FeatureFlags.SingleAsync(f => f.AppId == appIdA && f.Key == "shared-key-name");
        var stateB = await db.FeatureFlags.SingleAsync(f => f.AppId == appIdB && f.Key == "shared-key-name");
        Assert.AreEqual("\"value-a\"", stateA.Value);
        Assert.AreEqual("\"value-b\"", stateB.Value);
    }

    public static async Task SettingAnExistingFlagAgainOverwritesItsValueAndAdvancesTheWatermark(EventStoreContext db, FeatureFlagService featureFlags)
    {
        const string appId = "feature-flags-demo-3";

        var first = (PublishResult.Accepted)await featureFlags.SetFlagAsync(appId, "rollout-percentage", "10", TestClaimsPrincipal.None);
        var second = (PublishResult.Accepted)await featureFlags.SetFlagAsync(appId, "rollout-percentage", "50", TestClaimsPrincipal.None);
        Assert.IsTrue(second.SequenceNumber > first.SequenceNumber);

        var state = await db.FeatureFlags.SingleAsync(f => f.AppId == appId && f.Key == "rollout-percentage");
        Assert.AreEqual("50", state.Value);
        Assert.AreEqual(second.SequenceNumber, state.LastAppliedSequenceNumber);
    }
}
