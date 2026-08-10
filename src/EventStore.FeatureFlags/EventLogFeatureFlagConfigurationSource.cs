using System.Data.Common;
using Microsoft.Extensions.Configuration;

namespace EventStore.FeatureFlags;

// ADR-077 -- addable to IConfigurationBuilder BEFORE WebApplicationBuilder.Build()
// runs, the same "chain one more provider into the pipeline" pattern
// ADR-041's own secrets addendum already established (Key Vault/Vault
// providers alongside the static ones). Since no DI container exists yet
// at that point, this source (and the provider it builds) can't resolve
// EventStoreContext/IJsonPathTranslator the normal, per-request way -- it
// takes a raw ADO.NET connection factory instead, deliberately provider-
// agnostic (no direct Npgsql/SqlClient/Sqlite package reference here) so
// this project stays usable from any of the 3 Hosts. Read-only, one flat
// table, no JSON-typed columns -- EventStoreContext's own mandatory
// IJsonPathTranslator dependency would be pure overhead here.
public class EventLogFeatureFlagConfigurationSource(Func<DbConnection> connectionFactory, string appId, TimeSpan? pollInterval = null) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new EventLogFeatureFlagConfigurationProvider(connectionFactory, appId, pollInterval ?? TimeSpan.FromSeconds(5));
}
