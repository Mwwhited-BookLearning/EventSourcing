# CLAUDE.md

This repo is a **design package**, not an implemented codebase — there is no
`src/` yet. Every file under `docs/` is architecture/decision documentation
for an event-sourcing store, meant to be built from later. Treat doc edits
with the same care as code edits: internal consistency across files matters
more here than almost anything else, because there's no compiler to catch
drift.

Note: the folder/repo name itself is a typo (`EventSouring` → should be
`EventSourcing`). Known, deliberately not yet fixed — renaming the directory
this session's tools are actively running against was judged too risky
mid-task. Confirm with the user before doing it, and do it as an isolated
step (rename, then verify the shell's cwd survived) — don't fold it into a
larger batch of unrelated changes.

## Layout

- `README.md` — entry point: what the system is, doc index, open decisions.
- `docs/01`–`09` — the core design, read in that order (C4 architecture →
  data model → API contracts → OData pushdown → schema registry → solution
  structure → ADRs → build plan → CQRS read side). `04-odata-filter-
  pushdown.md` is partway through being superseded — see Integration status.
- `docs/02-data-model.md` — **classification overview + index only**, same
  split as the two below. The entity classes themselves live one group per
  file under `docs/data/` (`schema-registry.md`, `event-log.md`,
  `entity-store.md`, `dbcontext-and-conventions.md`). Never write an entity
  class back into `02-data-model.md` — add it to the right group file and
  update the classification table/diagram if it's a new group.
- `docs/10-open-questions.md` — a live tracker for genuinely unresolved
  questions, distinct from every other document type: an ADR is a
  decision already made, a comparison weighs a fork before deciding,
  this file is for a fork not yet weighed at all or a decision left
  deliberately partial. **Add a row here in the same pass you find one**
  while writing any other doc — don't let it live only as a buried
  sentence in an ADR's Consequences section. Move a row to a real ADR
  (or delete it) once resolved; this file should only ever hold things
  still actually open.
- `docs/07-adrs.md` — **template + index only.** The ADRs themselves live
  one per file under `docs/adrs/adr-NNN-slug.md`. Never write ADR content
  back into `07-adrs.md` — add a row to its index table and create the file
  under `adrs/`.
- `docs/patterns/` — a **pattern reference**, distinct from an ADR (a
  specific decision) and from `references.md` (a bare bibliography line):
  explains the general pattern first (cited), then points to the ADR(s)
  that apply it here. **Keep this folder in sync as new patterns/practices
  get discovered or decided — this is a standing instruction, not a
  one-time task.** At minimum, add a row to `patterns/README.md`'s catalog
  the same turn a new pattern gets decided; write the full standalone doc
  (with a PlantUML/Salt diagram, whichever fits) when there's time to do it
  properly. Falling behind on this folder has already happened once —
  don't let it happen again.
- `docs/features/*.md` — one doc per feature: context, PlantUML
  sequence/ER diagrams, Gherkin scenarios. Extracted into real `.feature`
  files only once implementation starts (see `06-solution-structure.md`).
- `docs/references.md` — bibliography. Two sections: standards actually
  **adopted** (with which ADR/doc uses them), and standards **considered
  and rejected**, each with the specific reason. Every new ADR that leans on
  a real-world spec/library should get an entry here, and every rejection
  should be as explicit as an adoption — don't let something get evaluated
  and then silently disappear. **A rejection can later flip to adopted** if
  a new requirement removes the original reason for rejecting it — this has
  already happened once (content-addressable storage: rejected for lack of
  a large-object use case, re-adopted once `ADR-032`'s binary attachments
  created one). Check `references.md` for a stale rejection before assuming
  something is still out of scope.
- `docs/comparisons/` — a **fourth kind of document**: when a real fork
  has genuine options on more than one side, write both (or all) sides
  out in full — pros, cons — *before* the deciding ADR, not after. Write
  one of these whenever a multi-option decision comes up, then have the
  ADR reference it rather than re-deriving the comparison inline.
- `docs/patterns/interactions/` — for when two patterns don't just both
  apply somewhere, but genuinely *compose* at one specific point (e.g.
  two different checks running in the same fold step) — gets its own page
  explaining the combination, linked from both patterns' own docs.
- `docs/libraries/{platform}/{library}.md` — a **fifth kind of document**:
  one file per adopted (or seriously considered) off-the-shelf
  framework/library, grouped by platform folder (`dotnet/`, `web/`, ...).
  What it's for, plus general usage examples — not a copy of that
  library's own docs, just enough to orient a reader and show the shape
  of how this design calls it. Referenced from whichever ADR/pattern doc
  actually adopts it, rather than repeating usage examples inline there.
  See `docs/libraries/README.md` for the catalog and the buy-over-build
  principle this folder exists to support.
- `docs/domains/{domain}.md` — a **sixth kind of document**: one
  reference doc per real-world domain considered as a proving-ground
  worked example — applicable ADRs (with a one-line reason each),
  governing regulations/standards, and special concerns (a real
  tension, a weak spot, a standout fit). Generated from, not a repeat
  of, `docs/comparisons/proving-ground-domain.md`'s coverage matrix and
  regulatory mapping table — that comparison is where the *choice*
  happened; these files are the per-domain reference for afterward.
  `docs/domains/README.md` is the catalog.
- `docs/design-docs/` — **removed.** Was a second, independently-developed
  design (a distributed, entity-centric event-sourced platform), imported
  purely as a reference for this integration; fully absorbed into ADRs
  `021`–`039` (see "Integration status" below), then deleted once that
  absorption was confirmed complete — nothing depends on the folder still
  existing. A `docs/design-docs/NN §X.Y`-style citation surviving in an
  ADR/pattern/comparison doc is a provenance pointer to that now-deleted
  source, not a live link — leave those as historical attribution rather
  than scrubbing them, but don't add new ones.

## Conventions established so far

- **No external `!include` in any PlantUML diagram, ever — hand-style C4
  notation in plain PlantUML instead.** `C4-PlantUML` (both the remote
  `raw.githubusercontent.com` form and the bundled `<C4/...>` stdlib
  form) fails silently — a blank or broken diagram, no readable error —
  in any renderer without live internet access or that exact stdlib path
  configured, which is most local/offline PlantUML setups actually used
  against this repo. This happened repeatedly, not once, across
  `01-c4-architecture.md` and two pattern docs
  (`cqrs-and-materialized-views.md`, `mvvm-client-architecture.md`) — all
  three now use plain `rectangle`/`database` elements with `<<stereotype>>`
  tags and a `skinparam` color tier per level (Person/System/Container/
  Component, dashed boundary boxes) instead, fully self-contained. Applies
  to every PlantUML diagram in this repo going forward, not just C4 ones —
  never reach for an external library `!include` of any kind; style it by
  hand. See `references.md`'s Reference-only entry for `C4-PlantUML` for
  the full reasoning.
- **ADRs are additive history, not editable state.** When a later decision
  changes an earlier one, don't delete the old text — strike it through
  (`~~...~~`) and add "Superseded by `ADR-XXX`" (see `ADR-006`'s
  `access_token` workaround for the pattern). If the earlier ADR was written
  *this same integration effort* and never shipped/built, a clean rewrite in
  place is fine instead (see `ADR-018`'s upcast mechanism, revised in place
  before anything depended on the original version — and `ADR-031`, revised
  in place from "telemetry channels" to "streaming channels" once
  audio/video turned out to be the same mechanism, before anything
  downstream depended on the narrower framing).
- **Never hardcode a future ADR's number in a propagated doc.** Write "the
  queued replication ADR" / "(queued sharding ADR)", not "`ADR-027`", for
  anything not yet actually written under `docs/adrs/`. ADR numbers are
  assigned by write order, and something has jumped the not-yet-written
  queue *repeatedly* this session (once when API-docs-UI/Aspire+OTel landed
  ahead of the design-docs merge queue, again when upcast-materialization/
  downcast/logical-fold/multi-tenancy/streaming-channels/attachments did) —
  every hardcoded forward reference written before that churn had to be
  found and fixed. Don't add more of them. Backfill the real number into
  cross-referencing docs only once an ADR is actually written.
- **Always search for prior art before designing anything new** — RFCs,
  standards, or just a commonly-named practice — even when the request
  doesn't name one. Stated explicitly, twice, this session. Search
  (WebFetch/WebSearch) *before* writing an ADR or pattern doc, not after;
  cite the real thing if one fits, state honestly if nothing does.
  `ADR-040`'s ticket-exchange mechanism is the clearest example: searching
  turned up CAS service tickets, RFC 7662 introspection, and CDN
  signed-URL conventions, none of which the request itself named.
- **Verify a spec before citing it.** Every RFC/standard number cited in an
  ADR was confirmed against the real spec (WebFetch the datatracker/spec
  page) before being written down, not recalled from memory and assumed
  correct — this includes pattern names in `docs/patterns/` (Idempotent
  Receiver, Materialized View, Optimistic Offline Lock, Tolerant Reader,
  Watermarks/event-time, Media Fragments URI, HTTP Range Requests, MVVM,
  MVP, MVC, CAS, and RFC 7662/Background Sync were all confirmed this way,
  not recalled).
- **Never invent a bespoke mechanism when a real standard already fits.**
  Check whether an existing RFC/spec/library already solves it, adopt it if
  the fit is genuine, and explicitly record in `references.md` why *not* if
  it was considered and rejected.
- **Say when something is only partially borrowed**, and disambiguate
  terminology collisions explicitly rather than hoping context makes it
  clear (examples: "query parameter" vs. the HTTP `QUERY` method, `ADR-010`;
  "projection" meaning a CQRS read model vs. design-docs' schema-mapping
  sense, `ADR-018`).
- **A repeated relationship gets its own envelope-metadata field, never
  conflated with an existing one just because the shape looks similar.**
  This design now has six: `parentEventIds` (causal derivation, `ADR-005`),
  `MaterializationOfEventId` (reshaped copy of, `ADR-027`),
  `TelemetryPointer` (position in a signal/media stream, `ADR-031`),
  `AttachmentRef` (supporting binary content, `ADR-032`), `erasureScope`
  (whose crypto-shredding key protects this field, when it's not the
  event's own `EntityId`, `ADR-057`), and `Signature` (a captured
  sign-off attestation — signer/time/meaning/`acr`, `ADR-066`; a milder
  fit than the other five, since it's an audit record rather than a
  pointer to another event/entity, worth a human gut-check if a seventh
  ever comes up). If a seventh comes up, ask what question it
  specifically answers before reusing one of these six.
- **Prefer buy over build.** For a complex pattern or task, check for an
  existing, well-adopted framework/library before designing a bespoke
  mechanism — the same instinct as "never invent a bespoke mechanism when
  a real standard already fits," extended from specs/RFCs to concrete
  libraries. When one fits, adopt it and give it a
  `docs/libraries/{platform}/{library}.md` writeup. When none fits (or
  the gap is genuinely this project's own business logic), build a small,
  generalized library isolated from business logic — not scattered
  through it — and it earns the same writeup once it exists. `ADR-037`'s
  GraphQL Gateway and `ADR-032`'s WebDAV surface had both gone unnamed at
  the concrete-library level until this pass — `docs/libraries/dotnet/
  hotchocolate.md` and `docs/libraries/dotnet/nwebdav.md` closed exactly
  that gap, found while doing this buy-over-build pass, not requested by
  name.
- **A new capability gets a Phase in `08-build-plan.md`**, with real
  dependencies and concrete exit criteria tied to a feature doc's Gherkin
  scenarios — not just an ADR with no build-plan entry. (Build-plan
  propagation for everything past `ADR-024` is currently behind — see
  Integration status.)

## Standing requirements, now attached to a written ADR

- **Fault/abend/restart-tolerant outbox** — durable and resumable across
  an unclean process termination, not merely safe under a graceful
  shutdown. Addressed in `ADR-033` (peer-sync outbox/inbox) and referenced
  by `ADR-039` (the MVVM client's local outbox reuses the same durable
  primitive). If a *third* outbox-shaped mechanism ever gets introduced,
  re-check that it actually inherits this, don't assume it does by family
  resemblance alone.
- **Never lose or corrupt data** — the governing principle stated in
  `README.md`'s opening section. Weigh any future durability trade-off
  against this explicitly (see `ADR-031`'s streaming-channel durability
  bar for the one accepted, narrow exception, stated as such rather than
  a silent default) rather than defaulting toward convenience.

## Integration status (`docs/design-docs/`, now removed, → primary design)

**Every ADR the design-docs merge implied now exists and is Accepted —
`ADR-021` through `ADR-039`.** Nothing from that merge remains only a
plan; what remains is propagating those decisions into the surrounding
docs that still describe the pre-integration shape (see below). Big,
foundational, locked-in decisions, for quick orientation:

- **Full entity-centric rebuild** — `EntityId`, an always-on Entity Store,
  `ExpectedVersion` (`ADR-021`), refined by `Optional<T>` property-level
  patches (`ADR-022`), a persist-everything ingestion posture (`ADR-023`,
  superseding parts of `ADR-011`/`013`/`020`), and optimistic concurrency
  + logical-order fold (`ADR-024`, `ADR-029` — see
  `docs/patterns/interactions/fold-ordering-and-conflict.md` for how the
  two compose).
- **Multi-tenant framework** — `appId` is a real schema-registry scoping
  key (`ADR-030`); the `Orders` walkthrough is a sample application, not
  part of the core engine.
- **Schema evolution went further than design-docs asked for** —
  upcasting is materialized to the log, not just computed live
  (`ADR-018`, `ADR-027`), downcasting exists for legacy consumers
  (`ADR-028`), and compatibility/deployment discipline is explicit
  (`ADR-038`).
- **Two new data planes, deliberately separate from the event log** —
  streaming channels for telemetry/audio/video (`ADR-031`, with
  standard-protocol playback/deep-linking/redaction) and content-addressed
  binary attachments, browsable via WebDAV (`ADR-032`).
- **Distribution is real** — gossip-topology replication with a minimum
  2-replica, regional-fault-tolerant requirement (`ADR-033`,
  `docs/comparisons/peer-sync-topology.md`) and entity-type-based sharding
  (`ADR-034`, `docs/comparisons/sharding-strategy.md`).
- **Non-authoritative capture** — `AuthorityStatus` as its own trust axis
  (`ADR-035`, `docs/comparisons/authority-rejection-behavior.md`), DID/UCAN
  self-attestation via OAuth Token Exchange (`ADR-036`, un-rejecting what
  `references.md` had marked reference-only).
- **GraphQL replaces OData entirely** (`ADR-037`) — not "primary/
  secondary." GraphQL queries travel over HTTP `QUERY`, never `GET`,
  specifically to keep PII/PHI-bearing filter arguments out of URLs/
  logs/proxy caches. Supersedes `ADR-003`/`04-odata-filter-pushdown.md`'s
  OData surface (the per-provider JSON pushdown mechanism survives; only
  the OData syntax goes away) and moves `ADR-018`'s upcast mechanism off
  OData `compute()` onto JS/CEL + GraphQL SDL directives.
- **MVVM client + entity views** (`ADR-039`) — sequenced last on purpose,
  the least load-bearing piece; composes primitives every earlier ADR
  already built rather than introducing new server-side mechanism. Now
  extended with: a concrete Vue 3 implementation mapping (`docs/patterns/
  mvvm-client-architecture.md`), an installable/offline PWA story with a
  Background-Sync-flushed outbox (`docs/patterns/pwa-offline-outbox.md`),
  multi-instance support (one window per entity stream, no shared outbox
  state across instances), and an explicit MVVM→MVP→MVC→code-behind
  fallback priority (`docs/comparisons/ui-architecture-patterns.md`) for
  UI technologies this ADR doesn't fully dictate.
- **Ticket exchange for header-incapable clients** (`ADR-040`) — closes
  the gap `ADR-031` (streaming playback) and `ADR-032` (WebDAV/attachment
  retrieval) reopened: a `<video src>`/WebDAV client can't set an
  `Authorization` header, the same problem `ADR-006`'s original
  `access_token`-in-URL workaround solved and `ADR-012` correctly removed.
  Solved via a short-lived, single-use, opaque ticket (RFC 8693 issuance,
  RFC 7662-shaped resolution) plus a client-side HMAC signature — not by
  repeating the removed workaround.
- **Explicit composition over convention-magic** (`ADR-041`) — constructor
  injection and a manual composition root (Pure DI) over assembly-scanning
  auto-registration; `Microsoft.Extensions.Logging`/`Configuration`/`DependencyInjection`
  (first-party) kept, but no AutoMapper, no third-party structured-logging
  framework, and `System.Text.Json` over `Newtonsoft.Json`. No rework
  elsewhere — nothing previously accepted conflicted with this.
- **Gated authoritative publish** (`ADR-042`, revises `ADR-035`) — the
  Entity Store now only folds an event once `AuthorityStatus` reaches
  `accepted`; a new `LiveEntityStoreRow` folds everything immediately
  instead, wrapped `isAuthoritative: false` at the query surface.
  Composes Write-Audit-Publish + the Quarantine pattern (deliberately
  inverted — visible-but-labeled, not blocked) + a second CQRS
  materialized view; see `docs/patterns/interactions/
  gated-authoritative-publish.md`. `AuthorityStatus`'s default also
  flipped from `unattested` to `accepted` for ordinary authenticated
  publishes — it only starts below `accepted` when a publish declares a
  reason not to trust it yet (self-attestation, or an explicit
  review-pending marker — covering both the "unauthorized submitter"
  and "unvalidated detector output" trigger cases named this session).
- **Delegated access grants + application-defined permissions**
  (`ADR-043`, `ADR-044`) — reuse `ADR-036`'s UCAN delegation for
  "secondary opinion"-style temporary, capped, entity-scoped access
  grants (explicitly disambiguated from the classical Four Eyes/
  two-person rule, which is a different mechanism), and resolve the one
  thing the UCAN spec itself leaves out-of-band — which DID is a root of
  trust for a capability namespace — via a new per-`AppId` `AppTrustRoot`
  registry. No new cryptographic mechanism either time.
- **Read access audit log** (`ADR-045`) — resolves the open question
  `ADR-043` raised: every read, through every surface, now writes an
  `AccessLogEntry` (`docs/data/access-log.md`, a sixth, independent
  append-only store) recording the reader's identity and whether their
  credential is `Authoritative` or `Attested`, hash-chained via
  `ADR-019`'s primitive applied to a second, independent chain.
- **RBAC + row-level access + federated claims augmentation**
  (`ADR-046`, `ADR-047`, plus a generalization inside `ADR-043`) —
  `Role`/`UserPermission` (`docs/data/schema-registry.md`) resolve to a
  flattened claim set at token issuance (ANSI/INCITS 359's base tier;
  hierarchy and separation-of-duty explicitly not adopted); direct
  user-level permissions are additive-only, never restrictive, by
  design (no explicit-deny concept anywhere in this model); `ADR-043`'s
  entity-scope claim generalized from "a delegated-grant feature" to a
  standing Row-Level-Security-shaped primitive usable by any claim
  source; `ADR-047` lets an external, already-authoritative IdP's token
  be augmented (never replaced) with this framework's own
  application-specific claims, via `ADR-036`'s Token Exchange machinery
  a third time. `ADR-032` also amended in place (not built/shipped, a
  natural extension) to support standalone attachments with a direct
  `RequiredReadClaim`/`RequiredPublishClaim`, independent of any linked
  event.
- **SPIFFE/SPIRE for internal service/peer identity** (`ADR-048`) —
  reverses `references.md`'s prior rejection of SPIFFE/SPIRE, once this
  design's own growth (many internal services in `06-solution-
  structure.md`, `ADR-033`'s cross-site peer servers) created the
  multi-workload-mesh scenario that rejection itself named as the
  reason to revisit. Answers a different question from `ADR-006`
  (workload identity, not user/external-client identity) — composes
  with it, doesn't replace it. See `docs/comparisons/service-
  identity.md` for the full comparison against static OAuth2 client
  credentials and hand-rolled mTLS.
- **Entity-level permission/masking metadata, surfaced via OpenAPI/
  AsyncAPI extensions, reused for log redaction** (`ADR-050`) —
  generalizes `ADR-008`'s one-claim-per-direction limit to a list
  (`RequiredClaims`); guarantees `x-masking`/`x-required-claims` survive
  into generated docs as real Specification Extensions (both specs
  formally define `x-*`, not invented); adopts `Microsoft.Extensions.
  Compliance.Redaction` (first-party, `ADR-041`-consistent) to keep
  PII/PHI/PCI out of logs, reusing `ADR-009`'s existing classification
  metadata rather than a second taxonomy. Two honest residual risks
  raised, not resolved: AND/OR semantics for multiple same-direction
  claims, and whether publicly-readable spec docs exposing *which*
  fields are sensitive is itself a leak (**resolved**: not a meaningful
  leak by default; `ADR-002` adds a config toggle to disable the spec
  endpoints entirely for stricter deployments).
- **Four more open questions resolved** (`ADR-051`–`053`, plus a
  decision folded into `ADR-050`): peer discovery is explicit static
  `SeedPeers` configuration only, no automatic discovery of any kind
  (`ADR-051`); streaming-channel redaction is read-time with a
  zero-fill/tone/blank-frame default per `ContentKind`, plus a
  configurable `PartialReveal` strategy shared with `ADR-009`
  (`ADR-052`); the CEL-vs-JSONata upcast-language tension resolves by
  making the engine pluggable behind `IUpcastExpressionEvaluator`, CEL
  the default (`ADR-053`); `RequiredClaims`' same-direction evaluation
  defaults to `OR`, with `AND`/richer combinations named as a plausible
  future extension, not built now. `ADR-009`'s `PartialReveal` uses
  named fields (`showFirst`/`showLast`/`maskChar`/`preserveSeparators`),
  deliberately not a cryptic mask-template string, after a readability
  request — modeled on PCI-DSS's own plain-language PAN-masking framing
  over the more cryptic (but real) `MaskedTextProvider` code-table
  convention. A third strategy, `"Hash"`, is now also decided and built
  (`ADR-009`) — a keyed HMAC via `Microsoft.Extensions.Compliance.
  Redaction`'s `HmacRedactor` (`ADR-050`), not a bare hash, specifically
  to avoid small-value-space reversal (a bare SHA-256 of a 9-digit SSN is
  brute-forceable). Format-preserving encryption, generalization/
  bucketing, and tokenization are now explicitly **declined** (not merely
  left open) in `docs/comparisons/masking-strategies.md`, applying
  KISS — each would add real surface (new key management, a fourth
  strategy, or a whole second component) for a requirement nobody has
  stated; `docs/10-open-questions.md`'s masking-strategies row is removed
  accordingly.
- **Masking/redaction content strategies are an explicit Strategy-pattern
  seam, per direct request** — `ADR-009`'s `IMaskingStrategy` (one class
  per strategy, keyed-registered via .NET's built-in keyed DI services,
  `ADR-041`'s composition root) means a future strategy is a new class
  plus one registration line, never a change to `IPayloadMasker`'s own
  code. `ADR-052`'s streaming redaction reuses the identical shape via a
  sibling `IStreamRedactionStrategy` (byte/frame-shaped content, not
  `JsonNode`) rather than a duplicated mechanism. Written up in
  `docs/patterns/strategy-pattern-extensible-masking.md` and
  `06-solution-structure.md`'s `IPayloadMasker` section; the one
  deliberate, named exception to `ADR-041`'s "no service-locator lookups"
  rule, since which strategy applies is a runtime fact carried in schema
  data, not a compile-time constructor parameter.
- **API Gateway (YARP)** (`ADR-049`) — also reverses a prior rejection,
  same trigger as `ADR-048`: this design's growth to multiple
  independently-addressable external surfaces (GraphQL, WebDAV,
  streaming, ticket/OAuth) is exactly the scenario the original "each
  host is a single deployable" rejection named as the reason to
  revisit. External auth/TLS terminates at the gateway; internal
  gateway-to-service calls hand off to `ADR-048`'s SPIFFE identity.

Also landed, independent of the entity/persist-everything direction
(tooling/infrastructure, decided alongside the merge but not part of it):
`ADR-025` (Scalar for OpenAPI docs UI, `@asyncapi/react-component` for
AsyncAPI) · `ADR-026` (dev via .NET Aspire + full OpenTelemetry — logging,
tracing, metrics; prod via Docker Compose).

Also written this pass, expanding the pattern/comparison catalogs beyond
what any single ADR strictly required, per direct request: [`docs/
comparisons/api-query-layer.md`](docs/comparisons/api-query-layer.md)
(GraphQL vs. OData vs. JSON:API vs. gRPC vs. REST-ad-hoc/PostgREST-style,
reaffirming `ADR-037`) and its two companion pattern docs
(`docs/patterns/graphql-query-language.md`,
`docs/patterns/odata-query-protocol.md`).

- **Client SDK generation** (`ADR-054`) — resolves a "generalized
  framework review" finding (this session) that nothing recommended a
  codegen story for consumers despite both exposed contracts supporting
  it. `Kiota` (Microsoft, first-party) generates the OpenAPI-side client
  for both .NET and TypeScript from one spec; `Strawberry Shake`
  (ChilliCream, same vendor as server-side `HotChocolate`) and `GraphQL
  Code Generator` (the TypeScript-ecosystem standard) split the
  GraphQL-side client by language, since that ecosystem doesn't have one
  tool covering both the way Kiota does. Same review surfaced five more
  genuine gaps — per-tenant rate limiting/quota, data retention/backup/
  DR, GDPR-style erasure, a consolidated extensibility-points reference,
  and a chaos/property-based testing strategy — tracked as real open
  questions in `docs/10-open-questions.md`, not yet resolved.

Those five remaining findings are now all resolved too (`ADR-055`
through `ADR-060`), per direct follow-up direction:
- **Testing strategy** (`ADR-055`) — `MSTest`+`Moq` (backend unit,
  `Moq`'s 2023 `SponsorLink` trust-damage caveat recorded honestly, not
  silently dropped), `Vitest`+`Vue Test Utils` (frontend unit),
  `Testcontainers` (service-level integration, already adopted,
  reaffirmed not replaced), `Playwright` for .NET with MSTest base
  classes (UI action/E2E). Confirmed this design has no stored-procedure
  layer at all (`ADR-004` + EF Core LINQ, one read-only raw-SQL
  exception for recursive CTEs) — nothing to test there. Chaos/property-
  based testing for hash-chain/replication/conflict invariants
  specifically remains its own, narrower open question — a different
  kind of testing than ordinary coverage.
- **Data lifecycle** (`ADR-056`) — classifies every store as
  authoritative-must-back-up (Event Log, Registry, Streaming Channel
  Store, Attachment Store, Access Audit Log) vs. rebuildable-optional
  (Entity Store, read models, materialized upcasts, recoverable by
  replay). No new mechanism — `ADR-004`'s portable-column choice already
  means nothing blocks native provider backup/PITR tooling.
- **GDPR/CCPA erasure** (`ADR-057`) — **reverses `ADR-009`'s "no
  deletion mechanism, and none is wanted" stance** (struck through
  there and in `README.md`, per this project's additive-history
  convention) — erasure is now a real requirement, solved via
  crypto-shredding: every `x-masking`-classified field is encrypted at
  rest with a per-`(AppId,EntityId)` key (envelope-encrypted, pluggable
  `IErasureKeyStore` backend — Key Vault/KMS/Vault/local-dev, not one
  vendor); erasing an entity destroys its key, never touches
  `StoredEvent.Payload` or `ADR-019`'s hash chain. The `value`/`masked`
  wrapper grows a third branch, `erased`, deliberately distinct from
  `masked` ("no one can ever see this again" vs. "you lack a claim").
  One new `x-masking` field, `erasureScope`, handles PII that belongs to
  a *different* entity than the event's own — a fifth repeated-
  relationship-shaped envelope field alongside `parentEventIds`/
  `MaterializationOfEventId`/`TelemetryPointer`/`AttachmentRef`. Honest
  caveats recorded, not dropped: some GDPR readings hold encrypted PII is
  still PII; an already-delivered webhook payload (`ADR-060`) sent
  before erasure isn't reachable after the fact.
- **Per-tenant rate limiting** (`ADR-058`) — ASP.NET Core's built-in
  `RateLimiting` middleware (no third-party library), partitioned per
  `AppId`, Token Bucket for publish, Concurrency Limiter for long-lived
  connections, enforced at the Gateway (`ADR-049`) since YARP is itself
  an ASP.NET Core app.
- **Extensibility, both halves** (`ADR-059`, `ADR-060`) — local
  extensions confirmed as composition-root registration only, formalized
  explicitly (no dynamic plugin loader, ever), cataloged in the new
  `docs/extensibility-points.md`; outbound extensibility is a new
  webhook/notification mechanism (`ADR-060`) that reuses the exact same
  durable outbox primitive `ADR-033`/`ADR-039` already share — the third
  reuse `CLAUDE.md` said to re-check, not assume, and it does inherit it
  (a `WebhookDeliveryCursor` structurally identical to `PeerSyncCursor`).
  Signing/retry follows the Standard Webhooks specification directly
  rather than a fourth bespoke convention.

All five remaining open questions (the testing-strategy residual, plus
the second review's three findings, plus the proving-ground-domain
question) are now resolved too, per direct follow-up direction —
`docs/10-open-questions.md`'s table is genuinely empty as of this pass:
- **Data residency** (`ADR-061`) — a real requirement, not
  speculative: per-`AppId` `AllowedRegions`, enforced at `ADR-033`'s
  peer-sync outbox (a region-constrained `AppId`'s events are simply
  never queued toward a disallowed site), `ADR-034`'s `EntityType` shard
  key left unchanged. Honest, named tension with `ADR-033`'s minimum-
  replication-factor-of-2: residency wins over fault tolerance when they
  conflict, stated as a deployment responsibility, not hidden.
- **Package distribution** (`ADR-062`) — the framework is distributed as
  installable packages (NuGet for the .NET engine, npm for the Vue
  client), not forked/cloned per deployment. New `EventStore.
  Abstractions` package carries every `docs/extensibility-points.md`
  interface with no implementation. `ADR-059`'s composition-root model
  is unaffected — this ADR only settles *where* the referenced code
  comes from. SemVer 2.0.0 now governs every published package, a real
  new obligation (public API surface discipline) this design didn't
  previously have.
- **Secrets management** — resolved as a direct addendum to `ADR-041`
  (no new ADR number, since it only extends that ADR's existing
  "configuration stays `Microsoft.Extensions.Configuration`" decision):
  every secret (connection strings, `ADR-057`'s KEK, `ADR-040`/`ADR-060`'s
  HMAC secrets) is an ordinary configuration value, sourced from
  whichever provider a deployment already uses — no bespoke mechanism,
  no vendor hard-dependency.
- **Distributed-correctness testing, staged** (`ADR-063`) — adopts
  `docs/comparisons/distributed-correctness-testing.md`'s suggested path
  directly: `FsCheck` (property-based) + `Polly`/`Simmy` (in-process
  fault injection) now; `Testcontainers`+`Toxiproxy` (real network-level
  partition testing) named as the first move if/when this heads toward
  production, Jepsen-style external verification as the ceiling beyond
  that — not a default, a named escalation with a stated trigger
  ("moving toward production"), not a calendar date.
- **Proving-ground domain, decided: two, not one** —
  `docs/comparisons/proving-ground-domain.md` scored 8 candidate domains
  (widened beyond the reviewer's own past-experience list on purpose)
  against every non-universal mechanism this framework has. **Clinical
  trials + connected medical-device telemetry** (broadest single-domain
  coverage; `ADR-043`'s "secondary opinion" access was already modeled
  on this exact scenario) is being paired with **digital identity/KYC**
  (the only domain that makes `ADR-036`'s DID/UCAN adoption central
  rather than secondary) — two domains chosen deliberately over one,
  both for combined feature coverage and to reduce the risk of this
  framework reading as built for one industry.

A traceability/auditability review (this session), prompted directly by
the two proving-ground domains, found one clear fix and two genuine
forks:
- **`ActorId` on every `StoredEvent`** (`ADR-064`) — checked
  `docs/data/event-log.md`'s actual shape rather than assumed, and found
  a real gap: `AttestedActorId` exists but is scoped entirely to
  `ADR-035`'s self-attestation path; an *ordinary* authenticated publish
  (`ADR-006` already verifies a real identity) had nothing recording who
  that verified caller was. Fixed directly, not left as an open
  question, since there was no real design fork — `ADR-045`'s read
  access audit log now has its missing write-side equivalent.
- **`IErasureKeyStore` corrected from "one backend per deployment" to
  keyed/multi-backend** (`ADR-057` amended) — direction received this
  session: cloud, local/dev, and on-prem/self-hosted key-management
  options must all be first-class, including running combinations of
  them in one deployment (a healthcare tenant's keys in a self-hosted
  on-prem `HashiCorp Vault`, composing with `ADR-061`'s region-pinning,
  while another tenant in the same deployment uses a cloud KMS). Now the
  same Strategy-pattern/keyed-DI shape as `IMaskingStrategy`, keyed by
  `AppId`, not a single deployment-wide pick — the same correction
  applied to `ADR-041`'s secrets addendum, where `Microsoft.Extensions.
  Configuration`'s own provider-chaining already satisfies "combinations"
  with no new mechanism needed.
- **Two genuine open questions, not forced to a decision**: whether
  non-repudiation/electronic signatures (FDA 21 CFR Part 11-shaped —
  verified against the real regulation before citing) belong at the
  framework level or the domain/application level, and whether control-
  plane actions (schema registration, RBAC grants, `AppTrustRoot`
  registration) get the same audit rigor as business events today — genuinely
  unclear either way, tracked in `docs/10-open-questions.md`.

**Local/edge active-scope caching + erasure invalidation** (`ADR-065`) —
direction received this session: a local machine only needs *active*
recording/review data, not full history, but a distributed clean/delete
must still be possible when regulation requires it. Resolved as a
client-side scoping+invalidation policy on top of mechanism this design
already has (`ADR-037`'s filtered GraphQL Subscriptions), not a new
replication tier — a local/edge client is a leaf consumer of one site,
not a fourth peer in `ADR-033`'s mesh. Checked MongoDB Atlas Device Sync
as candidate prior art and found it deprecated/shut down (Sept.
2024/2025) — correctly not adopted; CouchDB's long-established filtered
replication is the real precedent, already satisfied by this design's
existing filter mechanism. Honest, named limitation: an offline device
won't purge an erased entity until it reconnects — the same
already-delivered-copy limitation `ADR-060` states for webhooks.

**Soft/reveal-on-demand display masking** (`ADR-009` amended) — a
*different* axis from claims-based masking, disambiguated explicitly:
protects against shoulder-surfing for an already-authorized viewer, not
against unauthorized access. Original framing (server sends both
`value` and a `displayMask` together) was reconsidered mid-session per
direct observation and replaced with a better design: a
`revealOnDemand`-configured field's ordinary response is *always* the
masked/display shape, for every caller including claim-holders; seeing
the real value is a separate `revealField` action, checked against
`requiredClaim` (and optionally `ADR-066`'s step-up auth) at the moment
of the request. Cheaper serialization for the common case, sharper
`ADR-045` audit granularity (a reveal is its own logged action, not
inferred from a bulk response), and a narrower `ADR-065` local-cache
exposure window — the real value is never present on a device until
actually asked for. The general (non-`revealOnDemand`) masking wrapper
is unchanged — this is an opt-in trade for fields where shoulder-surfing
is a stated concern, not a new default for every classified field.

**Digital sign-off for regulated actions** (`ADR-066`) — resolves the
electronic-signature open question in favor of the framework level, per
direct direction: additional sign-off (password re-entry, one-time
code, or another secondary factor) triggers **RFC 9470**'s OAuth step-up
challenge (`acr_values`/`max_age`) rather than this framework
implementing any re-authentication method itself — that stays the IdP's
job (`ADR-006`). A new envelope `Signature` object (`SignerId`/
`SignedAt`/`Meaning`/`Acr`) satisfies 21 CFR Part 11 §11.50's three
linked elements, hash-chained via the existing `ADR-019` mechanism, no
new tamper-evidence primitive.

**Control-plane actions as reserved events in the same Event Log**
(`ADR-067`) — resolves the last open question this session's
traceability review raised, per direct direction: schema registration
(`ADR-020`), RBAC grants (`ADR-046`), and `AppTrustRoot` registration
(`ADR-044`) become reserved event types (reusing `ADR-020`'s existing
reservation pattern) in the *same* Event Log as business events, not a
separate audit table — because they can be linked to the business
events they cause/authorize via the existing `parentEventIds` lineage
mechanism (`ADR-005`), something a separate store couldn't participate
in. Explicitly the opposite storage choice from `ADR-045`'s `AccessLog`
(reads vs. writes, different volume profile, no causal link to draw)
— stated as a deliberate contrast, not an inconsistency. No new store,
tamper-evidence primitive, or identity scheme — entirely a reuse of
`ADR-005`/`ADR-019`/`ADR-020`/`ADR-021`'s existing mechanisms.
`docs/10-open-questions.md` is fully empty again as of this pass.

**Lineage export + bitemporal system-time playback** (`ADR-068`) —
direction received this session: export event chains/graphs for dev/
support replay, plus VCR-style play/rewind/fast-forward for litigation
review showing dropped data recovered "in place" as originally
observed, not the corrected hindsight view. Recognized the litigation
half as exactly **bitemporal modeling**'s own formally-named
distinction (Snodgrass/TSQL2, standardized in SQL:2011, shipped as SQL
Server's `FOR SYSTEM_TIME AS OF`) — this design already captures both
time axes without having queried one of them: `OccurredAt` is valid
time, `SequenceNumber` is transaction time; `ADR-021`'s `EntityStoreRow`
is a valid-time-corrected fold (deliberately smooths late arrivals for
"what's true now"), and system-time playback is the missing
transaction-time query — fold in raw arrival order up to a cutoff,
showing a `LateArrivalFlag` correction land exactly when it did. Export
walks the existing Lineage DAG (`ADR-005`), enforces masking/erasure
identically to any other read (no bypass), and adds a chain-of-custody
manifest hash reusing `ADR-019`'s primitive for a new purpose. Both
capabilities reuse existing mechanisms throughout — no new tamper-
evidence, masking, or authorization primitive. Extended further this
pass: a **self-contained offline player** for handing the litigation
export to a party with no access to this system at all — a single
static HTML file (`vite-plugin-singlefile`, reusing `ADR-039`'s Vue
playback component as a second build target, not a second stack) that
independently re-verifies the hash chain/manifest from its own embedded
data, no install, no server, ever — the industry-standard e-discovery
delivery format (MetaDiscovery/OSForensics/EZViewer), not a novel idea.

**Pluggable outbox flush triggers** (`ADR-069`) — extends `ADR-039`'s
client outbox (doesn't revise it) for genuinely isolated data capture:
the durable outbox's `Flush` operation is decoupled from its trigger,
which can now be opportunistic (existing Background Sync/open-focus),
scheduled "phone home" (Web Periodic Background Sync API — checked and
found Chromium-only/experimental, same honesty standard as the original
Background Sync caveat — or an OS/device-level scheduled task for
native clients), or explicit/manual, including a fully air-gapped
transfer that reuses `ADR-068`'s portable bundle format for outbound
commands rather than inventing a second bundle shape.

**Device input integration** (`ADR-070`) — answers "how do HID/raw USB/
serial/BLE device streams get into the web client, particularly
offline": a new `IDeviceInputSource` extensibility seam with one adapter
per browser hardware API (`WebUsbInputSource`/`WebHidInputSource`/
`WebSerialInputSource`/`WebBluetoothInputSource`), chosen per-device by
its actual hardware interface, not a deployment-wide pick — plus a
`NativeBridgeInputSource` (localhost WebSocket) for Firefox/Safari,
which support none of the four browser APIs at all (checked, not
assumed: WebUSB ~76%, Web Serial ~72%, WebHID ~27% global, Chromium-only
across the board; Web Bluetooth explicitly declined by Mozilla, absent
from WebKit entirely). Captured readings feed `ADR-039`'s existing
outbox unchanged; server-side, continuous telemetry maps to `ADR-031`'s
streaming channels and discrete readings to ordinary events defaulting
to `ADR-035`'s non-authoritative capture, unless the device itself
carries `ADR-036`'s self-attested identity.

### Propagation status

**Structurally propagated everywhere** (all of `ADR-021`–`039`):
`01-c4-architecture.md` was substantially rewritten — Entity Store,
Inbox/Router split, GraphQL Gateway (superseding the OData-era Follow/
Lineage containers), Peer Sync, Sharding, Streaming Channel Service,
Attachment Service are all real containers now, with updated Publish and
GraphQL component diagrams (streaming/attachment component diagrams still
outstanding — flagged in that file itself). `06-solution-structure.md`'s
project layout reflects every new project (`EventStore.Router`,
`EventStore.Fold`, `EventStore.GraphQL`, `EventStore.Sharding`,
`EventStore.PeerSync`, `EventStore.Streaming`, `EventStore.Attachments`,
`EventStore.Client.*`) — its detailed DI-wiring code sketches further down
that file are explicitly flagged stale in a banner, not silently wrong.
`08-build-plan.md` has ten new phases (11–20) covering every ADR from
`021` on, with real dependencies and exit criteria.
`03-api-contracts.md`, `04-odata-filter-pushdown.md`, and eight
`features/*.md` files carry clear banners naming exactly what's
superseded and pointing at the ADR that superseded it.

**Genuinely still outstanding** (named, not silently missing):
- Streaming Channel and Attachment component diagrams (`01-c4-
  architecture.md` says so itself), and now also a Live View component
  (`ADR-042`) alongside the Entity Store's — not drawn this pass.
- `03-api-contracts.md`'s Follow/Lineage/Registry-listing sections still
  describe the OData contract in full detail, not just a banner —
  rewriting them for the actual GraphQL contract shape is real,
  substantial work, not yet done. It also doesn't yet mention `ADR-040`'s
  ticket-exchange endpoints (`/oauth/token` with a ticket
  `requested_token_type`, `/oauth/introspect`) at all.
- Every banner'd `features/*.md` file's Gherkin scenarios themselves are
  unchanged — the banners say what's stale, they don't fix the
  scenarios. Rewriting `400`→`202`+`SchemaStatus` and OData→GraphQL
  syntax across ~8 files is the largest remaining chunk of this
  integration. None of them have a scenario for `ADR-040`'s ticket
  exchange yet either — it's new enough this pass that only the ADR and
  pattern doc exist, no feature doc.
- No dedicated GraphQL-pushdown doc exists yet to replace
  `04-odata-filter-pushdown.md` outright (it currently just points back
  at `ADR-037`, which doesn't have the same level of mechanism detail) —
  `docs/comparisons/api-query-layer.md` and `docs/patterns/graphql-
  query-language.md` help narrow this gap but are comparisons/pattern
  docs, not the contract-level rewrite itself.
- `06-solution-structure.md`'s DI-wiring code sketches (already flagged
  stale in its own banner) predate `ADR-041` — when that file's sketches
  are eventually redone, they should reflect explicit composition-root
  registration, not just the new projects' names.
- **`08-build-plan.md` has no phases yet for `ADR-054`–`070`** (client
  SDK generation, testing strategy, data lifecycle, GDPR erasure, rate
  limiting, extensibility model, webhooks, data residency, package
  distribution, staged correctness testing, `ActorId`, local active-scope
  caching, digital sign-off, control-plane-as-events, lineage export/
  bitemporal playback/offline player, pluggable outbox triggers, device
  input integration) — seventeen real capabilities added across this
  session's reviews with no build-plan entry, same gap this section
  already tracks for everything past `ADR-024`. A follow-up design-review
  pass (this session) confirmed `064`–`070` specifically were missing
  from this bullet's own count until just now — the tracking itself had
  drifted, not just the build plan. `ADR-057` (erasure) and `ADR-062`
  (package distribution — affects how every other phase's deliverable is
  even structured) most need real exit criteria before anything
  downstream is built.
- **Two follow-up findings from this review resolved directly, per
  direct verified reasoning, no new ADR numbers**: a signer's `ActorId`/
  `Signature` (`ADR-066`) is now stated as **categorically exempt** from
  `ADR-057`'s erasure — checked against the real regulation, not
  assumed: GDPR Art. 17(3)(b) (legal-obligation compliance) and 17(3)(e)
  (establishment/exercise/defence of legal claims) both apply directly,
  since a signature's entire evidentiary value depends on the signer's
  identity being un-erasable. `ADR-068`'s bundle-format-versioning
  question resolved the same way: full cross-version compatibility
  isn't attempted — matched framework/player versions are guaranteed
  playable, and a historical deployment's matching player is preserved
  by archiving it alongside its export, not by engineering a future
  player to read an arbitrarily old format. `docs/10-open-questions.md`
  is empty again.
- **PCI-DSS Sensitive Authentication Data boundary** (`ADR-071`) —
  another review pass, checking whether the proving-ground domains
  *not* chosen (brokerage/capital-markets, insurance) have requirements
  the two chosen domains don't exercise. Found one genuine, structural
  gap: PCI-DSS Requirement 3.2/3.2.2 bars storing Sensitive
  Authentication Data (CVV2/CVC2/CID, full track data, PIN blocks) after
  authorization **even encrypted** — a hard incompatibility with
  `ADR-023`'s persist-everything posture neither `ADR-009`'s masking nor
  `ADR-057`'s crypto-shredding can resolve, since both still write the
  real value to `Payload` first. Resolved via a reserved
  `regulatoryClassification` value, `"PCI-SAD"`, that makes schema
  *registration* (not publish) hard-reject the event type — the one
  place this design still enforces reject-on-invalid after `ADR-023`,
  since registration was never subject to that ADR's posture in the
  first place. Full PAN remains fully covered by ordinary masking/
  crypto-shredding. Confirming non-gap from the same pass: SEC Rule
  17a-4's broker-dealer recordkeeping rule already has its 2022–2023
  audit-trail alternative satisfied by `ADR-019`'s existing hash chain —
  no new mechanism needed if brokerage is ever built as a third
  proving-ground domain.
- **The proving-ground-domain comparison was extended twice more.**
  `docs/comparisons/proving-ground-domain.md` gained a coverage matrix
  for seven more candidates (pharmacovigilance, biobanking, public
  health surveillance, ITAR/export-controlled defense data, government
  case management, digital forensics, DSCSA pharma supply chain) plus a
  regulatory/compliance-framework mapping across all fifteen domains
  considered, per direct request for a "compliance axis" distinct from
  technical fit. That review surfaced two genuine, cross-domain gaps,
  both resolved directly once confirmed with concrete detail —
  **`ADR-072`**: bulk/batch ingestion (`POST /publish/batch`, still one
  persist-everything guarantee per event inside it, not a new
  persistence model) and a new `IInterchangeFormatAdapter` extensibility
  seam for external interchange standards, both inbound (confirmed
  directly from the clinical-trials domain: HL7v2 — over its real
  transport, MLLP/TCP, verified rather than assumed to be HTTP — and/or
  FHIR bridging hospital EMR data in) and outbound (ICH E2B(R3) for
  pharmacovigilance, GS1/EPCIS for DSCSA, composing with `ADR-060`'s
  webhook delivery as a transform step ahead of it, not a replacement).
- **The proving-ground coverage matrix was redone at full ADR
  granularity, per direct request** — `docs/comparisons/
  proving-ground-domain.md`'s two curated, mechanism-bundled matrices
  (18 named mechanisms each) were replaced with one unified matrix
  covering all 72 ADRs × all 15 domains: 26 ADRs that genuinely
  differentiate by domain get a full H/M/L/— row each; the other 46 —
  framework-level decisions that apply identically regardless of domain
  (persistence providers, DI/composition, spec generation, testing
  strategy, and similar) — are named explicitly in a "why these don't
  differentiate" list rather than forced into artificial per-domain
  scores or silently dropped. Verified the full 1–72 range is covered
  exactly once across the two lists, no gaps or duplicates.
- **A new `docs/domains/` folder, now complete** — one reference doc
  per proving-ground domain (applicable ADRs, governing regulations,
  special concerns) for all 15 domains considered, plus `domains/
  README.md`'s catalog, per direct request. The two chosen domains were
  written directly; the other thirteen were generated in parallel by
  four dispatched agents, each grounded in `docs/comparisons/
  proving-ground-domain.md`'s already-verified coverage matrix and
  regulatory mapping table rather than inventing new claims — spot-
  checked for quality afterward.
- **Four more propagation gaps the same design-review pass found,
  entirely untracked until now**: `01-c4-architecture.md` has zero
  mention of any `ADR-054`–`070` capability (no new containers/
  components for webhooks, rate limiting, digital sign-off, lineage
  export/playback, or device input); `03-api-contracts.md` has zero
  mention of the new `revealField`/export/playback/webhook-registration
  endpoints or the RFC 9470 step-up challenge shape; **none** of the 18
  files under `docs/features/` reference any ADR past `053` — the entire
  `054`–`070` batch has zero Gherkin coverage, the largest single gap
  found (contrast the `021`–`039` batch, whose feature-doc coverage gap
  was already closed in an earlier pass); and `06-solution-structure.md`
  reflects almost none of the new capabilities' projects (a webhook
  dispatcher, a rate limiter, an SDK-generation step, device-input client
  packages) beyond one incidental `EntityErasureKey` mention.

**Feature-doc coverage gap closed** (found during a full-package review,
this pass): `ADR-021`, `030`, `031`, `032`, `033`/`034` (combined, per
`docs/comparisons/README.md`'s own grouping), `035`, `036`, and `039` had
zero feature doc each — not stale, just absent — despite being
foundational, Accepted ADRs. All eight now exist under `docs/features/`
(`entity-concept.md`, `multi-tenancy.md`, `streaming-channels.md`,
`binary-attachments.md`, `replication-and-sharding.md`,
`non-authoritative-capture.md`, `did-ucan-attestation.md`,
`mvvm-client.md`), each with real Gherkin scenarios (not banners over
stale ones, since there was nothing stale to banner — these are new).
`08-build-plan.md`'s Phase 11 and Phase 17 exit criteria now cite
`entity-concept.md`/`non-authoritative-capture.md` directly, matching
every other phase's citation pattern; Phases 12/14/15/16/18/20 still
don't cite a feature doc by name and could be updated the same way if
this gets revisited.

Check this section before assuming any doc is fully consistent with
`ADR-025` onward — the structural/architectural picture is current
everywhere; the exhaustive detail (every contract example, every Gherkin
scenario) is not, and each file says so where it applies.
