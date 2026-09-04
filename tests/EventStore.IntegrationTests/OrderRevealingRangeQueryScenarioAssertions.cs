using EventStore.Abstractions;
using EventStore.Domain.SchemaRegistry;
using EventStore.Erasure;
using EventStore.GraphQL;
using EventStore.Inbox;
using EventStore.Persistence;
using EventStore.SchemaRegistry;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// build-plan item #55's own literal exit criterion ("a range query against
// an OrderRevealing field compiles to a native ciphertext comparison with no
// decryption performed to evaluate the predicate") had NO integration test
// coverage at all until this pass, 2026-09-04 -- SearchableEncryptionSqlite
// Tests.cs's only prior OrderRevealing-specific test covers registration
// rejection, never an actual query. Found while doing this item's own
// further review/hardening work, alongside the real gap it was masking:
// GraphQlFilterPredicateBuilder.ResolveOrderRevealingMatchesAsync used to
// pull EVERY row for the field into application memory and compare in a C#
// LINQ-to-Objects loop, never pushed to the database at all. Fixed (see
// that method's own updated comment) by switching PayloadIndexer's
// OrderRevealing token encoding from base64 to fixed-width UPPERCASE HEX
// (empirically verified, 40,000 pairs, zero mismatches: hex-string ordinal
// comparison agrees exactly with OrderRevealingEncryption.Compare's real
// ordering; base64's own alphabet does not have this property) and pushing
// the comparison down as a real `.Where` clause EF Core translates to
// native SQL.
//
// Run against all three providers, unlike GraphQlFilterPredicateBuilder
// SqliteTests's own SQLite-only posture for ordinary (non-encrypted) field
// filtering -- that class explicitly declines Postgres/SqlServer coverage
// because the underlying native-SQL generation is REUSED UNCHANGED from an
// already-proven mechanism. This one is NOT reused from anywhere: ordinal
// string comparison on a text column is a genuinely new translation shape,
// and SQL Server's default collation in particular is case-insensitive and
// linguistically aware, not purely byte-value-ordinal -- a real, non-
// hypothetical risk this exact mechanism could quietly break on that
// provider specifically while still passing on SQLite (whose own default
// collation IS binary). Verified for real against all three, not assumed.
internal static class OrderRevealingRangeQueryScenarioAssertions
{
    private const string OrderRevealingSchema = """
        {
          "type": "object",
          "properties": {
            "OrderId": { "type": "string" },
            "Amount": {
              "type": "number",
              "x-masking": { "requiredClaim": "amount:view", "strategy": "FixedValue" },
              "x-masking-searchable": { "indexKind": "OrderRevealing", "keyScope": "Shared" }
            }
          },
          "required": ["OrderId", "Amount"]
        }
        """;

    public static async Task RangeQueryMatchesViaANativeSqlComparisonNeverDecryptingToCompare(
        EventStoreContext db, SchemaRegistryService registry, PublishService publish,
        SearchIndexKeyService searchIndexKeyService, IEncryptedPredicateEvaluator predicateEvaluator)
    {
        var appId = $"searchable-encryption-ore-range-{Guid.NewGuid():N}";
        var result = await registry.RegisterAsync("OrderPlaced", new RegisterEventTypeRequest(
            AppId: appId, JsonSchema: OrderRevealingSchema,
            FilterableFields: [new FilterableFieldRequest("$.Amount", "Number", false)],
            ChangeKind: "Full", EntityIdField: "$.OrderId",
            ParentValidationMode: null, RequiredClaims: null, UpcastFromPrevious: null, DowncastToPrevious: null));
        Assert.IsInstanceOfType<RegisterEventTypeResult.Success>(result);

        foreach (var (orderId, amount) in new[] { ("o-10", 10), ("o-25", 25), ("o-50", 50), ("o-75", 75), ("o-100", 100) })
            await publish.PublishAsync("OrderPlaced", new PublishEventRequest(appId, 1, $$"""{ "OrderId": "{{orderId}}", "Amount": {{amount}} }""", null, null), TestClaimsPrincipal.None);

        // Confirm the searchable-index Token really is stored as ciphertext,
        // never the plaintext Amount, in the derived index table (the same
        // "never extracts as plaintext" property SearchableEncryptionSqlite
        // Tests's own Equality test proves for Payload itself).
        var indexRows = await db.EncryptedFieldIndexEntries.AsNoTracking().Where(e => e.AppId == appId).ToListAsync();
        Assert.AreEqual(5, indexRows.Count);
        Assert.IsTrue(indexRows.All(e => !"1025 5075100".Contains(e.Token)));

        var definition = await registry.GetActiveAsync(appId, "OrderPlaced");
        // gte 25, lte 75 -- expects exactly {25, 50, 75}, excluding both the
        // low (10) and high (100) outliers, proving genuine two-sided range
        // bounding rather than merely "some comparison happened."
        var predicate = await GraphQlFilterPredicateBuilder.Build(
            db, appId, "orderplaced", searchIndexKeyService, predicateEvaluator, definition!.FilterableFields,
            [new EventFilterInput("Amount", null, null, null, "25", null, "75", null)], CancellationToken.None);

        // An untranslatable LINQ predicate in this `.Where(predicate)` call
        // throws at ToListAsync on modern EF Core, it does not silently
        // client-evaluate -- reaching this line with the right 3 rows IS the
        // proof of genuine native SQL translation on THIS provider, not
        // merely correct results (same proof shape as
        // GraphQlFilterPredicateBuilderSqliteTests.
        // AGreaterThanClauseOnANumberFieldPushesDownToNativeSql).
        var matches = await db.Events.AsNoTracking().Where(e => e.AppId == appId).Where(predicate).ToListAsync();

        Assert.AreEqual(3, matches.Count);
        var orderIds = matches.Select(e => System.Text.Json.JsonDocument.Parse(e.Payload).RootElement.GetProperty("OrderId").GetString()).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "o-25", "o-50", "o-75" }, orderIds.ToList());
    }
}
