[← Libraries index](../README.md)

# EF Core (dotnet)

**What it's for:** an ORM — maps C# entity classes to relational tables,
translates LINQ queries to provider-native SQL, and manages
schema migrations, across SQLite/PostgreSQL/SQL Server without
provider-specific data-access code scattered through the app.

**Why bought, not built:** a hand-rolled data-access layer would need to
reimplement change tracking, migration generation, and per-provider SQL
translation — EF Core's actual job — with none of that effort going
toward this project's own subject matter (event sourcing, schema
evolution, entity semantics).

## General usage

```csharp
public class EventStoreContext : DbContext
{
    public DbSet<StoredEvent> Events => Set<StoredEvent>();
    public DbSet<EventTypeDefinition> EventTypes => Set<EventTypeDefinition>();
}

// Per-provider JSON pushdown (04-odata-filter-pushdown.md) uses
// EF Core's translation layer directly:
var query = context.Events.Where(e => EF.Functions.JsonValue(e.Payload, "$.Amount") == "150");
```

## Where this project uses it

Throughout `02-data-model.md`/`docs/data/*.md` and `06-solution-
structure.md` — `EventStoreContext`'s full entity set, plus the
per-provider `IJsonPathTranslator` pushdown mechanism
`04-odata-filter-pushdown.md` describes (still the mechanism underneath
GraphQL resolver arguments post-`ADR-037`). `ADR-001` is the specific
decision to build one provider per deployment rather than switch
providers at runtime.

## Links

- [learn.microsoft.com/ef/core](https://learn.microsoft.com/en-us/ef/core/)
