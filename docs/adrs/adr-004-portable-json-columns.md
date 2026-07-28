[← ADR index](../07-adrs.md)

# ADR-004: JSON payload/schema columns stored as portable text, not native JSON column types

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
