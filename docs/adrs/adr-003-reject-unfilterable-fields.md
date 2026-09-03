[← ADR index](../07-adrs.md)

# ADR-003: Reject filtering on non-indexed/undeclared fields (vs. silent full scan)

Status: Accepted

Context: Filtering must be pushed to the database. Allowing `$filter` on
arbitrary JSON fields would silently degrade to full-scan behavior with a
per-row function call, with no visibility to the caller that this
happened.

Decision: `$filter` may only reference fields declared as
`FilterableField` at schema-registration time. ~~Any other field
reference is rejected with `400 Bad Request` at parse time, before
querying the database.~~ **Superseded by `ADR-037`** (found unmarked by
a design-compliance audit this session — this ADR carried no pointer to
its own supersession at all): the OData `$filter` surface this decision
describes is gone, replaced by a GraphQL query layer. The underlying
rule is unchanged in substance — only fields declared `FilterableField`
at registration can be filtered — but an undeclared field now surfaces
as a GraphQL validation error (`GraphQLException`, rejected before
touching the database), not an HTTP `400`. See `ADR-037`'s own text for
the current mechanism.

Consequences: Callers must know which fields are filterable in advance
(discoverable via the registry, and — pre-`ADR-037` — via AsyncAPI
channel parameter descriptions; now via GraphQL schema introspection).
Prevents silent performance cliffs. Requires the schema registration
workflow to include declaring filterable fields up front, which is an
extra step for whoever registers a schema.
