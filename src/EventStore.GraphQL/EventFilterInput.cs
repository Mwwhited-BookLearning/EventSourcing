namespace EventStore.GraphQL;

// docs/04-odata-filter-pushdown.md's GraphQL operator table (eq/neq/gt/gte/
// lt/lte/contains), re-expressed as one static, hand-written GraphQL input
// type rather than a dynamically-built per-event-type filter-input object
// (HotChocolate's own [UseFiltering] middleware infers the latter by
// reflecting over a bound C# CLR type, which a fully dynamic ObjectType has
// none of -- runtime-constructing an equivalent dynamic InputObjectType
// whose coerced argument value round-trips correctly through HotChocolate's
// input-coercion pipeline was not verified as reliably buildable within this
// item's own scope). This is a deliberate, honestly-flagged narrowing from
// ADR-037's literal "a client cannot construct a query referencing an
// undeclared field" schema-level guarantee for FILTERING specifically
// (still true for the SUBSCRIPTION FIELD NAME and PAYLOAD FIELDS below,
// which this item's own dynamic type module genuinely builds per registered
// event type) to a runtime check: GraphQlFilterPredicateBuilder rejects an
// undeclared Field name with a GraphQL error before it ever reaches the
// database, the same functional safety ADR-003's original rule required,
// just enforced one step later than schema validation. Flagged in
// 08-build-plan.md, not silently narrowed. Values travel as strings and are
// cast server-side to the field's own declared FilterableFieldType, mirroring
// FilterPredicateBuilder's own BuildConstantExpression exactly.
public record EventFilterInput(string Field, string? Eq, string? Neq, string? Gt, string? Gte, string? Lt, string? Lte, string? Contains);
