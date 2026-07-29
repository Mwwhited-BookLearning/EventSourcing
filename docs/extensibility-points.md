[← Document index](README.md)

# Extensibility Points Reference

A consolidated catalog of every seam this framework lets a hosting/
deployment team customize without forking core code — found scattered
across a dozen ADRs during a generalized-framework review (this session)
and pulled into one page here, per the same reasoning `docs/patterns/
README.md`/`docs/libraries/README.md`/`references.md` already exist as
consolidated catalogs for their own document types.

**Every seam below follows the identical registration model, decided
once in `ADR-059`, not repeated per row:** an interface, one or more
built-in implementations registered in the framework's own composition
root, and a hosting team's custom implementation registered the same way
in *their own* composition root (`ADR-041`, Pure DI). There is no dynamic
plugin discovery anywhere — adding an extension is always "write a
class, add one registration line," never "drop a file in a folder."
Most seams are keyed-registered (.NET keyed DI services,
`docs/patterns/strategy-pattern-extensible-masking.md`) so several
implementations can be active at once, selected by a runtime string
carried in data (a schema's `strategy` field, a channel's config) rather
than one implementation per whole deployment — noted per row where that
distinction matters.

| Interface | What it lets you customize | Built-in implementation(s) | Selection | Governing ADR |
|---|---|---|---|---|
| `IMaskingStrategy` | How a masked field's `masked` content is computed | `FixedValueMaskingStrategy`, `PartialRevealMaskingStrategy`, `HashMaskingStrategy` | Keyed by `x-masking.strategy` string, per field | `ADR-009` |
| `IStreamRedactionStrategy` | How a redacted streaming-channel range is substituted | `ZeroFillStrategy`, `ToneStrategy`, `BlankFrameStrategy` (+ reuses `PartialRevealMaskingStrategy` where content is string-shaped) | Keyed by `RedactedRange.Strategy` string, per range | `ADR-052` |
| `IUpcastExpressionEvaluator` | Which declarative language evaluates an `upcastFromPrevious` expression | CEL (default), JSONata (alternative) | One engine active per whole deployment — not mixed per event type | `ADR-053` |
| `IEventUpcaster` | Custom reshape logic for one event type's schema version step, beyond what the declarative engine expresses | Per registered event type/version, hosting-team-authored | Resolved per `(EventType, SchemaVersion)` at upcast time | `ADR-018` |
| `IProjection<TReadModel>` | A custom CQRS read model, consuming the public Follow API | `OrderSummaryProjection` (worked example only — not part of the core engine, `ADR-030`) | One registration per read model a hosting team wants | `ADR-015` |
| `IEventLineageQueryProvider` | Provider-specific recursive ancestor/descendant traversal SQL | One implementation per supported provider (SQLite/Postgres/SQL Server) | One per `EventStore.Host.<Provider>` build (`ADR-001`) — not runtime-selected | `01-c4-architecture.md`'s Lineage component |
| `IJsonPathTranslator` | Provider-specific `$filter`/query-pushdown translation | One implementation per supported provider | Same as above — build-time, per provider | `04-odata-filter-pushdown.md`, `06-solution-structure.md` |
| `IErasureKeyStore` | Where per-entity crypto-shredding keys (DEKs) are wrapped/stored/destroyed | None shipped by default — a deployment registers Azure Key Vault, AWS KMS, HashiCorp Vault, or a local dev store | One backend active per whole deployment | `ADR-057` |

**Not a seam, for contrast**: `IPayloadMasker` itself (`ADR-009`) is the
one framework-owned orchestrator that *consumes* `IMaskingStrategy` — a
hosting team doesn't reimplement it, only the strategies it dispatches
to. Listed in `06-solution-structure.md`'s code sketch, not above, since
there's nothing to extend there.

## Where a new seam gets added

Found a genuine new extension point while writing an ADR/pattern doc?
Add a row here in the same pass, matching the discipline
`docs/10-open-questions.md` already states for itself — don't let a new
seam live only as a buried interface definition in an ADR's Decision
section.
