[← ADR index](../07-adrs.md)

# ADR-023: Persist-everything ingestion posture (supersedes reject-on-invalid framing in `ADR-011`/`ADR-013`/`ADR-020`)

Status: Accepted — a deliberate posture change, not an accident of
integrating a second design. Chosen explicitly over keeping EventSouring's
original reject-on-invalid behavior as the default.

Context: Every publish decision made so far in this design rejects
outright on failure: schema-invalid payloads are `400` (`ADR-013`'s
`validation-failed`), an unknown `schemaVersion` is `400`
(`ADR-020`), a failed upcast produces a dead-letter event **but the
underlying philosophy was still "this publish either succeeds as the
intended type, or something clearly went wrong."** The second design
package's governing rule is stronger and different in kind: **never let
unrecognized, unverified, or currently-unroutable data block, delay, or
corrupt anything else — persist first, always; understand, validate,
authorize, and reconcile as separate, non-blocking, eventually-completed
steps** (`docs/design-docs/01 §1.2`). This is a real, considered
trade-off — reviewed and adopted deliberately, not defaulted into.

Decision:
- **Every syntactically-parseable publish request is persisted.** The
  distinction that used to be "valid → `201`, invalid → `400`" becomes
  "transport-usable → persisted with a status, transport-unusable →
  the only real rejection left." Concretely:
  - Cannot be parsed as the envelope shape at all (not valid JSON, missing
    required transport fields like `entityId`/`correlationId`) → `400`,
    genuinely rejected — nothing to persist, there's no event to append.
  - Everything else — unknown `schemaVersion`, payload violates the
    known schema, unresolvable `EntityId`, upcast failure, missing
    authority proof (`ADR-035`) — is **persisted**, not rejected.
- The publish response becomes **`202 Accepted`** (not `201 Created`) with
  a status envelope, replacing this ADR's predecessors' response shapes:

  ```json
  {
    "correlationId": "018f2a1e-...",
    "status": "received",
    "entityId": null,
    "schemaStatus": null,
    "authorityStatus": "accepted",
    "reason": null,
    "timestamp": "2026-07-29T14:32:00Z",
    "sequenceNumber": 48213,
    "originId": null
  }
  ```

  **`sequenceNumber`/`originId` added by `ADR-090`** — lets a caller
  explicitly filter a later read for "has this site applied everything
  up through this point," the mechanism this design uses instead of a
  frontier-token abstraction. `originId` is `null` for a single-site
  deployment; populated once `ADR-033`'s multi-site mesh is active.

  | `status` | Terminal? | Meaning |
  |---|---|---|
  | `received` | No | Persisted, not yet routed/folded |
  | `processing` | No | Picked up by the router, in flight (only surfaced if meaningfully slow) |
  | `applied` | Yes | Folded into the Entity Store (`ADR-021`), `entityId` populated |
  | `rejected` | Yes | Transport/structurally unusable — the one case above; never used for schema-invalid or unattested content |

- **`SchemaStatus` (`unknown` \| `invalid` \| `conformant`) becomes
  non-gating, advisory metadata that rides alongside `status`, never
  forcing it to `rejected`.** This directly supersedes `ADR-013`'s
  `validation-failed`/`400` row and `ADR-020`'s `unknown-schema-version`/
  `400` row — both of those situations are now `202` + a `SchemaStatus`
  flag, not a `400`. `ADR-018`'s `EventUpcastFailed` dead-letter event is
  **reframed as one instance of this general posture**, not a special
  case invented on its own: an upcast failure is exactly a `SchemaStatus:
  invalid` situation like any other, persisted and flagged, not a
  uniquely-shaped exception to the reject-by-default rule everything
  else still followed.
- **Known properties still apply even when other parts of the same
  payload are unrecognized** — a payload that's half-valid still folds
  its recognized fields normally (`ADR-022`'s `Extensions`-bag routing
  for unknown properties is the concrete mechanism; this ADR is what
  removes the reason to reject the *whole* payload over one bad field).
- **The router, not the inbox endpoint, owns validation.** Persistence
  (`received`) happens synchronously in the publish request; schema
  matching, entity resolution, and upcast validation happen afterward,
  asynchronously, exactly mirroring `docs/design-docs/04`'s inbox/router
  split. This is a genuine architectural addition, not just a response-
  shape change — `PublishEndpoint` (`06-solution-structure.md`) splits
  into an `InboxEndpoint` (append-only, always succeeds if parseable) and
  a background `Router` (does everything `PublishEndpoint` used to do
  inline).

Consequences:
- **This is a deliberate reversal of `ADR-013`'s `validation-failed` and
  `ADR-020`'s `unknown-schema-version` rows** — both are struck from the
  Problem Details table (`ADR-013`) as publish-time errors; they become
  `SchemaStatus` values on an accepted `202` instead. `ADR-013` otherwise
  stands unchanged (auth/scope/claim failures, `409` idempotency
  conflicts, and genuinely malformed envelopes are still real errors).
- **Idempotency (`ADR-011`) is otherwise unaffected in mechanism** —
  `eventId`/`correlationId` uniqueness and `PayloadHash` comparison still
  work exactly as designed; only the *response* on a fresh, schema-invalid
  submission changes (was `400`, now `202` + `SchemaStatus: invalid`).
- **Consumers must now check `SchemaStatus`/`AuthorityStatus` themselves**
  if they care whether data is "clean" — nothing in the store enforces
  that for them anymore at the publish boundary. This trades ingestion
  reliability (nothing is ever lost to a rejected publish) for a real
  shift of responsibility onto readers — the store's own posture is now
  "never lose an inbound message," not "only store good data."
  `ADR-037`'s GraphQL layer surfaces both statuses as ordinary, filterable
  fields, matching this trade explicitly rather than hiding it.
- Existing feature-doc Gherkin scenarios that assert `400` for a
  schema-invalid publish (`features/publish-event.md` and others) need
  rewriting to assert `202` + `SchemaStatus: invalid` instead — flagged as
  a real cross-doc follow-up, same category of acknowledged debt as
  `ADR-020`'s `schemaVersion`-required breaking change.
- This does **not** change `RequiredPublishClaim`/scope enforcement
  (`ADR-006`/`ADR-008`) — those remain real, blocking `401`/`403`s. This
  posture is specifically about *content* (shape, schema, authority-of-
  claimed-identity), not about whether the caller is allowed to call the
  endpoint at all. A caller with no `events:publish` scope still never
  gets to persist anything.
