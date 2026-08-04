[← Comparisons index](README.md)

# Should "this event expects a tracked response within a window" be a framework capability, or left to the application?

**Raised by:** a direct conversation about mixed-SLA telemetry consumers
(some near-real-time, some tolerant of minutes' delay), which surfaced a
related but distinct question once Intraoperative Neurophysiological
Monitoring (IONM) and polysomnography (sleep-study EEG) were named as
concrete use cases (see `docs/domains/clinical-trials-device-telemetry/
README.md`'s "Special concerns" — the dual-channel live-safety split
those two now name concretely): an IONM alert isn't just "delivered
fast," it needs someone to *see and act on it* in time, which is a
different property than delivery latency. This doc is about that second
property — a tracked expectation that a specific event gets a specific
kind of response within a window — not about transport delivery, which
this design already handles (synchronous `202` receipt on publish,
`ADR-011`/`ADR-023`; Follow's own checkpoint-is-the-ack shape, `ADR-010`/
`09-cqrs-read-models.md`; webhooks' at-least-once retry plus
`WebhookDeliveryFailed` dead-letter, `ADR-060`).

## The fork

Today, "an alert expects an acknowledgment" is already fully expressible
with existing primitives and zero framework change: an application
registers a second event type (e.g. an `IonmAlertAcknowledged`, the same
shape `authorityDecision.TargetEventId` already uses to reference the
event it's deciding on, `docs/domains/clinical-trials-device-telemetry/
features/adverse-event-capture-and-review.md`) and writes its own
watcher — architecturally an "internal follower," the same shape
`ADR-015`'s `ProjectionHost` and `ADR-031`'s `ChannelDerivationWorker`
already use — that queries for alerts with no matching acknowledgment
inside whatever window it chooses, and reacts however it wants. The
question is whether that pattern is worth lifting into a first-class,
opt-in registry capability instead.

### Option A — Leave it to the application (status quo, no framework change)

| | |
|---|---|
| **Pros** | Zero new framework surface — fully expressible today. Escalation policy (what "acknowledged" means, who can supply it, what happens if the window elapses) is inherently domain-specific, the same "core engine contains zero domain-specific knowledge" boundary `ADR-030` already draws, and `ADR-031` already draws again specifically for telemetry ("detection is explicitly out of framework scope — a detector is an application concern"). There is direct, already-settled precedent for punting a structurally similar case to the domain: pharmacovigilance's 15-day expedited-reporting clock (FDA 21 CFR 314.80(c)(1)/600.80(c)(1)) — "event happened, a follow-up action is expected within a deadline, consequences follow if it's missed" — was evaluated and explicitly left with no owning ADR (`docs/domains/pharmacovigilance/README.md`), not generalized into the framework. |
| **Cons** | Duplicated logic across domains — every domain that needs this (IONM's ack, the pharmacovigilance clock, any future case) re-derives its own version of the same watcher, including the parts that are easy to get subtly wrong independently each time: window-boundary math, a late-arriving response racing the timeout check, and making the watcher itself durable/restart-safe rather than an in-memory timer that forgets on crash (`CLAUDE.md`'s own standing rule — check a new outbox-shaped mechanism actually inherits fault-tolerance, don't assume it by family resemblance — applies to a hand-rolled watcher exactly as much as a hand-rolled outbox). No single source of truth: "which event types expect an ack, and within how long" would live scattered across N applications' own code, unlike `EventTypeDefinition.RequiredSignature`, which answers "which event types require a sign-off" from one queryable registry table. Inconsistent failure shape — this design has a deliberate discipline of turning a detected problem into an ordinary, `Follow`-able event (`EventUpcastFailed`, `ChannelLagDetected`, `WebhookDeliveryFailed`); left to each app, one domain's "missed ack" might follow that discipline and another's might not. |

### Option B — A first-class, opt-in registry capability

A new nullable `EventTypeDefinition.ExpectedResponse { EventType, Within }`
(same "`null` = no expectation, set = opt in" shape `RequiredSignature`
already uses), paired with a framework-provided watcher that publishes a
reserved `ExpectedResponseMissing` event when the window elapses with no
matching response — mirroring `ADR-031`'s `ChannelLagDetected` exactly
("detector notices an absence, publishes an ordinary, Follow-able event"
rather than a silent timer).

| | |
|---|---|
| **Pros** | A single, queryable, registry-level answer to "which event types have a response-time SLA, and what is it" — the same discoverability `RequiredSignature` already gives sign-off requirements. One shared, properly fault-tolerant watcher implementation (durable checkpoint, leader-lease if singleton, `ADR-078`-shaped) instead of N independently-reinvented ones. Consistent failure shape falls out automatically — every domain's missed-ack becomes an ordinary event the same way, without each one having to remember to do that. |
| **Cons** | The deadline semantics genuinely differ across the two concrete cases in hand: IONM's is same-session, wall-clock, clinical (seconds to a couple of minutes); pharmacovigilance's 15-day clock is calendar/business-day-driven and ends in a regulatory filing, not a clinical action. A single generic `Within: TimeSpan` risks being either too naive to fit the regulatory case or, if stretched to cover it, accreting enough configuration (business-day calendars, filing-specific side effects) that it stops being simpler than the app just writing its own watcher. Only one of the two cases (IONM) is actually built out as a worked feature doc so far — generalizing from a single example is exactly the "designing for a hypothetical future requirement" this project's own conventions warn against (`CLAUDE.md`: "don't design for hypothetical future requirements... three similar lines is better than a premature abstraction"). Reopens, at least partially, the domain-knowledge boundary `ADR-031` deliberately drew around detection/escalation. |

## Recommendation

Leave it as an application-level convention for now (Option A) — a
deferral, not a permanent decline. The two candidate cases already in
this design (IONM's same-session clinical ack, pharmacovigilance's
calendar-day regulatory clock) differ enough in their deadline semantics
that generalizing from the one case that's actually been worked through
(IONM) risks guessing at a shape the second case wouldn't fit anyway —
better to build both as real, concrete watchers first and see whether
the logic actually converges. If it does, Option B's `ExpectedResponse`
field plus a reserved `ExpectedResponseMissing` event is the shape to
build, directly mirroring `ADR-031`'s already-Accepted `ChannelLagDetected`
pattern — no new mechanism family, just a second application of one this
design already trusts. Tracked as a back-burnered row in
`docs/10-open-questions.md`, not closed, so this doesn't quietly
disappear the way `CLAUDE.md`'s own cautionary tale (`ChannelOrigin.Origin`/
`OriginId`) warns a flagged-but-untracked gap tends to.

**Update, same session — superseded by `ADR-094`.** On further
discussion this Recommendation's own test turned out to be the wrong
one: IONM and the pharmacovigilance clock are domain-level *example
instances*, not the shape of the framework decision itself, so waiting
for their specific numbers to "converge" was never actually the right
gate — a plain `TimeSpan` already covers a 300-second window and a
15-day window equally well with no strain. The real question was always
whether the underlying *relationship* ("this event is a reply to that
event") generalizes cleanly on its own, independent of any domain's
specifics — and it does, directly via Hohpe & Woolf's Correlation
Identifier pattern (`docs/patterns/request-reply-correlation.md`), which
neither option above actually considered. `ADR-094` adopts a refined
Option B on that basis: a generic envelope field (`RespondsToEventId`)
plus an opt-in registry declaration (`ExpectedResponse`), with escalation
policy itself still left entirely to the application — narrower framework
surface than this comparison's original Option B sketch, and decided
without needing a second worked domain example first. `docs/
10-open-questions.md`'s row is resolved (deleted) accordingly. The
pros/cons above are kept as written, for the teaching value in seeing
where the original framing was and wasn't the right test.
