[← Document index](../README.md)

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
| `IErasureKeyStore` | Where per-entity crypto-shredding keys (DEKs) are wrapped/stored/destroyed | None shipped by default — a deployment registers any mix of Azure Key Vault/AWS KMS/Google Cloud KMS (cloud), self-hosted `HashiCorp Vault` (on-prem), or a local encrypted store (dev) | Keyed by `AppId` — multiple backends can be active **simultaneously** in one deployment, unlike `IUpcastExpressionEvaluator` below | `ADR-057` |
| `IDeviceInputSource` | How the MVVM client reads from a connected physical device | `WebUsbInputSource`, `WebHidInputSource`, `WebSerialInputSource`, `WebBluetoothInputSource`, `NativeBridgeInputSource` (localhost WebSocket, for Firefox/Safari) | Dictated by the device's own hardware interface, not a deployment pick — several active simultaneously | `ADR-070` |
| `IInterchangeFormatAdapter` | Transform between this framework's own JSON Schema shape and an external interchange standard, inbound or outbound | None shipped by default — a deployment registers `Hl7V2Adapter`, `FhirAdapter`, `IchE2bR3Adapter`, `Gs1EpcisAdapter`, or others as needed | Chosen per integration need — several active simultaneously | `ADR-072` |
| `IAttachmentContentStore` | Where a large attachment's actual bytes are stored/tiered | None shipped by default — a deployment registers any mix of Azure Blob (Hot/Cool/Cold/Archive), S3 (Standard/Infrequent-Access/Glacier), or a local dev store | Keyed per `ContentProviderKey`, same multi-backend-simultaneously shape as `IErasureKeyStore` above | `ADR-032` |
| `ITimestampAuthorityClient` | Which RFC 3161 Time Stamping Authority signs a `TimeStampToken` over a submitted hash — a `Signature`'s `ChainHash`-derived hash, or a Lineage Export `ManifestHash` | `HttpTimestampAuthorityClient` (a generic RFC 3161 HTTP client, ships by default; requires `Timestamping:TsaUrl` — no TSA vendor hardcoded) | Selected per deployment; enabled per event type alongside `RequiredSignature`, and for Lineage Export whenever a TSA is configured | `ADR-086` |

**Which package each interface actually ships in**, verified against
`src/` this pass (not repeated per row above, since it cuts across rows
rather than following the table's own per-seam structure): `IMaskingStrategy`,
`IStreamRedactionStrategy`, `IUpcastExpressionEvaluator`, `IErasureKeyStore`,
`IAttachmentContentStore`, and, added by `08-build-plan.md` item 42,
`ITimestampAuthorityClient`, all now ship in one consolidated
`EventStore.Abstractions` package (`ADR-062`, `08-build-plan.md` item 39) —
a hosting team's custom implementation of any of these six references
only that one small assembly, never the framework's own larger internal
projects. `ITimestampAuthorityClient`'s own default implementation
(`HttpTimestampAuthorityClient`, a real RFC 3161 HTTP client using the
BCL's `System.Security.Cryptography.Pkcs` types) ships in a separate new
`EventStore.Timestamping` package instead, the same "interface in
Abstractions, default implementation in its own small package"
split `IErasureKeyStore`/`EventStore.Erasure` already established. `IEventLineageQueryProvider` and `IJsonPathTranslator`
deliberately stay in `EventStore.Persistence` instead — both are
provider-specific, build-time-selected (`ADR-001`), never a hosting-team
extension point the way the five `EventStore.Abstractions` interfaces are,
so consolidating them alongside those five would misrepresent what they're
actually for. `IProjection<TReadModel>` and `IInterchangeFormatAdapter`
similarly stay in their own pre-existing, purpose-built packages
(`EventStore.Projections.Abstractions`, `EventStore.Interchange.Abstractions`
respectively) rather than moving into `EventStore.Abstractions` — each
already had a narrower, more appropriate home before this consolidation,
and moving them would cost their existing dependents a reference change
for no real gain.

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
