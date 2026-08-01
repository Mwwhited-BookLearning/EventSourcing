# Open Questions

A live tracker for genuinely unresolved questions — distinct from every
other document type here: an ADR (`docs/adrs/`) is a decision already
made; a comparison (`docs/comparisons/`) weighs a fork *before* a
decision; this file is for the fork that hasn't been weighed yet at all,
or a decision that was deliberately left partial.

**When a row gets resolved, delete it outright — don't strike it
through and retain it.** (Direction received this session, reversing an
earlier same-session correction that said the opposite.) Every resolved
row already has a real, permanent, scoped home: the ADR that resolved
it, or an existing ADR's additive addendum. Retaining a struck-through
copy here duplicates that home for no reason. The one-line historical
record of *what got resolved, when, by which ADR* lives in that day's
`docs/changes/{date}.md` instead — see `docs/changes/2026-07-31.md` for
this session's resolutions. If another doc cites this file by row
number, update that citation to point at the resolving ADR (or that
day's changelog) once the row is deleted — a row number is not a stable
long-term address.

**A row can also be deleted for a different reason: it turns out to be
genuinely domain-specific, not a framework-wide fork**, and gets
relocated to the owning domain's own `README.md` Special Concerns
section instead (e.g. algorithmic-bias auditing → `docs/domains/
insurance-telematics/README.md`; FDA's 15-day adverse-event clock →
`docs/domains/pharmacovigilance/README.md`). That's a "this never
belonged in the framework-level tracker" correction, not a resolution —
nothing is lost, the content lives on on in the domain doc.

**Not included here, on purpose**:
- Domain-specific regulatory/compliance gaps found while reviewing one
  domain's own `README.md` — those live in that domain's own Special
  Concerns section, not here, even while genuinely unresolved.
- Anything deferred purely on scheduling with no open design question of
  its own (e.g. `ADR-007`, `ADR-009`'s masking-enforcement build — both
  fully designed, just sequenced later in `08-build-plan.md`). Those are
  priority calls, not open questions — see `CLAUDE.md`/`08-build-
  plan.md` for that distinction.
- Known propagation/documentation debt with no fork to weigh (a missing
  diagram, a stale Gherkin scenario) — tracked in `TODO.md`, not here.
- A question genuinely still open but explicitly **deprioritized** for
  now rather than resolved — noted in place, in the row itself, with a
  **Back-burnered** marker and the reason, rather than removed (nothing
  was decided, so there's no resolution to move elsewhere).

## How to add an entry

Found a real fork or an explicitly-left-open decision while writing an
ADR/pattern/comparison/library doc? Add a row here in the same pass —
don't let it live only as a buried sentence in that doc's Consequences
section where it's easy to lose track of.

| # | Question | Raised by | Why it's still open |
|---|---|---|---|
| 5 | **Back-burnered — deprioritized while this remains a design package/POC, not a shipped product.** What CI/CD platform, build→release→run separation, and dev/staging/prod environment-promotion path does this framework's own build assume? `ADR-001` assumes "CI/CD must build and publish three artifacts," `ADR-074` assumes an SBOM-generation build step — neither names the pipeline tool itself, and `ADR-026` only decides launch shape (Aspire dev / Compose prod), not a third environment. **Narrowed**: the *publish target* half is already answered — `ADR-062` commits to NuGet+npm; what remains is purely *which tool* runs the pipeline and whether a third (staging) environment exists. `ADR-080` narrows it further: npm provenance specifically requires GitHub Actions or GitLab CI/CD. | Common-architecture-decision review | Narrowed, not resolved — the publish-target half is settled; the CI-platform-tool/staging-environment half remains genuinely unweighed but explicitly low priority at this stage |
| 7 | **Back-burnered — deferred as an operations/runbook concern, not a dev decision.** Should this design define SLIs/SLOs (router fold lag, peer-sync outbox depth/age, webhook delivery lag, hash-chain verification success) with alert thresholds, and is an incident-response/on-call process in framework scope at all? | Common-architecture-decision review | `ADR-026` wires full OpenTelemetry instrumentation, the real prerequisite this needs — *which* thresholds/who's on call is deployment-specific operational policy, not something the framework itself should decide |
| 9 | Should this design adopt a maintained threat model (STRIDE or similar) over `01-c4-architecture.md`'s containers, plus a `docs/` risk register (arc42 §11), given ~20 ADRs each make individually-sound security decisions that have never been checked together against one adversary/trust-boundary model? | Common-architecture-decision review | Genuinely unweighed — no document currently asks what a hostile tenant or insider can do with the combination of `ADR-035`'s unauthenticated-submitter posture, `ADR-023`'s persist-everything, and `ADR-058`'s volume-only rate limiting |
| 11 | **Mostly resolved — one narrower residual left.** "Whose clock is authoritative" is close to a non-question: `SequenceNumber`+`ChainHash` (`ADR-019`) guarantee tamper-evidence/ordering independent of `OccurredAt`'s truthfulness; no new policy needed there. The device-clock-lie-detection residual is also resolved: `ADR-083` adds optional `TelemetrySample.MonotonicElapsedMicros`. **Only remaining residual**: should RFC 3161 timestamping still be adopted for `ADR-066` signatures / `ADR-068` litigation exports? | Common-architecture-decision review; narrowed by direct design conversation | Only the RFC 3161 adoption question remains genuinely unweighed |
| 16 | Should `ADR-040`'s ticket-signing secret and `ADR-060`'s webhook-signing secret each become a current+previous pair in configuration, so `ADR-060` can emit the dual signatures the Standard Webhooks spec it already adopted explicitly supports for zero-downtime rotation? | Common-architecture-decision review | `ADR-041`'s secrets addendum resolves sourcing well, and every other secret's rotation is owned elsewhere (SPIRE certs, JWKS, `ADR-057`'s DEKs) — these two self-minted secrets are the one residual with no rotation story at all |
| 18 | Beyond `ADR-056`'s deliberately-deferred retention-window/backup-cadence policy, what's the actual *mechanism* for archiving an ever-growing Event Log/`AccessLog` — table partitioning, or some way to detach a segment of `ADR-019`'s hash chain without breaking verification? **Narrowed**: cold-storage *tiering* is resolved, but only for `ADR-032`'s attachment/blob store — a structured `StoredEvent`/`AccessLogEntry` row has no access-pattern-driven "temperature" the way a large binary blob does. | Common-architecture-decision review; narrowed by direct design conversation | The table-partitioning/hash-chain-segment mechanism gap remains fully open, distinct from `ADR-056`'s deployment-time cost/policy deferral and from `ADR-032`'s resolved attachment-tiering note |
| 19 | Is internationalization/localization (i18n/l10n) in or out of framework scope, given `ADR-073` just set the precedent that a UI-cross-cutting standard (accessibility) belongs at the framework level regardless of which pattern renders a screen? | Common-architecture-decision review | Zero mention anywhere; lower confidence than the other rows, but worth an explicit ruling rather than silence |
| 21 | Should this design adopt something like a **frontier token** (a per-origin-stream offset map returned by every write, presented on a later read, guaranteeing that read reflects at least the presented offsets even if served by a lagging replica) to give causal/read-your-writes consistency across `ADR-033`'s gossip-replicated multi-site mesh — or continue to deliberately decline read-after-write consistency, as already stated? Real prior art exists to weigh (e.g. Cosmos DB session tokens, causal-consistency tokens in other distributed databases). | Independent cross-reference against a separate architecture document | Genuinely unweighed — `ADR-033`'s mesh (now scoped to one tenant's own multi-site deployment, per `ADR-075`) has this exact exposure: write at Site A, immediately read from Site B, don't see your own write |
