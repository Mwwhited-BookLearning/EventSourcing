[← ADR index](../07-adrs.md)

# ADR-003: Reject filtering on non-indexed/undeclared fields (vs. silent full scan)

Status: Accepted

Context: Filtering must be pushed to the database. Allowing `$filter` on
arbitrary JSON fields would silently degrade to full-scan behavior with a
per-row function call, with no visibility to the caller that this
happened.

Decision: `$filter` may only reference fields declared as
`FilterableField` at schema-registration time. Any other field reference
is rejected with `400 Bad Request` at parse time, before querying the
database.

Consequences: Callers must know which fields are filterable in advance
(discoverable via the registry and via AsyncAPI channel parameter
descriptions). Prevents silent performance cliffs. Requires the schema
registration workflow to include declaring filterable fields up front,
which is an extra step for whoever registers a schema.
