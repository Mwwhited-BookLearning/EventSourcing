namespace EventStore.Persistence;

// Placeholder shape, per "Scaffolding & Persistence"'s scope (docs/08-build-plan.md):
// the interface must exist and be unconditionally registered per provider now, so
// there's no second wave of DI wiring once "Follow API + Filter Pushdown" lands --
// but the actual pushdown/index-expression logic belongs to that later item, per
// docs/04-odata-filter-pushdown.md's now-GraphQL-driven mechanism. Do not add real
// logic here until that item's own design is worked out.
public interface IJsonPathTranslator
{
    string TranslateToProviderExpression(string jsonPath);
}
