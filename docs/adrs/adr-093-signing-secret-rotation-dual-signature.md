[← ADR index](../07-adrs.md)

# ADR-093: `ADR-040`/`ADR-060`'s signing secrets support a current+previous pair with dual-signature emission; rotation cadence stays ops-configurable

Status: Accepted

Context: `docs/10-open-questions.md` row 16 asked whether `ADR-040`'s
ticket-signing secret and `ADR-060`'s webhook-signing secret should each
become a current+previous pair, so `ADR-060` can emit the dual
signatures the Standard Webhooks spec it already adopted explicitly
supports for zero-downtime rotation. Raised as a possible pure ops/
configuration question this session — resolved by separating the two
halves it actually contains: **whether the schema/code can support a
rotation window at all is a real capability decision (this ADR); when
and how often to actually rotate is ops-configurable, unchanged.**

Decision:
- **Both `WebhookSubscription.SigningSecret` and the ticket-exchange
  shared secret become a current+previous *pair*, not a single value**
  — a schema change, not a configuration-only fix. Without this, no
  amount of operational discipline can achieve zero-downtime rotation:
  a single-secret field forces a choice between breaking in-flight
  verifications on the old key or never rotating at all. This is exactly
  why the question isn't purely ops — an ops team can't configure their
  way around a schema that only holds one value.
- **`ADR-060`'s webhook dispatcher emits dual signatures during an
  overlap window**, using Standard Webhooks' own already-adopted,
  already-supported mechanism for exactly this (multiple simultaneous
  `whsec_`-shaped keys, each producing its own signature in the
  `webhook-signature` header) — no new signing mechanism, using what
  `ADR-060` already committed to more fully.
- **The ticket-exchange HMAC verifier accepts a signature against either
  the current or previous secret** during the same overlap window — the
  read-side counterpart to the dual-signature emission above, using the
  identical HMAC-SHA256 convention `ADR-040` already established.
- **The rotation cadence/schedule itself stays ops-configurable, not a
  framework decision** — how often to rotate, how long the overlap
  window lasts before the previous secret is discarded, is deployment
  policy, the same shape `ADR-058`'s rate limits and `ADR-077`'s feature
  flags already take (framework supplies the mechanism, deployment
  supplies the values).

Consequences:
- `docs/data/schema-registry.md`'s `WebhookSubscription` and whichever
  entity holds the ticket-exchange secret gain a `Previous*` field
  alongside the current one — propagation work, same pass per this
  project's data-model-ownership convention where the entity already
  has a home; not yet done for the ticket-exchange secret specifically,
  since it doesn't yet have one.
- Resolves `docs/10-open-questions.md` row 16 — the last remaining row
  this session.
