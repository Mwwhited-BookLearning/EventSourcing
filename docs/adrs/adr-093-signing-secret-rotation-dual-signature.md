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
- ~~The ticket-exchange HMAC verifier accepts a signature against either
  the current or previous secret during the same overlap window — the
  read-side counterpart to the dual-signature emission above, using the
  identical HMAC-SHA256 convention `ADR-040` already established.~~
  **Corrected, later pass — the premise below this rests on was never
  actually verified before being written down, and turned out to be
  false**: `OpenIddictApplicationDescriptor.ClientSecret` is a single
  string per registered application, not a collection. Searched
  OpenIddict's own documentation, source, and issue tracker
  (`openiddict/openiddict-core`) for any built-in "multiple
  simultaneously-valid secrets per client" mechanism — found none. Real
  zero-downtime rotation for an OpenIddict-registered client requires
  either registering a second client application as a temporary stopgap,
  or a custom event handler overriding the default credential-validation
  pipeline to also check a locally-stored previous secret — genuine
  framework-level work, not "no framework change needed" as this ADR
  originally claimed. **Descoped, not built this pass** (build-plan item
  40, per direct user decision after this was found): only the webhook
  half of this ADR (below) is built. The ticket-exchange HMAC-rotation
  mechanism remains open design work — see `TODO.md`.
  **Built, later pass**: option (a) above, a real pipeline-level
  credential-validation override — see this ADR's own Consequences for
  the concrete mechanism and where it lives. Verified by actually running
  it, which is also how a SECOND wrong assumption surfaced mid-build: the
  application-code-level check
  (`applicationManager.ValidateClientSecretAsync`, called explicitly
  inside `/connect/token`'s own token-exchange branch) is never actually
  reachable for a rotated-away secret, because OpenIddict's own built-in
  `ValidateClientSecret` handler already rejects it (`ID2055`)
  unconditionally for EVERY grant type reaching this endpoint, including
  Token Exchange, before that branch's own code ever runs — a real
  handler override inside OpenIddict's own `ValidateTokenRequestContext`
  pipeline stage was genuinely required, not merely a nice-to-have; a
  purely application-level fallback check (tried first, and initially
  believed sufficient) never gets the chance to run.
- **The rotation cadence/schedule itself stays ops-configurable, not a
  framework decision** — how often to rotate, how long the overlap
  window lasts before the previous secret is discarded, is deployment
  policy, the same shape `ADR-058`'s rate limits and `ADR-077`'s feature
  flags already take (framework supplies the mechanism, deployment
  supplies the values).

Consequences:
- `docs/data/schema-registry.md`'s `WebhookSubscription` gains a
  `PreviousSigningSecret` field alongside `SigningSecret` — done.
- **Correction, checked against `ADR-040`'s own Consequences before
  assuming a new entity was needed**: this ADR originally said "whichever
  entity holds the ticket-exchange secret gain a `Previous*` field... not
  yet done, since it doesn't yet have one" — implying a persisted entity
  still needed creating. It doesn't. `ADR-040`'s own Consequences already
  settle where the shared secret lives: either the caller's
  already-registered OAuth2 `client_secret` (`ADR-006`) — DevIdp-side
  state, outside `EventStoreContext` entirely, the same "identity/token
  state lives in `EventStore.DevIdp`'s OpenIddict store, not the event
  store's database" convention `auth.md`'s own Data model section already
  states — or a caller-generated `one_time_secret`, which by design is
  used for exactly one ticket and never persisted at all, so "current vs.
  previous" doesn't apply to it any more than it would to a nonce.
  ~~This ADR's rotation mechanism still applies to the `client_secret`
  path — but as an instance of ordinary OAuth2 client-credential rotation
  (OpenIddict already supports a client holding multiple valid
  credentials), not a new field this design's own data model needs to
  invent. No entity added; none is needed.~~ **Corrected, later pass**:
  the "OpenIddict already supports multiple valid credentials" claim
  repeats the same unverified premise struck through in the Decision
  section above, and is equally false. No entity was added and none is
  needed for the schema question — that much is still correct — but the
  `client_secret` rotation path itself is NOT simply "ordinary OAuth2
  client-credential rotation" with no framework change; it needs real
  design work (a second registered application as a stopgap, or a
  custom credential-validation handler) not attempted this pass.
  **Built, later pass**: `EventStore.DevIdp`'s `ClientSecretRotationStore`
  (a DI singleton, in-process/non-persistent, same posture as `TicketStore`)
  tracks, per registered `clientId`, a CURRENT-secret override (once
  rotated at least once against this app instance) and a PREVIOUS secret
  with its own overlap-window expiry — no persisted entity, exactly the
  "outside `EventStoreContext` entirely" DevIdp-side state this
  Consequences section's own prior correction already named. A new
  `POST /oauth/clients/{clientId}/rotate-secret` admin endpoint updates
  OpenIddict's registered application to the new secret (via
  `IOpenIddictApplicationManager.UpdateAsync`, `PopulateAsync`+descriptor)
  and records the old one as the tracked previous value. The actual
  credential-acceptance mechanism lives in `.AddServer(...)`'s own
  `OpenIddictServerEvents.ValidateTokenRequestContext` pipeline stage (a
  handler registered ahead of OpenIddict's built-in `ValidateClientSecret`
  check, `SetOrder(int.MinValue)` — the same technique this file's own
  pre-existing `ValidateTokenContext` handler already used for a
  different purpose): if the presented secret matches an unexpired
  previous one, it transparently rewrites the request's `ClientSecret` to
  the current value before OpenIddict's own check ever runs — this ADR's
  option (a), a real credential-validation-pipeline override, not an
  application-code-level fallback (see this ADR's own struck-through note
  above on exactly why the latter alone doesn't work). `/oauth/introspect`
  gets the matching read-side fix: a signature that fails against the
  current secret is retried against the tracked previous one before being
  rejected, covering a ticket signed just before a caller's own credential
  refresh landed. `tests/EventStore.IntegrationTests/
  TicketExchangeSecretRotationHttpSqliteTests.cs` proves the overlap
  window, both grant-path and introspection-path acceptance, and that a
  secret that was never current or previous still gets rejected.
- Resolves `docs/10-open-questions.md` row 16 for the webhook half only
  — **corrected, later pass**: the ticket-exchange half is not actually
  resolved, since the mechanism its resolution rested on turned out not
  to exist. Re-opened as a `TODO.md` item rather than a
  `docs/10-open-questions.md` row, since the SCHEMA question (does a
  current+previous pair exist) is answered (no, and none is needed) —
  only the MECHANISM question (how does rotation actually work for an
  OpenIddict client) was still open. **Built, later pass** (see above) —
  the `TODO.md` item this note re-opened is now removed.
