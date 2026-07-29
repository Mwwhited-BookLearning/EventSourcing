[← ADR index](../07-adrs.md)

# ADR-060: Outbound webhook/notification support — reuses the durable outbox primitive, Standard Webhooks-shaped signing

Status: Accepted

Context: `docs/extensibility-points.md`/`ADR-059` covers *inbound*
(local) extensibility; direction received this session names a second,
distinct need — **outbound** notifications: a hosting team registers a
webhook URL and this framework calls out to it when matching events
happen, rather than requiring every consumer to hold an open Follow
connection (`ADR-010`). Searched prior art before designing a bespoke
mechanism, per this project's standing convention: the [Standard
Webhooks specification](https://github.com/standard-webhooks/standard-webhooks/blob/main/spec/standard-webhooks.md)
(backed by Svix, Zapier, and others) defines exactly the header/signing/
retry shape most real webhook systems (Stripe, GitHub, DocuSign) already
converge on independently — adopted here rather than inventing a
fourth signing convention.

**`CLAUDE.md`'s standing instruction applies directly**: "if a third
outbox-shaped mechanism ever gets introduced, re-check that it actually
inherits [fault/abend/restart tolerance], don't assume it does by family
resemblance alone." `ADR-033`'s peer-sync outbox/inbox and `ADR-039`'s
client-local outbox are the first two; this is the third, checked below.

Decision:
- **A new `WebhookSubscription`** (per `AppId`, `ADR-030`): target URL, a
  signing secret, the event type(s)/entity type(s) it wants notified
  about, and — reusing `ADR-009`'s own precedent exactly — **a fixed
  claim set computed once at registration time**, the same "claims fixed
  for the lifetime of one Follow connection" rule `ADR-009` already
  states, applied here to a subscription's lifetime instead of a
  connection's. Every payload this subscription is ever sent is masked
  against that fixed claim set (`IPayloadMasker`, unchanged) — a webhook
  target is an external party by definition and must never receive an
  unmasked field its subscription's claims don't cover.
- **Delivery reuses the exact same durable outbox/inbox primitive
  `ADR-023`'s client Inbox, `ADR-033`'s peer sync, and `ADR-039`'s client
  outbox already share** — a matching event enqueues into a durable
  `WebhookOutbox` table (not an in-memory queue — nothing queued is ever
  only in memory, the same fault/abend/restart-tolerance test `ADR-033`
  states explicitly), and a `WebhookDeliveryCursor { SubscriptionId,
  LastDeliveredSequenceNumber, LastAttemptAt, LastSuccessAt }` tracks
  resumption — structurally identical to `ADR-033`'s `PeerSyncCursor`,
  confirming this really does inherit the primitive rather than merely
  resembling it.
- **Signing and headers follow Standard Webhooks directly**: `webhook-id`
  (delivery identifier, doubles as the idempotency key), `webhook-
  timestamp`, and `webhook-signature` (HMAC-SHA256 over
  `{id}.{timestamp}.{payload}`, using the subscription's own secret) —
  the same "sign so a party that can't otherwise verify a request can
  trust it" reasoning `ADR-040`'s ticket-exchange HMAC already
  established for a different direction (that one client-signs a URL
  *inbound*; this one signs a payload *outbound* — same primitive,
  opposite direction, not a second signing convention invented).
- **At-least-once delivery, retried with exponential backoff + jitter**
  (Standard Webhooks' own recommendation) — never at-most-once, matching
  `README.md`'s "never lose data" governing principle. The receiving
  party is responsible for idempotent handling keyed on `webhook-id`,
  the same Idempotent Receiver discipline (`ADR-011`) this framework
  already asks of *its own* publish endpoint, now stated as guidance for
  a webhook consumer instead.
- **Exhausted retries dead-letter as an ordinary event, not silent
  failure** — a reserved `WebhookDeliveryFailed` event, published back
  into the subscribing tenant's own Event Log (the same "make the
  failure an inspectable record" posture `ADR-020`'s `EventUpcastFailed`
  already established for a different failure kind), so a delivery
  failure is queryable/alertable through the framework's own ordinary
  mechanisms rather than only visible in an operator's logs.

Consequences:
- Resolves the outbound half of the generalized-framework review's
  extensibility finding (`docs/10-open-questions.md`).
- **Honest limitation, stated rather than glossed over**: once a payload
  is delivered to a webhook target, this framework has no further control
  over that copy. If `ADR-057`'s crypto-shredding later erases a field,
  an *already-delivered* webhook payload sent before erasure is not
  reachable — the same limitation Verraes' crypto-shredding write-up
  already names generally ("doesn't prevent consumers from storing
  encrypted data... or deriving new sensitive values"), now concretely
  real for this framework's own outbound surface. A *retried* delivery
  attempted after erasure correctly carries `{"erased": true}` — only
  copies already sent before the erasure are the exposure.
- `docs/data/schema-registry.md` gains `WebhookSubscription`; a new data
  group file (or `schema-registry.md`'s own group, since a subscription
  is tenant/registry-adjacent configuration, not event/entity data) holds
  `WebhookOutbox`/`WebhookDeliveryCursor` — not yet placed this pass,
  flagged as remaining propagation work.
- No new signing mechanism invented — Standard Webhooks' HMAC-SHA256
  construction and header names are used as specified, not reinvented
  loosely "in the spirit of."
