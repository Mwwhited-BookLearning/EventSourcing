[← ADR index](../07-adrs.md)

# ADR-068: Lineage-scoped event export for dev/support replay, bitemporal system-time playback, and a self-contained offline player for litigation review

Status: Accepted

Context: Two related capabilities requested this session — export an
event chain/graph so it can be replayed in development (a dev/support
need), and VCR-style play/rewind/fast-forward over history so a
litigation reviewer can see data "as originally occurred," including a
dropped/late-arriving value being recovered **in place, as it happened**
— not the corrected, hindsight-clean picture the authoritative Entity
Store already shows. Both are new views over history this design didn't
build yet, distinct from what already exists (`ADR-010`'s tail/replay,
`ADR-024 §8.4`'s per-property `entityHistory`).

**The litigation-review requirement is precisely bitemporal modeling's
own, formally-named distinction** — checked against real prior art
rather than designed from scratch: [C.J. Date/Snodgrass's bitemporal
terminology](https://www.researchgate.net/publication/261845780_Temporal_features_in_SQL2011)
and its standardization in **SQL:2011** (system-versioned tables for
*transaction time*, application-time period tables for *valid time*;
[SQL Server's `FOR SYSTEM_TIME AS OF`](https://learn.microsoft.com/en-us/sql/relational-databases/tables/temporal-tables?view=sql-server-ver17)
is the concrete, shipped implementation of the query this ADR needs).
This design already captures both temporal axes without knowing it:
**valid time** is `OccurredAt` (`ADR-029` — when something was true);
**transaction time** is `SequenceNumber`/arrival order (when the system
learned about it). `EntityStoreRow` (`ADR-021`) is a **valid-time-
corrected** view — it folds in logical (`OccurredAt`) order specifically
so a late arrival can't silently overwrite newer data, which is exactly
right for "what do we now know is true," and exactly wrong for "what did
the system show at the time" — the litigation ask is a genuine **system-
time (transaction-time) query**, a different axis this design has never
queried before, not a variant of the existing one.

Decision:

**1. Lineage-scoped event export, for dev/support replay:**
- **Walks the existing Lineage DAG (`ADR-005`), not a new traversal
  mechanism** — given a starting `EntityId` (or set of them), gathers
  every causally-connected event (ancestors/descendants, via the same
  `IEventLineageQueryProvider`/`CycleGuard` machinery the Lineage API
  already uses) into an export set.
- **Goes through the exact same read-path enforcement as any other
  query — no bypass.** `ADR-008`'s `RequiredClaims` (Read direction,
  `ADR-050` — correction, verified against `docs/data/schema-
  registry.md`: already list-shaped by the time this ADR was written),
  `ADR-009`'s
  masking (including `ADR-057`'s `erased` branch for anything crypto-
  shredded), and `ADR-045`'s read-access audit logging all apply
  unchanged. An export is a read, not a privileged escape hatch — a
  field the exporting actor couldn't see live, they can't see exported
  either.
- **Portable bundle format**: NDJSON of the exported `StoredEvent`s in
  `SequenceNumber` order, plus a manifest carrying every referenced
  `EventTypeDefinition`/`SchemaVersion` (so an importing environment can
  validate/upcast correctly even if its own registry hasn't seen that
  version) and a **manifest hash** — `SHA-256` over the ordered original
  `ChainHash` values plus export metadata (exported-by `ActorId`,
  exported-at) — reusing `ADR-019`'s hash primitive for a new purpose:
  proving the export is a complete, unaltered copy of that chain
  segment, a real chain-of-custody concern for the litigation use case
  specifically, not just the dev-replay one.
- **Import preserves provenance rather than pretending the copy is
  original**: an imported event gets a fresh `SequenceNumber`/`ChainHash`
  in the receiving environment's own log (it *is* a new append there),
  while `OriginalSequenceNumber`/`OriginalChainHash`/`ImportedFrom`
  travel as new envelope metadata recording where it actually came from
  — never silently presented as if it had been organically published
  in the new environment.

**2. Bitemporal system-time playback, for litigation review:**
- **A new query mode, not a variant of the existing fold**: "reconstruct
  this entity's state as of transaction time T" — fold only the events
  with `SequenceNumber <= T`, applied **in arrival order**
  (`SequenceNumber` order), with no logical-time correction — the
  literal opposite of `ADR-029`'s fold rule, deliberately, because the
  point is showing what an observer *actually saw*, late-arrival
  corrections and all, at the moment each one landed. When a
  `LateArrivalFlag`'d event is reached in the playback, the
  reconstruction visibly changes right there — "recovered in place, in
  real time," not smoothed away as `EntityStoreRow` already does for the
  authoritative view.
- **VCR-style controls (play/rewind/fast-forward) are a stepping
  interface over consecutive `SequenceNumber` positions** for a given
  entity (or lineage graph) — play advances forward at a configurable
  pace, rewind moves backward, fast-forward skips ahead; each position
  is a system-time-as-of reconstruction per the rule above.
- **v1 computes this on demand, not via a new persisted store** —
  simplest thing that satisfies the actual requirement, consistent with
  this design's own KISS precedent (`ADR-009`'s declined masking
  strategies). **Named, not forced, future optimization**: if scrubbing
  performance ever needs it, periodic system-time snapshots (the same
  snapshot-then-replay-forward shape `ADR-016`'s `ProjectionSnapshot`
  and `ADR-027`'s materialized upcasts already use elsewhere in this
  design) would let a seek jump to the nearest snapshot and replay only
  the delta — not built now, since nothing has asked for it yet.
- **Masking/erasure enforcement is identical to any other read** — the
  same reasoning as the export mechanism above; system-time playback is
  a new *ordering* of history, not a new *authorization* surface.

**3. Self-contained offline player, for handing the litigation export to
a party with no access to this system at all:**
- **Real prior art checked, not a novel idea**: standalone, self-
  executing offline evidence viewers are the industry-standard delivery
  format in e-discovery/forensics specifically — [MetaDiscovery's
  self-executing load-file viewer](https://metadigitalforensics.com/),
  [OSForensics' standalone HTML case reports](https://www.osforensics.com/generate-reports.html),
  and SANS's [EZViewer](https://www.sans.org/tools/ezviewer) (zero-
  dependency, standalone) are all the same shape this ADR adopts, not
  invented for this design.
- **A single, self-contained static HTML file — the exported bundle's
  data and the playback UI both embedded, zero external requests, opens
  by double-click in any browser, no install, no server, ever.** Built
  as an alternate bundle target of the *same* playback component
  `ADR-039`'s Vue/MVVM client already needs for the live litigation-
  review case — not a second technology stack — using [`vite-plugin-
  singlefile`](https://github.com/richardtallent/vite-plugin-singlefile)
  (inlines all JS/CSS into one HTML output; already fits, since
  `ADR-055` already established this client is Vite-based). One
  playback implementation, two build targets: connected-to-a-live-API
  and embedded-in-a-static-file.
- **Self-verifying, not merely self-contained — with one honest limit,
  found by a design review this session, not glossed over.** On load,
  the player independently recomputes `ADR-019`'s `ChainHash` sequence
  and this ADR's manifest hash from the embedded event data and displays
  a clear pass/fail integrity result. **This recomputation is exact,
  independent of trusting the exporting party, for every event whose
  fields are unmasked in the bundle** — the common, intended case, since
  an export is normally run by a fully-authorized compliance/legal
  actor for a litigation hold, holding every relevant claim already, and
  `ADR-057`-erased fields are permanently unrecoverable by design,
  full-access exporter or not. **It cannot be exact for an event
  containing a field masked because the *exporting actor themselves*
  lacked the claim for it** (the claims-restricted case `ADR-068`'s
  no-bypass rule exists for) — `ChainHash`/`PayloadHash` were computed
  once, at original publish time, over the real stored bytes; a field
  replaced with `ADR-009`'s `{"masked": ...}` wrapper before bundling is
  not those bytes, so re-hashing the bundle's (redacted) content cannot
  reproduce the original chain values for that specific event. What the
  player *can* still verify in that case: the chain's **structural**
  linkage (each event's own `ChainHash`/`PayloadHash` — envelope
  metadata, unaffected by masking — correctly derives from the prior
  event's, per `ADR-019`'s formula over the metadata fields), just not
  an independent re-derivation from the masked field's content itself.
  This is a genuine, unavoidable trade-off between "redact before
  sharing" and "let the recipient re-derive the exact original hash of
  what was redacted" — not a bug to design around, a fact to state
  honestly. The player's UI should distinguish "fully independently
  verified" from "verified except for N masked fields, chain linkage
  intact" rather than presenting one undifferentiated pass/fail.
- **No masking/claims logic in the player, deliberately** — enforcement
  already happened once, at export time (per the export mechanism
  above); the player renders exactly what's in the bundle, `masked`/
  `erased` branches included verbatim, faithfully. Building a second
  enforcement point into the offline player would be redundant at best
  and a second place for the two to drift at worst.

**4. Bundle-format versioning — resolved directly, no new ADR (fills in
this ADR's own previously-flagged gap):** full forward/backward
compatibility of the export format across every future major framework
version (`ADR-062`'s SemVer) was flagged as unweighed. Direction
received: unreasonable to guarantee that far out — instead, **matched
versions must be playable, and the matching player travels with the
export rather than being reconstructed later**:
- **The manifest carries the producing framework's SemVer version**
  (`ADR-062`) — a bundle records exactly which version created it.
- **The guarantee is narrowed to "same version reads its own bundles,"
  not "any future version reads any past bundle."** A framework version
  is only ever responsible for reading bundles it itself (or a
  compatible minor/patch release, ordinary SemVer semantics) produced —
  `ADR-038`'s N-1/N+1 window is about *event schema* evolution within
  one running deployment and doesn't need to stretch to cover this
  separate concern.
- **The mitigation is archival, not engineering-around the problem**:
  exporting a litigation bundle also snapshots (or makes trivially
  rebuildable) the exact matching offline-player build current at
  export time, so the pair travels and is preserved together — a
  historical deployment's matching player stays playable indefinitely
  because it was kept, not because a future player was engineered to
  read an arbitrarily old format.

Consequences:
- Resolves `docs/10-open-questions.md`'s bundle-format-versioning row —
  the table is empty again.
- `01-c4-architecture.md`'s GraphQL Gateway gains two new resolver
  concepts (lineage export, system-time playback) — not yet drawn into
  the component diagram, flagged as remaining propagation work.
- The Entity Store's existing valid-time-corrected fold (`ADR-021`/
  `ADR-029`) is completely unchanged — this ADR adds a second, parallel
  way to view history, it doesn't touch the authoritative one.
- A dev/support export and a litigation system-time playback compose
  naturally: replaying an *imported* bundle's `OriginalSequenceNumber`
  order reproduces "what did the customer actually see, in what order"
  for debugging — the same mechanism serving both stated use cases,
  not two unrelated features that happen to share a name.
- No new tamper-evidence, masking, or authorization primitive anywhere
  in this ADR — entirely a reuse of `ADR-005`/`ADR-008`/`ADR-009`/
  `ADR-019`/`ADR-029`/`ADR-045`/`ADR-057`'s existing mechanisms, applied
  to two new *read* shapes.
- `06-solution-structure.md` gains a new build target/project for the
  offline player (sharing `ADR-039`'s Vue playback component, a separate
  `vite-plugin-singlefile`-configured build, not a separate app) — not
  yet detailed, flagged as remaining propagation work.
  **Corrected, 2026-08-11: built.** `client-web/packages/reference-app/
  offline-player/` (entry point + `vite.offline-player.config.ts`) and
  `client-web/packages/reference-app/scripts/embed-bundle.mjs` (the
  per-export "embed and rebuild" step `§4` below describes) now exist —
  paths corrected here, a design-compliance audit this session found
  they'd drifted one level: nested under the `reference-app` package,
  not directly under top-level `client-web/`; `OfflineBundleViewer.vue`/`BitemporalPlaybackControl.vue`
  under `client-web/packages/reference-app/src/components/playback/` are the shared Vue
  component this bullet anticipated, mounted from both build targets.
  One honest scope narrowing found while building, not anticipated by
  this ADR's own sequence diagram wording: the player recomputes the
  **manifest hash** exactly (SHA-256 over the bundle's own ordered
  `ChainHash` values, cross-verified against the real C# `ManifestHash.
  Compute` output this session) plus a masked/erased-field **count**,
  but does **not** attempt a per-event `PayloadHash`-from-`Payload`
  re-derivation — `ExportedEventLine` carries no `ParentEventIds`
  (`EventPayloadHash.Compute`'s third input), so recomputing that hash
  for any event with real causal parents (`ADR-005`) would produce a
  false "tampered" result on ordinary, legitimate lineage data. The
  "fully verified" / "verified except N masked fields" distinction this
  ADR calls for is preserved, just derived from the manifest-hash check
  plus the masked-field count rather than a per-event byte-exact
  re-hash. See `client-web/packages/mvvm-client/src/playback/verifyBundle.ts`'s own comments.
- `docs/libraries/web/vite-plugin-singlefile.md` is the concrete usage
  write-up for the new library dependency — added this pass.

**Compliance note** (a proving-ground compliance review, this session):
the offline player's independent hash-chain/manifest re-verification is
a direct fit for Federal Rule of Evidence 902(13)/(14) — self-
authentication of "a record generated by an electronic process or
system that produces an accurate result" and of "data copied from an
electronic device... if authenticated by a process of digital
identification" — letting a reviewing party authenticate the export from
the math alone, with no live testimony about this system's internals
required. System-time playback's "a correction lands in place, never
obscuring what was originally shown" rule is likewise the exact behavior
21 CFR Part 11 §11.10(e) requires of an audit trail ("record changes
shall not obscure previously recorded information").
