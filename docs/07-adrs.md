# Architecture Decision Records

## ADR template

```
## ADR-NNN: <title>
Status: Proposed | Accepted | Superseded
Context: <why this decision was needed>
Decision: <what was decided>
Consequences: <trade-offs accepted>
```

---

## ADR-001: Runtime-switched database provider (vs. per-deployment build)

Status: Proposed (needs confirmation)

Context: The store must run on SQLite, PostgreSQL, or SQL Server. Provider
selection could be a compile-time/per-deployment choice or a runtime
config switch.

Decision: Runtime config switch (`Database:Provider` in configuration),
single deployable artifact, with per-provider migrations assemblies
selected at startup based on the same config value.

Consequences: Simpler CI/CD (one artifact). Requires all three migration
histories to be kept in sync manually when the model changes (add a
migration to all three provider projects together). Startup logic must
correctly route to the matching migrations assembly.

---

## ADR-002: On-demand OpenAPI/AsyncAPI generation (vs. materialized cache)

Status: Proposed (needs confirmation)

Context: Spec documents must always reflect current registry state.
Generating on every request is simplest but has a cost; materializing on
registration requires invalidation logic.

Decision: Generate on demand, with a short (~60s) in-memory cache
invalidated on schema registration events. Revisit if event-type count
grows large enough that generation cost becomes measurable.

Consequences: No staleness bugs, minimal cache-invalidation surface.
Slight repeated generation cost under high spec-endpoint traffic — mitigate
with the short-lived cache rather than a full invalidation pipeline.

---

## ADR-003: Reject filtering on non-indexed/undeclared fields (vs. silent full scan)

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

---

## ADR-004: JSON payload/schema columns stored as portable text, not native JSON column types

Status: Accepted

Context: SQL Server's `json` type, Postgres's `jsonb`, and SQLite have
different native JSON storage representations; using any of them at the
EF model level would break provider portability of the shared model and
migrations.

Decision: `Payload` and `JsonSchema` are stored as plain text
(`TEXT`/`nvarchar(max)`/`text`). Native JSON *functions* (`json_extract`,
`->>`, `JSON_VALUE`) are still used at query time via the
`IJsonPathTranslator` abstraction — this is a query-translation concern,
not a column-type concern.

Consequences: No native JSON validation/indexing at the column-type level
from EF Core's perspective; indexing is instead achieved via
provider-specific expression indexes / computed columns applied
out-of-band by the Schema Registry Service (see `02-data-model.md`).
