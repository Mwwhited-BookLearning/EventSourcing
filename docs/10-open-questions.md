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
nothing is lost, the content lives on in the domain doc.

**Not included here, on purpose**:
- Domain-specific regulatory/compliance gaps found while reviewing one
  domain's own `README.md` — those live in that domain's own Special
  Concerns section, not here, even while genuinely unresolved.
- **Pure operations/deployment-process concerns with no architecture or
  development decision embedded** — alert thresholds, on-call rotation,
  paging policy, and similar. Confirmed explicitly this session (former
  row 7's residual, after `ADR-088` resolved the actual instrumentation
  half): these aren't merely deprioritized, they were never a fork this
  file should hold in the first place — no design decision is possible
  or needed at the framework level, only an operational runbook a
  deployment writes for itself.
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
| 1 | Every background worker in this design (`RouterWorker`, `DerivationWorker`, `WebhookOutboxPump`, `PeerSyncWorker`, `ChannelDerivationWorker`, `ExpectedResponseWatcher`, ...) advances via a fixed-interval poll loop against the database — never a push notification. Whether that should stay the sole mechanism, or gain a push-based low-latency wake-up on top of it, is undecided. | Direct request, 2026-08-12, while building "Proving-Ground Application UX" | Genuinely a multi-way fork with no clearly superior default: (a) Postgres `LISTEN`/`NOTIFY` — a real native mechanism, but fire-and-forget (no durable queue; a disconnected listener misses a `NOTIFY` outright, 8000-byte payload cap) — the well-established pattern for this exact gap is "notify-to-wake, poll-to-confirm" (`NOTIFY` shortens the sleep, the poll-based cursor stays the actual correctness guarantee), not a replacement for polling; (b) SQL Server Service Broker — a genuine durable, transactional queue whose most distinguishing feature is *activation*, not just durability: internal activation has SSB itself invoke a stored procedure only when the queue holds unread messages (configurable max concurrent readers/backoff), and external activation fires a Broker event an outside listener can subscribe to — no always-running poller at all in either case, the queue itself is the trigger, a materially different model from "poll on a timer" or even "notify-to-wake." Still SQL-Server-only, and this design targets three providers (`ADR-001`) — SQLite has no analog at all (it isn't a client-server database — no separate server process for a cross-process notification model to attach to; its own `sqlite3_update_hook` is process-local only), so polling would still be needed as the universal fallback regardless; (c) RabbitMQ (or a similar dedicated broker) — true push/ack/redelivery, but a new external infrastructure dependency this design doesn't have anywhere today (no message broker at all — the event log is the only durable store, `ADR-023`/`ADR-056`) — weigh against `ADR-041`'s explicit-composition/no-third-party-magic framing before adopting; (d) an in-process signal (a `Channel<T>` woken on commit) — genuinely lowers latency within one replica, but doesn't help cross-replica coordination, which every one of these workers actually needs. Nobody has picked between these, or even decided whether the added complexity is worth it over the current, already-working poll-based design — this row exists so the choice doesn't get made by default via continued silence. |
