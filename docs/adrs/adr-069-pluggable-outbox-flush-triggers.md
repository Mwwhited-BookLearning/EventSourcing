[← ADR index](../07-adrs.md)

# ADR-069: Pluggable outbox flush triggers — opportunistic, scheduled ("phone home"), and explicit/manual, including a fully offline transfer

Status: Accepted — extends `ADR-039`'s client outbox and
`docs/patterns/pwa-offline-outbox.md`, doesn't revise either

Context: Direction received this session: some real deployments involve
data capture that's genuinely **isolated** — not just "temporarily
offline, will reconnect soon" (what `ADR-039`'s outbox and its Background
Sync trigger already assume) but a device or system that may go long
stretches with no network path at all, needing either a **periodic
phone-home** (a scheduled sync attempt) or an **explicit upload action**
(a deliberate, operator-initiated sync — potentially with no network
involved at all).

Decision:
- **The durable outbox exposes one idempotent `Flush` operation; any
  trigger may invoke it, any number of times, safely** — already true in
  spirit (`ADR-039`'s Background Sync trigger and its open/focus fallback
  are already two different triggers for the same underlying flush), now
  stated as the general principle this ADR builds on: `ADR-011`'s
  publish idempotency means a redundant or repeated `Flush` attempt is
  always safe, so the framework never needs to reason about *which*
  trigger fired, only that `Flush` ran.
- **Three trigger categories, not the two `ADR-039` already covers**:
  1. **Opportunistic** (existing, unchanged) — Background Sync API /
     open-focus fallback, `docs/patterns/pwa-offline-outbox.md`.
  2. **Scheduled ("periodic phone-home") — new.** For a web/PWA client:
     the [Web Periodic Background Sync
     API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Periodic_Background_Synchronization_API)
     where available — checked rather than assumed: **Chromium-only,
     experimental, not Baseline; unsupported in Firefox and Safari as of
     this writing**, the same kind of honest caveat `ADR-039`'s own
     Background Sync note already states, not glossed over. Where
     unavailable, or for a non-browser/native device client (isolated
     medical equipment, an industrial gateway), the equivalent is an
     **OS-level or device-level scheduled task** calling the same
     `Flush` operation on a timer — this framework doesn't build a
     scheduler, it only needs `Flush` to be safely callable by one.
  3. **Explicit/manual — new.** A user/operator-initiated "sync now"
     action, for the case where connectivity is available but only
     briefly or only when deliberately sought out (a field device
     brought within range of a network on a set schedule, an operator's
     own judgment call). **For a genuinely air-gapped device with no
     network path at all, ever**: export the outbox's queued commands to
     a portable medium for physical transport ("sneakernet") and later
     import at a connected system — **reusing `ADR-068`'s portable
     bundle format directly** (NDJSON + manifest + chain-of-custody hash)
     rather than inventing a second bundle shape, just carrying queued
     outbound commands instead of historical read-side events. The same
     verification story applies: the receiving system can confirm the
     transferred bundle is complete and unaltered before importing it.
- **The client doesn't need to know or care which category fired
  `Flush`** — same operation, same durability guarantee, same
  idempotency, regardless of whether it was an automatic background
  event, a scheduled wake-up, or a human clicking a button.

Consequences:
- No change to `ADR-039`'s outbox durability model or
  `pwa-offline-outbox.md`'s existing opportunistic-trigger description —
  this is a pure addition of two more ways to invoke the same operation.
- `docs/libraries/web/` gains no new library for the scheduled web case
  (Periodic Background Sync is a browser API, not a package); the
  OS/device-level scheduled-task case is deployment-specific and out of
  this framework's own scope, the same way `ADR-051`'s peer discovery
  leaves cluster bootstrap to deployment configuration rather than
  building a scheduler itself.
- The explicit/manual offline-transfer path means `ADR-068`'s portable
  bundle format now has two real producers (server-side history export,
  client-side outbox export) and two real consumers (a dev/support
  environment, a reconnected server) — worth keeping the format genuinely
  shared rather than letting the two drift into separate shapes over
  time.
