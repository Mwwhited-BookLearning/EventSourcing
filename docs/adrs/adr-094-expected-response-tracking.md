[← ADR index](../07-adrs.md)

# ADR-094: Expected-response tracking — a generic `RespondsToEventId` envelope field + opt-in registry declaration, never a domain-specific mechanism

Status: Accepted

Context: A conversation about mixed-SLA telemetry consumers (some
near-real-time, some tolerant of minutes' delay) named two concrete
examples — Intraoperative Neurophysiological Monitoring (IONM, a
same-session surgical alert) and polysomnography (an overnight,
delay-tolerant sleep study) — which this design already handles without
change (`ADR-031`'s dual-`TelemetryChannel` mechanism;
`docs/domains/clinical-trials-device-telemetry/README.md`'s "Special
concerns"). Naming IONM surfaced a genuinely distinct question, weighed
in full in `docs/comparisons/event-response-acknowledgment.md`: delivery
latency (already solved — `ADR-011`'s synchronous `202`, `ADR-010`'s
Follow checkpoint, `ADR-060`'s webhook retry/dead-letter) is not the same
property as *"did a specific expected response actually arrive within a
window."* That comparison's first pass framed the fork as IONM's
same-session ack vs. pharmacovigilance's 15-day regulatory clock
(`docs/domains/pharmacovigilance/README.md`) converging or not — on
review this was the wrong test: **IONM and polysomnography are domain-
level example instances, not the shape of a framework decision.** A
generic `TimeSpan`-based window already covers both a 300-second and a
15-day deadline without strain; what actually matters is whether the
*relationship itself* — "this event is a response to that event" — can
be expressed generically enough that no domain's specifics leak into the
framework mechanism, the same test every other envelope field in this
design already has to pass.

**Prior art, searched before designing anything bespoke** (per
`.claude/protocols/verify-before-citing.md`): Hohpe & Woolf's Enterprise
Integration Patterns catalog names exactly this shape —
[**Correlation Identifier**](https://www.enterpriseintegrationpatterns.com/patterns/messaging/CorrelationIdentifier.html)
("a reply message should contain a Correlation Identifier, a unique
identifier that indicates which request message this reply is for"),
paired with **Request-Reply**. This design already cites the same
authors' Idempotent Receiver/Inbox/Dead Letter Channel patterns
(`docs/patterns/idempotent-receiver-and-inbox.md`) — Correlation
Identifier is a fourth pattern from the same catalog, not a new
provenance to track.

**The envelope-field gut-check** (`CLAUDE.md`'s standing rule: "if an
eighth [relationship-shaped envelope field] comes up, ask what question
it specifically answers first"): this is that eighth field. The question
it answers — "which prior event does this one satisfy a declared
response expectation for" — is answered by none of the existing seven.
`parentEventIds` (`ADR-005`) is broader and untimed: causal derivation,
any number of parents, no expectation that one *must* exist by any
particular time. `MaterializationOfEventId` (`ADR-027`) is a reshaped
copy of the same logical event, not a reply to a different one.
`TelemetryPointer` (`ADR-031`/`ADR-081`) names a position in a signal
stream. `AttachmentRef` (`ADR-032`) names supporting binary content.
`erasureScope` (`ADR-057`) names whose crypto-shredding key protects a
field. `Signature` (`ADR-066`) captures a sign-off attestation — a
response event *may* also carry a `Signature` (an `authorityDecision`
already does), but signing and responding are orthogonal: a response
needn't be signed, and a signed event needn't be a response to anything.
`OriginalSequenceNumber`/`OriginalChainHash`/`ImportedFrom` (`ADR-068`)
record where an imported event actually came from. None of these let a
consumer ask "is there a reply to event X yet, and if not, has its
window elapsed" — that requires its own field.

Decision:
- **A new envelope field, `StoredEvent.RespondsToEventId : Guid?`** —
  optional on any publish, naming the `EventId` of the event this one is
  a reply to. Kept out of `Payload`, the same reasoning `ADR-005`
  established for `parentEventIds`: it must never collide with JSON
  Schema validation or `additionalProperties` rules. Deliberately
  **not** existence-validated at publish time (no `ParentValidationMode`-
  style Strict/Permissive fork) — a `RespondsToEventId` naming an
  `EventId` that doesn't (yet, or ever) exist is simply a response that
  correlates to nothing findable, never a rejected publish; adding a
  second validation-mode fork here would answer a question nobody has
  actually asked yet.
- **A new, nullable `EventTypeDefinition.ExpectedResponse { string
  ResponseEventType, TimeSpan Within }`**, declared on the *request*
  event type — `null` = no expectation (today's behavior, unchanged),
  set = "a `ResponseEventType` event carrying a matching
  `RespondsToEventId` is expected within `Within`." Same "`null` means
  opt-out, set means opt-in" shape `RequiredSignature` already
  established (`ADR-066`) — no new registration idiom invented. **v1
  deliberately allows exactly one `ResponseEventType`, not a list** —
  `RequiredClaims`' OR-of-list shape (`ADR-050`) was considered and
  declined here absent a stated need for "any of several response types
  satisfies this expectation"; extend to a list the same way `ADR-050`
  extended `RequiredClaims`, if and when a real case asks for it.
- **A durable `ExpectedResponseTracker { Guid RequestEventId (PK),
  string RequestEventType, string ExpectedResponseEventType, DateTimeOffset
  DeadlineAt, Guid? SatisfiedByEventId, DateTimeOffset? SatisfiedAt,
  DateTimeOffset? EscalatedAt }`** — the durable-checkpoint discipline
  every background worker in this design already uses (`ProjectionCheckpoint`,
  `PeerSyncCursor`, `WebhookDeliveryCursor`), not an in-memory timer that
  forgets on crash (`CLAUDE.md`'s standing fault-tolerance rule for any
  outbox/tracker-shaped mechanism).
- **A new singleton background worker, `ExpectedResponseWatcher`**,
  leader-lease-gated exactly like `Router`/`UpcastMaterializer`/the
  outbox pumps (`ADR-078`) — architecturally an "internal follower," the
  identical shape `ADR-015`'s `ProjectionHost` and `ADR-031`'s
  `ChannelDerivationWorker` already use, no new consumer pattern:
  1. Tails every event type any `EventTypeDefinition.ExpectedResponse`
     is configured on; on each such event, inserts an
     `ExpectedResponseTracker` row with `DeadlineAt` = this event's
     receipt time + `Within`.
  2. Tails every distinct `ResponseEventType` any registered
     `ExpectedResponse` names; on each such event carrying a
     `RespondsToEventId`, looks up the tracker row by that ID and stamps
     `SatisfiedByEventId`/`SatisfiedAt` — **whether on time or late**. A
     late response is still recorded, never treated as an error; this
     design's "never lose data" governing principle applies here exactly
     as everywhere else.
  3. On a periodic sweep, for tracker rows past `DeadlineAt` with
     `SatisfiedAt` still `null` and `EscalatedAt` still `null`, publishes
     a reserved **`ExpectedResponseMissing`** event — through the
     completely ordinary publish path (`ADR-023`), the same "make a
     detected problem an inspectable, `Follow`-able record" discipline
     `EventUpcastFailed` (`ADR-020`) and `ChannelLagDetected` (`ADR-031`)
     already use, not a bespoke side-channel — carrying `{RequestEventId,
     RequestEventType, ExpectedResponseEventType, DeadlineAt}` in its
     payload, and setting `RespondsToEventId = RequestEventId` on its own
     envelope too, so "everything that references event X" (its
     children, its response, and now its missing-response escalation)
     all pivot through the one generic field, not two different
     mechanisms. Stamps `EscalatedAt` so this fires exactly once per
     tracker row, even if a later sweep runs again before a late response
     arrives.
  4. Reserved at the platform level, never registered via `PUT
     /registry/{event-type}` — the identical treatment `EventUpcastFailed`
     already gets (`ADR-020`).
- **Auth reuses existing mechanisms**: `ExpectedResponseWatcher` is a
  Follow caller and a publisher like any other (`ADR-015`'s reasoning for
  `ProjectionHost`, applied unchanged) — a new seeded OAuth2 client
  (`expected-response-watcher-client`, scopes `events:follow` +
  `events:publish`) alongside the others in `docs/features/auth.md`'s
  seeded-clients table. **Landed directly in this pass, not assumed
  already done**: checking found `ADR-015` itself had made the identical
  claim about adding `projections-client` and that row had never actually
  been added — a real instance of `CLAUDE.md`'s own cautionary lesson
  ("verify a propagation claim against the actual file, never trust an
  ADR's own Consequences section saying something is done"). Both rows
  are added together this pass.
- **Escalation policy stays explicitly out of framework scope** — the
  same boundary `ADR-031` already draws for telemetry detection ("a
  detector is an application concern"), applied here without
  modification. The framework's job ends at "publish `ExpectedResponseMissing`
  as an ordinary, `Follow`-able fact"; what a domain does about it (page a
  backup clinician, trigger a regulatory-filing reminder, do nothing) is
  entirely app-owned, exactly like `ChannelLagDetected` today. This is
  precisely the split the request/response envelope makes possible: the
  framework recognizes the *relationship* generically; only the domain
  names *which* event types participate in it and *what happens* when one
  goes unanswered.

Consequences:
- **Resolves `docs/10-open-questions.md`'s back-burnered row** (deleted
  per that file's own "when a row gets resolved, delete it outright"
  rule) and supersedes the deferral recommended in
  `docs/comparisons/event-response-acknowledgment.md` — that comparison's
  pros/cons stay as written for teaching value (per
  `docs/comparisons/README.md`'s own stated purpose), amended with a note
  pointing here, since the actual reason to proceed (a fully generic
  envelope relationship, not "the two examples converged") differs from
  what that comparison's Recommendation originally argued.
- **`docs/data/event-log.md` gains `RespondsToEventId` on `StoredEvent`**,
  documented as the eighth distinct relationship-shaped envelope field,
  and **`docs/data/schema-registry.md` gains `EventTypeDefinition.
  ExpectedResponse` and the `ExpectedResponseTracker` entity** — landed in
  the same pass as this ADR, per `CLAUDE.md`'s standing rule that the ADR
  introducing a persisted field is that field's naming authority and must
  not defer the matching data-model edit.
- **`CLAUDE.md`'s "repeated relationship" bullet is updated to name eight
  fields, not seven**, with this field's specific justifying question
  stated inline — not just incremented, per that bullet's own standing
  instruction to ask (and record) what a new field specifically answers.
- **IONM is the first concrete configuration example**, documented in
  `docs/domains/clinical-trials-device-telemetry/README.md`: an
  `IonmAlertRaised` event type declares `ExpectedResponse {
  ResponseEventType: "IonmAlertAcknowledged", Within: <domain-chosen,
  same-session duration> }`; the neurotechnologist's or surgeon's
  acknowledgment publishes an `IonmAlertAcknowledged` event carrying
  `RespondsToEventId` back at the alert. Polysomnography needs no
  `ExpectedResponse` at all — its scoring workflow has no "escalate if
  unanswered" requirement, which is exactly why it was never claimed as a
  second convergence data point for *this* mechanism (it motivated the
  SLA-latency question this ADR is downstream of, not the
  acknowledgment question itself). Pharmacovigilance's 15-day clock
  remains domain-owned and un-migrated to this mechanism in this pass —
  a real candidate for a future configuration, not retrofitted here
  without that domain's own review.
- **Deliberately not solved here**: multi-type response expectations
  (`ResponseEventType` as a list) — v1 ships single-type only, extend if
  a real case asks, per this ADR's Decision. Business/calendar-day-aware
  windows (relevant to a regulatory clock, not to IONM) — `Within` is a
  plain `TimeSpan`; a deployment needing calendar-day semantics computes
  its own `DeadlineAt`-equivalent upstream of registering `Within`, not
  a framework concern this ADR takes on.
- **`08-build-plan.md` gains a named item** ("Expected-Response
  Tracking") for this capability, per `CLAUDE.md`'s standing "a new
  capability gets a named item" rule.
- **`docs/features/expected-response-tracking.md` is the worked
  example** — sequence diagrams for tracking/resolution/escalation, the
  ER diagram, an ops-facing Salt mockup, and the Gherkin scenarios
  `08-build-plan.md`'s new item is built against.
- **`docs/references.md` gains a row** for Correlation Identifier/
  Request-Reply (Hohpe & Woolf), landed in the same pass this citation is
  first used, per `.claude/protocols/verify-before-citing.md`.
