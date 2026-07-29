# Open Questions

A live tracker for genuinely unresolved questions — distinct from every
other document type here: an ADR (`docs/adrs/`) is a decision already
made; a comparison (`docs/comparisons/`) weighs a fork *before* a
decision; this file is for the fork that hasn't been weighed yet at all,
or a decision that was deliberately left partial. When an entry here
gets resolved, move it to a real ADR (or fold it into the ADR that
raised it) and delete the row — this file should only ever contain
things that are *actually* still open, not a permanent archive.

**Not included here, on purpose**: anything deferred purely on
scheduling with no open design question of its own — `ADR-007`
(derived/materialized event types) and `ADR-009`'s masking-enforcement
build are both fully designed, just sequenced later in
`08-build-plan.md`. Those are priority calls, not open questions — see
`CLAUDE.md`/`08-build-plan.md` for that distinction, not this file.

| Question | Raised by | Why it's still open |
|---|---|---|
| Full mechanism for streaming-channel redaction (`RedactedRange`: `ChannelId`, `FromTimestamp`, `ToTimestamp`, `RequiredClaim`) — the field shape is named, and [`docs/comparisons/streaming-redaction-mechanism.md`](comparisons/streaming-redaction-mechanism.md) has now searched prior art and narrowed the fork (zero-fill vs. noise for `RawScalar`/`RawBinary`, tone vs. silence and a spatial/temporal scope disambiguation for `Media`, read-time vs. materialized per `ADR-028`/`ADR-027`'s precedent, plus a sideband existence-signal requirement), but no ADR has actually been written yet — the mechanism is better-grounded, not yet decided. | `ADR-031` | One genuine sub-question the comparison couldn't settle in the abstract: whether a "public" materialized redacted view is worth building depends on how many distinct claim populations a real deployment's `RedactedRange`s produce — a build-time fact, not a design choice. Otherwise ready for the queued streaming-redaction ADR to pick up. |
| CEL vs. JSONata for declarative upcast mappings — CEL fits the problem shape better (narrower, safer, faster, purpose-built) and stays `ADR-037`'s pick, but JSONata's .NET port (`Jsonata.Net.Native`) is currently the single, consolidated, spec-conformant implementation, vs. CEL-for-.NET's four fragmented community packages. | `docs/comparisons/upcast-transform-language.md` | A real, live tension between design-fit and implementation-maturity, not resolved by assumption — needs a spike against this project's actual upcast-mapping shapes and each library's real state at build time before locking in either way. |
| Which richer masking-content strategy to build first beyond the shipped `FixedValue` placeholder (`ADR-009`), and whether tokenization belongs in `x-masking` at all — configurable partial-reveal and format-preserving masking fit `ADR-009`'s existing claims-gated wrapper cleanly (partial-reveal is the cheapest real increment); generalization/bucketing fits only as a single-value transform, never as a k-anonymity guarantee; tokenization's separate-party/separate-mechanism reversal model doesn't fit the wrapper at all and would need its own mechanism if ever needed. See `docs/comparisons/masking-strategies.md`. | `ADR-009`, `README.md`'s "deliberately is not" list, `docs/comparisons/masking-strategies.md` | Narrowed, not resolved: the architecture fit of each candidate strategy is now known, but no specific application need has picked one to actually build yet. |
| `ADR-050`'s `RequiredClaims` list generalizes `ADR-008` beyond one claim per direction — but when multiple claims are declared for the *same* direction, must a caller hold all of them (AND) or any one (OR)? Not resolved. | `ADR-050` | Explicitly deferred, not assumed either way. |
| Does surfacing `x-required-claims`/`x-masking` in a *publicly*-readable generated OpenAPI/AsyncAPI document (`features/auth.md`: spec docs are anonymous) itself leak information — "this field requires `clearance:phi`" reveals *where* sensitive data lives, even without granting access to the value? | `ADR-050` | A real risk `ADR-050` raised about its own decision, not assumed away — no mitigation designed yet (e.g. restricting spec-doc access, or omitting the extension from the public variant). |
| How does a new peer server (`ADR-033`) discover other sites when independently deployed, outside any single Aspire/`docker-compose` orchestration boundary? mDNS/DNS-SD were re-examined and are the wrong tool (LAN-scoped), not a fit for cross-internet, cross-site discovery. | `ADR-033`, re-raised during a `references.md` review | [`docs/comparisons/peer-discovery.md`](comparisons/peer-discovery.md) now compares a static seed-peer list, DNS-based seed discovery, and a dedicated discovery/rendezvous service, and recommends the static seed-peer list at this design's stated scale (a handful of regional sites) — but no ADR has formalized that choice yet, so it stays here until one does. |

## How to add an entry

Found a real fork or an explicitly-left-open decision while writing an
ADR/pattern/comparison/library doc? Add a row here in the same pass —
don't let it live only as a buried sentence in that doc's Consequences
section where it's easy to lose track of (that's exactly how several of
the rows above were found during a full-package review, not when they
were first written).
