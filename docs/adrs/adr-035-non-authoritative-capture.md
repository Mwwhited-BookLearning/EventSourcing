[← ADR index](../07-adrs.md)

# ADR-035: Non-authoritative capture — `AuthorityStatus` as a trust axis independent of `SchemaStatus`

Status: Accepted

Context: `ADR-023`'s persist-everything posture already covers *shape*
problems (`SchemaStatus`) — an unrecognized or invalid payload is
persisted and flagged, never rejected. There's a separate, genuinely
different question: can the *submitter's authority* to make this claim
be verified at capture time at all? A field actor working offline, or any
identity whose permission can't be checked synchronously, still needs to
be captured — `docs/design-docs/12` names this precisely, and this design
adopts it directly.

Decision:
- **`AuthorityStatus` is a second, independent trust axis** — schema
  status answers "do we understand the shape of this," authority status
  answers "do we trust who submitted it and what they claim to be
  doing." Both advisory, both non-blocking, both travel with the event
  (`docs/data/event-log.md` — `AttestedActorId`, `AttestedClaims`,
  `AuthorityStatus`, `AuthorityDecisionRef`, new fields on `StoredEvent`).
  Neither ever gates `Status`, per `ADR-023`.
- **Lifecycle**: `unattested → pending_review → accepted | rejected`
  (or `unattested` directly to `accepted`/`rejected` on immediate
  confirmation). An `unattested` event is folded into the Entity Store,
  replicated (`ADR-033`), and queryable — identically to an `accepted`
  one; the only difference is a label.
- **Accept/reject decisions are new events, never mutations** — an
  `authorityDecision` event (`targetEventId`, `decision`,
  `decidingActorId`, `reason`), the same "corrections are additive"
  principle this design applies everywhere else (`ADR-009`'s closing
  note, `ADR-024`'s conflict handling). `AuthorityDecisionRef` on the
  original event is a denormalized back-pointer set by the fold step for
  query convenience, never a correction of history.
- **Rejection is annotate-only by default, per-event-type overridable to
  compensating-patch** — see
  [`docs/comparisons/authority-rejection-behavior.md`](../comparisons/authority-rejection-behavior.md)
  for the full comparison. `RejectionBehavior` (`Annotate` | `Compensate`)
  becomes a field on `EventTypeDefinition`
  (`docs/data/schema-registry.md`), alongside `ChangeKind`.
- **`AttestedClaims` gets its own lightweight schema-registry entry**
  (an `attestation` entity type, `AppId`-scoped like everything else,
  `ADR-030`) rather than remaining an untyped blob forever — claims
  evolve the same additive, versioned way as everything else in this
  design.

Consequences:
- This is what makes `ADR-036` (DID/UCAN) meaningful — without a real
  trust axis to populate, self-attested credentials would have nowhere
  to attach. `AttestedClaims`/`AuthorityStatus` are the concrete fields
  that ADR's JWT claim shape maps onto.
- View definitions (`ADR-039`, the MVVM client) should
  render `unattested`/`pending_review` data with a visual indicator,
  reusing the same generic "flag" rendering convention `ADR-024`'s
  `ConflictFlag` already established — not a bespoke one per concern.
- Replication (`ADR-033`) treats an unattested event as a full peer-sync
  citizen — authority review is a downstream, per-server (or centrally
  reviewed) concern, not a sync precondition. Two servers can
  independently disagree about whether something's been reviewed,
  resolved the same way as any other divergence (`ADR-024`'s
  `ConflictFlag`, reused, not a new mechanism).
