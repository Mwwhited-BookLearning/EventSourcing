using System.Text.Json.Nodes;
using EventStore.Router;
using FsCheck;
using FsCheck.Fluent;

namespace EventStore.UnitTests;

// ADR-063 -- the "pure-logic half of ADR-024's conflict-resolution
// policy," checked in-memory against the fold function's own merge
// primitive directly (EntityDataMerger.MergePatch), not through
// RouterWorker.FoldAsync's full EF-Core-dependent machinery -- that
// integration-level behavior is already covered by EventStore.
// IntegrationTests' own EntityScenarioAssertions; this is the narrower,
// cheaper, generator-driven check ADR-063's own Decision names.
[TestClass]
public class ConflictResolutionPropertyTests
{
    // ADR-024's own Decision, verbatim: "two patches based on the same
    // version touching DIFFERENT properties both fold cleanly regardless
    // of arrival order." At the pure-merge level that's exactly order-
    // independence for two patches touching disjoint property sets.
    [TestMethod]
    public void TwoPatchesTouchingDisjointPropertiesMergeToTheSameResultRegardlessOfOrder()
    {
        Prop.ForAll(
            Arb.From(DisjointPatchPairGenerator()),
            pair => MergesIdenticallyEitherOrder(pair.Current, pair.PatchA, pair.PatchB))
            .QuickCheckThrowOnFailure();
    }

    // Stream-order last-write-wins (ADR-024's own default policy): when
    // two patches DO touch the same property, whichever one is applied
    // SECOND wins for that property -- order matters precisely when
    // (and only when) the property sets overlap.
    [TestMethod]
    public void TwoPatchesTouchingTheSamePropertyResolveToWhicheverWasAppliedSecond()
    {
        Prop.ForAll(
            Arb.From(OverlappingPatchPairGenerator()),
            pair => SecondPatchWinsTheSharedProperty(pair.Current, pair.PatchA, pair.PatchB, pair.SharedKey))
            .QuickCheckThrowOnFailure();
    }

    // Applying the identical patch twice must be indistinguishable from
    // applying it once -- a real property this design's own idempotent-
    // retry posture (ADR-011) depends on at the merge level, not just at
    // the publish-deduplication level.
    [TestMethod]
    public void ApplyingTheSamePatchTwiceIsTheSameAsApplyingItOnce()
    {
        Prop.ForAll(
            Arb.From(SinglePatchGenerator()),
            pair => AppliedTwiceEqualsAppliedOnce(pair.Current, pair.Patch))
            .QuickCheckThrowOnFailure();
    }

    private static bool MergesIdenticallyEitherOrder(JsonObject current, JsonObject patchA, JsonObject patchB)
    {
        var abThenOrder = EntityDataMerger.MergePatch(EntityDataMerger.MergePatch(current, patchA), patchB);
        var baThenOrder = EntityDataMerger.MergePatch(EntityDataMerger.MergePatch(current, patchB), patchA);
        return JsonObjectsAreSemanticallyEqual(abThenOrder, baThenOrder);
    }

    // A plain `.ToJsonString()` comparison is too strict for this
    // property: MergePatch preserves each call's own key-INSERTION
    // order, so applying two disjoint patches in different orders can
    // produce genuinely different key orderings for an identical set of
    // key-value pairs -- a real false failure found only by running this
    // (the property IS true at the semantic level; the first version of
    // this test's own comparison method was what was wrong, not
    // EntityDataMerger). Sufficient for these flat, single-level test
    // objects, whose every value is a plain JsonValue leaf.
    private static bool JsonObjectsAreSemanticallyEqual(JsonObject a, JsonObject b) =>
        a.Count == b.Count && a.All(kv => b.TryGetPropertyValue(kv.Key, out var other) && kv.Value?.ToJsonString() == other?.ToJsonString());

    private static bool SecondPatchWinsTheSharedProperty(JsonObject current, JsonObject patchA, JsonObject patchB, string sharedKey)
    {
        var aThenB = EntityDataMerger.MergePatch(EntityDataMerger.MergePatch(current, patchA), patchB);
        return aThenB[sharedKey]?.ToJsonString() == patchB[sharedKey]?.ToJsonString();
    }

    private static bool AppliedTwiceEqualsAppliedOnce(JsonObject current, JsonObject patch)
    {
        var once = EntityDataMerger.MergePatch(current, patch);
        var twice = EntityDataMerger.MergePatch(EntityDataMerger.MergePatch(current, patch), patch);
        return JsonObjectsAreSemanticallyEqual(once, twice);
    }

    private static readonly string[] PropertyNames = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static JsonObject RandomBaseObject(Random random) =>
        new JsonObject(PropertyNames
            .Where(_ => random.Next(2) == 0)
            .Select(name => new KeyValuePair<string, JsonNode?>(name, JsonValue.Create($"base-{name}-{random.Next(1000)}"))));

    private static Gen<(JsonObject Current, JsonObject PatchA, JsonObject PatchB)> DisjointPatchPairGenerator() =>
        Gen.Fresh(() =>
        {
            var random = new Random();
            var current = RandomBaseObject(random);
            var shuffled = PropertyNames.OrderBy(_ => random.Next()).ToArray();
            var splitPoint = 1 + random.Next(shuffled.Length - 1); // at least one key on each side
            var keysForA = shuffled[..splitPoint];
            var keysForB = shuffled[splitPoint..];
            var patchA = new JsonObject(keysForA.Select(k => new KeyValuePair<string, JsonNode?>(k, JsonValue.Create($"A-{k}-{random.Next(1000)}"))));
            var patchB = new JsonObject(keysForB.Select(k => new KeyValuePair<string, JsonNode?>(k, JsonValue.Create($"B-{k}-{random.Next(1000)}"))));
            return (current, patchA, patchB);
        });

    private static Gen<(JsonObject Current, JsonObject PatchA, JsonObject PatchB, string SharedKey)> OverlappingPatchPairGenerator() =>
        Gen.Fresh(() =>
        {
            var random = new Random();
            var current = RandomBaseObject(random);
            var sharedKey = PropertyNames[random.Next(PropertyNames.Length)];
            var patchA = new JsonObject { [sharedKey] = JsonValue.Create($"A-value-{random.Next(1000)}") };
            var patchB = new JsonObject { [sharedKey] = JsonValue.Create($"B-value-{random.Next(1000)}") };
            return (current, patchA, patchB, sharedKey);
        });

    private static Gen<(JsonObject Current, JsonObject Patch)> SinglePatchGenerator() =>
        Gen.Fresh(() =>
        {
            var random = new Random();
            var current = RandomBaseObject(random);
            var patch = new JsonObject(PropertyNames
                .Where(_ => random.Next(2) == 0)
                .Select(name => new KeyValuePair<string, JsonNode?>(name, JsonValue.Create($"patch-{name}-{random.Next(1000)}"))));
            return (current, patch);
        });
}
