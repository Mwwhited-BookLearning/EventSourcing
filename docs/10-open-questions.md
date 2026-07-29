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
| Should operation-level scopes (`registry:admin`, `events:publish`, etc., `ADR-006`) themselves become `AppId`-scoped — e.g. `registry:admin:app1` — so one application's operator can't touch another application's schemas? | `ADR-030` | Explicitly raised and explicitly not answered: today any caller with `registry:admin` can register a type for *any* `appId`. Flagged for whichever future ADR takes it on. |
| Template engine for `ADR-039`'s entity views: raw HTML+JS with a small injected binding runtime, vs. a lightweight templating syntax compiled client-side? | `ADR-039` | Left open deliberately — a real remaining decision for whoever builds this, not resolved by any ADR since. |
| Full mechanism for streaming-channel redaction (`RedactedRange`: `ChannelId`, `FromTimestamp`, `ToTimestamp`, `RequiredClaim`) — the field shape is named, but the read-time transform that substitutes silence/blank-frames/zeroed-samples over a redacted span is not designed beyond that shape. | `ADR-031` | "Not designed further than this shape here; a full ADR if/when it's actually built." |
| Attachment-specific authorization model beyond optionally inheriting an owning event's `RequiredReadClaim` — is a per-attachment claim ever needed independent of the event/entity it's linked to? | `ADR-032` | "Not designed further here" — named as a real gap, not resolved. |
| Which concrete CEL-for-.NET library to adopt for declarative upcast mappings (`Cel.NET` / `Cel` / `cel-net` / `cel-csharp`) — the ecosystem is fragmented with no single dominant implementation. | `docs/libraries/dotnet/cel-dotnet.md` | Explicitly candidates-only; needs a spike against this project's actual upcast-mapping shapes before locking in, per that doc's own recommendation. |
| NWebDav vs. `Dav.AspNetCore.Server` for the WebDAV surface — NWebDav was picked for longer track record, with no specific reason to prefer the newer alternative beyond it existing. | `docs/libraries/dotnet/nwebdav.md` | Flagged as "worth re-evaluating at build time," not a firm long-term commitment. |
| Richer masking-content strategies beyond the fixed `PartialReveal`/`Hash` placeholder (`ADR-009`) — e.g. configurable partial-reveal rules per field. | `ADR-009`, `README.md`'s "deliberately is not" list | "A further, undecided proposal on top of" the shipped fixed-placeholder mechanism — genuinely unspecified, not just unscheduled. |
| Should every *read* made under an `ADR-043` delegated access grant be logged as its own auditable event, the way break-glass access literature treats per-use audit logging as standard? | `ADR-043` | This project's read side isn't event-sourced today (only writes are) — logging every read would be a genuinely new mechanism, not a reuse of an existing one. Worth its own ADR if/when built, not decided by default. |
| Who may register/deregister an `AppTrustRoot` entry (`ADR-044`)? Registering the wrong DID as trusted for an `AppId` grants that DID's holder the ability to mint arbitrary permissions within that application's namespace. | `ADR-044` | "Not designed further here" — a real gate is needed (presumably a `registry:admin`-adjacent scope), not yet specified. |

## How to add an entry

Found a real fork or an explicitly-left-open decision while writing an
ADR/pattern/comparison/library doc? Add a row here in the same pass —
don't let it live only as a buried sentence in that doc's Consequences
section where it's easy to lose track of (that's exactly how several of
the rows above were found during a full-package review, not when they
were first written).
