# Open Questions

A live tracker for genuinely unresolved questions — distinct from every
other document type here: an ADR (`docs/adrs/`) is a decision already
made; a comparison (`docs/comparisons/`) weighs a fork *before* a
decision; this file is for the fork that hasn't been weighed yet at all,
or a decision that was deliberately left partial. **When an entry here
gets resolved, strike it through in place and point at the resolving
ADR/comparison — never delete the row.** (Corrected, this session: an
earlier version of this paragraph said "delete the row," which every
actual resolved row below has consistently *not* done, for good reason —
several ADRs now cite a specific row number directly, e.g. `ADR-076`
"Resolves `docs/10-open-questions.md` row 12"; deleting the row would
break that citation. See `.claude/protocols/additive-history-editing.md`
for the general rule this file already follows.) A struck-through row
still means the *design fork itself* is closed — this file just doesn't
erase the historical record of what was asked and how it got answered,
the same way an Accepted ADR is never deleted once superseded. **A row
can still be deleted outright, though, in one specific case: it turns
out to be genuinely domain-specific, not a framework-wide fork, and
gets relocated to the owning domain's own `README.md` Special Concerns
section instead** (this session: former rows 3 and 4, algorithmic-bias
auditing and the FDA 15-day adverse-event clock, moved to `docs/domains/
insurance-telematics/README.md` and `docs/domains/pharmacovigilance/
README.md` respectively). That's a "this never belonged in the
framework-level tracker" correction, not a resolution, and nothing is
lost — the content now lives, in expanded form, in the domain doc.

**Not included here, on purpose**: domain-specific regulatory/compliance
gaps found while reviewing one domain's own `README.md` — those live in
that domain's own Special Concerns section, not here, even while
genuinely unresolved (see the deletion case just above). Also excluded:
anything deferred purely on
scheduling with no open design question of its own — `ADR-007`
(derived/materialized event types) and `ADR-009`'s masking-enforcement
build are both fully designed, just sequenced later in
`08-build-plan.md`. Those are priority calls, not open questions — see
`CLAUDE.md`/`08-build-plan.md` for that distinction, not this file. Same
reasoning excludes a generalized-framework review's (this session)
documentation-completeness findings — missing component diagrams, the
still-partial GraphQL contract rewrite, stale `features/*.md` Gherkin
scenarios, pre-`ADR-041` DI-wiring sketches — from this table: those are
known propagation debt with no fork to weigh, already tracked in
`CLAUDE.md`'s "Genuinely still outstanding" section. That same review's
six other findings are now all resolved: client-SDK/codegen (`ADR-054`),
tenant rate limiting (`ADR-058`), data lifecycle/backup (`ADR-056`),
GDPR/CCPA erasure (`ADR-057`), and extensibility cataloging, both the
local half (`ADR-059`, `docs/extensibility-points.md`) and the outbound/
webhook half (`ADR-060`). The testing-strategy residual, the second
review's three findings, and the proving-ground-domain question are now
**all resolved too** — staged testing adoption (`ADR-063`), data
residency (`ADR-061`), package distribution (`ADR-062`), secrets
management (`ADR-041`'s addendum), and the proving-ground domain
decision (both clinical trials/device telemetry and digital identity/
KYC, `docs/comparisons/proving-ground-domain.md`). A traceability/
auditability review (this session), prompted by the two proving-ground
domains, found one clear fix (`ActorId` on every `StoredEvent`,
`ADR-064` — not tracked here, since it wasn't a real fork). The
electronic-signature fork this review also raised is now resolved too
— framework-level, via RFC 9470 step-up authentication (`ADR-066`). The
last remaining fork — control-plane audit rigor — is resolved too:
`ADR-067` models schema registration/RBAC grants/`AppTrustRoot`
registration as reserved events in the same Event Log. A follow-up
design-review pass (two independent agents, this session) then found two
more genuine forks; the first — whether a signer's identity is ever
erasable — is now resolved too: **no, categorically exempt**, per GDPR
Article 17(3)(b)/(e) (compliance with a legal obligation; establishment/
exercise/defence of legal claims) — see `ADR-066`'s amendment. Everything
else the review pass checked (ADR index accuracy, envelope-field
consistency, cross-reference correctness) came back clean or was fixed
directly as a documentation bug, not left as an open question. The
second fork — bundle-format versioning — is resolved too: matched
framework/player versions are guaranteed playable, full forward
compatibility across major versions isn't attempted, and a historical
deployment's matching player is preserved by archiving it alongside the
export rather than engineered around — see `ADR-068`'s amendment. A
compliance-focused review of the proving-ground regulatory mapping
(this session) found four more real gaps; two were resolved directly
(`ADR-073` now adopts WCAG 2.1 AA as a cross-cutting accessibility
standard, not folded into `ADR-039`; SOX Section 404's ITGCs turned out to already be satisfied by
`ADR-045`/`ADR-019`/`ADR-067`, a confirming non-gap, not a decision).
Two forks from that pass remained open — resolved this session, see rows
1–2 below. **A full compliance-review pass across all 74 ADRs and all 15
proving-ground domain docs (this session)**, plus a dedicated common-
solution-architecture gap check (grounded in the Twelve-Factor App, AWS
Well-Architected, arc42, Google SRE, and SLSA — verified, not assumed),
resolved the review itself but surfaced **17 more genuinely open
forks**: two domain-specific regulatory gaps found while updating the
domain docs (since relocated to `docs/domains/insurance-telematics/
README.md` and `docs/domains/pharmacovigilance/README.md` — see "Not
included here, on purpose" above), and fifteen common architecture-
decision areas this design has never actually weighed (rows 5–19). Row
13 (feature flags) was the single highest-priority row here — an
apparent outright contradiction between two already-Accepted ADRs
(`ADR-038` vs. `ADR-041`/`ADR-058`) — **now resolved and written up as
`ADR-077`** (a chained, reload-token-based configuration provider, not a
real contradiction). Rows 1, 2, 12, and 14 were resolved the same
session, written up as `ADR-079`, an `ADR-045` addendum, `ADR-076`, and
`ADR-078` respectively — all five now struck through below.

| # | Question | Raised by | Why it's still open |
|---|---|---|---|
| ~~1~~ | ~~Should OFAC sanctions screening and BSA SAR filing be a framework-level extensibility seam or purely domain/application logic?~~ **Resolved.** `ADR-079` — yes, an extensibility seam (`ISanctionsScreeningProvider`, shaped like `IErasureKeyStore`), but scoped to the KYC/Meridian application's own composition root, not core Duplex — the first domain-scoped (non-core) extension point in this design. | Proving-ground compliance review (earlier pass) | — |
| ~~2~~ | ~~What does a GDPR Art. 33/34 breach-notification workflow look like on top of `ADR-045`'s existing audit log?~~ **Resolved.** `ADR-045`'s addendum — deliberately out of framework scope; an external legal/business process, not a functional requirement. | Proving-ground compliance review (earlier pass) | — (follow-up doc updates tracked in `TODO.md`, not here) |
| 5 | **Back-burnered, this session — not resolved, deprioritized while this remains a design package/POC, not a shipped product.** What CI/CD platform, build→release→run separation, and dev/staging/prod environment-promotion path does this framework's own build assume? `ADR-001` assumes "CI/CD must build and publish three artifacts," `ADR-074` assumes an SBOM-generation build step — neither names the pipeline tool itself, and `ADR-026` only decides launch shape (Aspire dev / Compose prod), not a third environment. **Direction received, this session**: the *publish target* half of this question is already answered, not open — `ADR-062` already commits to NuGet+npm as the production publish flow; what's actually still unweighed is narrower than the row's own title suggests — purely *which tool* runs that pipeline (GitHub Actions / Azure DevOps / GitLab CI / etc.) and whether a third (staging) environment exists — neither of which needs deciding until this moves toward a real, shipped build. | Common-architecture-decision review, this session; narrowed by direct design conversation, this session | Narrowed, not resolved — the publish-target half is already settled by `ADR-062`; the CI-platform-tool/staging-environment half remains genuinely unweighed but is explicitly low priority at this stage |
| ~~6~~ | ~~Should this design adopt dependency-vulnerability scanning and a build-signing/provenance standard on top of `ADR-074`'s SBOM generation?~~ **Resolved.** `ADR-080` — yes: Dependabot + `dotnet list package --vulnerable` + `npm audit` for scanning; NuGet author signing + `npm publish --provenance` for build provenance, targeting SLSA Level 2 now (Level 3 a named future escalation, not decided). | Common-architecture-decision review, this session | — |
| 7 | **Back-burnered, this session — deferred as an operations/runbook concern, not a dev decision.** Should this design define SLIs/SLOs (router fold lag, peer-sync outbox depth/age, webhook delivery lag, hash-chain verification success) with alert thresholds, and is an incident-response/on-call process in framework scope at all? | Common-architecture-decision review, this session; deprioritized by direct design conversation, this session | `ADR-026` wires full OpenTelemetry instrumentation, which is the real prerequisite this ADR already supplies — *which* thresholds/who's on call is deployment-specific operational policy, not something the framework itself should decide |
| 8 | What should liveness vs. readiness probes actually test — in particular, should a degraded dependency (unreachable peer, replica lag) fail readiness, given `ADR-023`'s never-block publish posture? | Common-architecture-decision review, this session | `06-solution-structure.md` explicitly defers this ("not detailed further in this doc"); the readiness-vs-never-block tension is real and unweighed |
| 9 | Should this design adopt a maintained threat model (STRIDE or similar) over `01-c4-architecture.md`'s containers, plus a `docs/` risk register (arc42 §11), given ~20 ADRs each make individually-sound security decisions that have never been checked together against one adversary/trust-boundary model? | Common-architecture-decision review, this session | Genuinely unweighed — no document currently asks what a hostile tenant or insider can do with the combination of `ADR-035`'s unauthenticated-submitter posture, `ADR-023`'s persist-everything, and `ADR-058`'s volume-only rate limiting |
| 10 | What are this design's actual throughput/latency/scale targets (events/sec, fold lag, query latency, tenants/entities/events ceilings), and should load/soak/performance-regression testing be its own decision, distinct from `ADR-055`/`ADR-063`'s functional/correctness coverage? | Common-architecture-decision review, this session | `README.md` states one qualitative goal ("never lose or corrupt data") and no measurable one, yet several ADRs (`031`, `015`, `034`, `058`) make real performance trade-offs against no stated target |
| 11 | **Mostly resolved — one narrower residual left.** "Whose clock is authoritative" turns out to be close to a non-question: `SequenceNumber` (server-assigned, arrival order) plus `ChainHash` (`ADR-019`) are what actually guarantee tamper-evidence and ordering, independent of `OccurredAt`'s truthfulness. `OccurredAt` is correctly advisory-only, exactly as already built; no new "whose clock is authoritative" policy is needed. Its first residual — a second, monotonic timer for `ADR-070`-sourced device telemetry, to make a lying wall-clock *detectable* — is now also **resolved**: `ADR-083` adds optional `TelemetrySample.MonotonicElapsedMicros`, captured by the client-side recording agent. **Only remaining residual**: should RFC 3161 timestamping still be adopted for `ADR-066` signatures / `ADR-068` litigation exports — unaffected by the above, still fully open. | Common-architecture-decision review, this session; narrowed and mostly resolved by direct design conversation | Only the RFC 3161 adoption question remains genuinely unweighed |
| ~~12~~ | ~~How does a package consumer actually apply this framework's EF Core migrations to a database they own?~~ **Resolved.** `ADR-076` — EF Core migration bundles, run as a single deploy-time step (never `Database.Migrate()` at app startup); a provider-native declarative tool (DACPAC/`SqlPackage`, or pgschema for Postgres) may apply the EF-generated SQL instead. | Common-architecture-decision review, this session | — |
| ~~13~~ | ~~What is the actual flag-vs-config boundary and mechanism, given `ADR-038` promises instant feature-flag rollback but `ADR-041`/`ADR-058` route everything through static `IConfiguration`?~~ **Resolved — not actually a contradiction.** `ADR-077` — a chained, reload-token-based `IConfigurationProvider` (flag state as a reserved Event Log event, per `ADR-067`'s pattern; polled every few seconds; `AppId`-scoped per `ADR-075`) gives the instant toggle without violating either ADR. | Common-architecture-decision review, this session | — |
| ~~14~~ | ~~Can more than one `Router`/`UpcastMaterializer`/outbox-pump instance run per site, and if so, how do they avoid double-folding the same `EntityId`?~~ **Resolved.** `ADR-078` — single-active-worker per role, via a database-backed lease (Leader Election pattern, adapted from Azure's Blob lease to a lease row for portability), not a quorum system. | Common-architecture-decision review, this session | — (`06-solution-structure.md`'s single-instance spec-caching assumption is a distinct, still-separately-open question) |
| ~~15~~ | ~~Should this design explicitly affirm shared-store multi-tenancy... or add a database-per-tenant deployment option for regulated tenants?~~ **Resolved.** See [`docs/comparisons/multi-tenant-isolation-model.md`](../comparisons/multi-tenant-isolation-model.md) and `ADR-075` — silo (dedicated deployment per tenant), federated via `ADR-060`/`ADR-072`, not shared-store pool multi-tenancy. | — | — |
| 16 | Should `ADR-040`'s ticket-signing secret and `ADR-060`'s webhook-signing secret each become a current+previous pair in configuration, so `ADR-060` can emit the dual signatures the Standard Webhooks spec it already adopted explicitly supports for zero-downtime rotation? | Common-architecture-decision review, this session | `ADR-041`'s secrets addendum resolves sourcing well, and every other secret's rotation is owned elsewhere (SPIRE certs, JWKS, `ADR-057`'s DEKs) — these two self-minted secrets are the one residual with no rotation story at all |
| ~~17~~ | ~~Should the repository's actual `LICENSE`... be recorded as a deliberate choice, or reconsidered?~~ **Resolved — confirmed, not reconsidered.** MIT Non-AI is the deliberate choice; no runtime license-key/entitlement mechanism exists or is wanted (distinct question from Jason's "licensing modeled as a ledger participant," which is domain/application-specific and not adopted here either). | — | — (the SPDX-friction follow-up note is tracked in `TODO.md`, not here) |
| 18 | Beyond `ADR-056`'s deliberately-deferred retention-window/backup-cadence policy, what's the actual *mechanism* for archiving an ever-growing Event Log/`AccessLog` — table partitioning, or some way to detach a segment of `ADR-019`'s hash chain without breaking verification? **Narrowed this session**: cold-storage *tiering* specifically is resolved, but only for `ADR-032`'s attachment/blob store, not here — a structured `StoredEvent`/`AccessLogEntry` row has no access-pattern-driven "temperature" the way a large binary blob does, so hot/cool/cold tiering doesn't apply to this residual the way it does to attachments. | Common-architecture-decision review, this session; narrowed by direct design conversation, this session | The table-partitioning/hash-chain-segment mechanism gap remains fully open — distinct from the cost/policy question `ADR-056` already, correctly, defers to deployment time, and now also distinct from `ADR-032`'s resolved attachment-tiering note |
| 19 | Is internationalization/localization (i18n/l10n) in or out of framework scope, given `ADR-073` just set the precedent that a UI-cross-cutting standard (accessibility) belongs at the framework level regardless of which pattern renders a screen? | Common-architecture-decision review, this session | Zero mention anywhere; lower confidence than rows 5–18, but worth an explicit ruling rather than silence |
| ~~20~~ | ~~Should `TelemetryChannel` gain a `ThreadId` grouping multiple simultaneous channels under one session, and should `TelemetryPointer` generalize to a list?~~ **Resolved.** `ADR-081` — yes to both; `TelemetryChannel.ThreadId` groups a multi-channel session, denormalized onto each `TelemetryPointer` entry; `TelemetryPointer` is now `List<TelemetryPointerEntry>`, one entry per contributing channel. | Direct design conversation, this session | — (existing feature-doc examples showing the old singular shape are stale — tracked in `TODO.md`) |
| 21 | Should this design adopt something like a **frontier token** (a separate architecture document's mechanism: a per-origin-stream offset map returned by every write, presented on a later read, guaranteeing that read reflects at least the presented offsets even if served by a lagging replica) to give causal/read-your-writes consistency across `ADR-033`'s gossip-replicated multi-site mesh — or continue to deliberately decline read-after-write consistency, as already stated? Real prior art exists to weigh (e.g. Cosmos DB session tokens, causal-consistency tokens in other distributed databases). | Independent cross-reference against a separate architecture document, this session | Genuinely unweighed — `ADR-033`'s mesh (now scoped to one tenant's own multi-site deployment, per `ADR-075`) has this exact exposure: write at Site A, immediately read from Site B, don't see your own write. Previously stated as a deliberate decline, not evaluated against a real working alternative until now |
| 22 | How should tenant-to-tenant federation (`ADR-075`) actually map one tenant's native event shape onto another's, given `ADR-072`'s `IInterchangeFormatAdapter` was built for *externally standardized* formats (HL7v2/FHIR/ICH E2B(R3)/GS1-EPCIS), each anchored to a real published spec — but two federating tenants each have their own independently-versioned schema registry (`ADR-030`) with no shared external spec to anchor a mapping to, and a bespoke adapter per tenant pair doesn't scale past a handful of federation partners? | Buildability review of `ADR-075`, this session | A real, un-glossed-over residual named directly in `ADR-075`'s own text — genuinely unweighed whether this needs a new "native" adapter category, a shared interchange schema tenants opt into, or is accepted as an inherently bespoke, low-volume integration cost |

## How to add an entry

Found a real fork or an explicitly-left-open decision while writing an
ADR/pattern/comparison/library doc? Add a row here in the same pass —
don't let it live only as a buried sentence in that doc's Consequences
section where it's easy to lose track of (that's exactly how several of
the rows above were found during a full-package review, not when they
were first written).
