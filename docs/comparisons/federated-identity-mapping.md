[← Comparisons index](README.md)

# Federated Identity Mapping: bare `sub` vs. composite (`iss`, `sub`) vs. full JIT provisioning

**Resolves `docs/10-open-questions.md`'s open row**: "How does an external
federated IdP's `sub` map to this framework's own `ActorId` for
`Role`/`UserPermission` lookup (`ADR-047`)? A simple `sub == ActorId`
convention would work for the common case but isn't mandated."

`ADR-047` deliberately left this open — it names the `sub == ActorId`
convention only as an example of *a* mapping that would work for the
common case, not as the decision. This comparison weighs that convention
against the real prior art for exactly this problem before mandating one.

**Stated requirement**: once `ADR-047`'s token exchange verifies an
externally-issued token against a registered `TrustedFederationIssuer`,
the framework needs *some* deterministic way to take that token's
identity claims and arrive at the `ActorId` used to look up `Role`
(`ADR-046`) and `UserPermission` (`ADR-046`) records for the target
`AppId` — a lookup that has to be **collision-free** (two different
external humans must never resolve to the same `ActorId`) and **stable**
(the same external human must always resolve to the same `ActorId`,
login after login).

## Prior art

- **OpenID Connect Core 1.0** defines `sub` as "a locally unique and
  never reassigned identifier **within the Issuer** for the End-User"
  ([openid.net/specs/openid-connect-core-1_0.html](https://openid.net/specs/openid-connect-core-1_0.html))
  — the uniqueness guarantee is explicitly scoped to one issuer, not
  global.
- **OpenID Connect Basic Client Implementer's Guide 1.0, §2.5.3** states
  this plainly, not as an inference: "The `sub` (subject) and `iss`
  (issuer) Claims, used together, are the only Claims that an RP can
  rely upon as a stable identifier for the End-User"
  ([openid.net/specs/openid-connect-basic-1_0.html](https://openid.net/specs/openid-connect-basic-1_0.html)).
  Every other claim (`email`, `name`, `preferred_username`) can change or
  be reused by the issuer over time; `sub` alone cannot be relied on
  across issuers; only the pair can.
- **SAML/OIDC Just-In-Time (JIT) provisioning** is the standard,
  widely-implemented pattern (Okta, Auth0, Oracle IAM, OpenIAM, and
  effectively every enterprise SSO product document this) for the
  adjacent question this comparison's Option C answers: what happens the
  *first* time a given external identity shows up, when no local record
  for it exists yet — automatically create/link a local identity record
  from the assertion's/token's claims, rather than requiring the local
  system to be pre-seeded with every federated user in advance.
- **SCIM (System for Cross-domain Identity Management), RFC 7643 (core
  schema) and RFC 7644 (protocol)** is the real, IETF-published standard
  for the *lifecycle* side of this problem — provisioning **and**
  deprovisioning identities across systems. Two parts of its schema are
  directly relevant here: the `active` boolean ("a value of `false`
  implies that the user's account has been suspended" — deactivation,
  not deletion, is the normal deprovisioning move) and `externalId` (an
  explicit, client-assigned field for holding *another* system's
  identifier for the same resource — precisely the "map an external key
  to a local record" shape this comparison needs, just for a push-based
  provisioning protocol rather than pull-based JIT).

## The options

### Option A — bare `sub == ActorId`

| | |
|---|---|
| **Pros** | Zero mapping infrastructure — no new table, no new write path. Correct in the common case `ADR-047` was written against: exactly one `TrustedFederationIssuer` registered per `AppId`. |
| **Cons** | **Directly contradicts OIDC's own guidance** — Core 1.0 scopes `sub`'s uniqueness guarantee to "within the Issuer," and the Basic Client Implementer's Guide says `sub` alone is *not* one of "the only Claims an RP can rely upon" for exactly this reason. `ADR-047`'s own `TrustedFederationIssuer` registry is a list, not capped at one entry per `AppId` — the moment a second issuer is registered (a company merger, adding a second SSO tenant, a customer bringing their own IdP), two different issuers' `sub` values can collide onto the same `ActorId`, silently merging two different people's `Role`/`UserPermission` records. That is a privilege-escalation-shaped bug, not a data-quality nuisance — person B's request would carry person A's permissions the instant a `sub` value repeats across issuers, e.g. two IdPs that both hand out small sequential integers as `sub`. |

### Option B — composite (`iss`, `sub`) as the real key

| | |
|---|---|
| **Pros** | This is what the spec itself names as the only claim combination an RP may treat as stable and unique — not a judgment call this design is making alone. Immediately safe for multiple `TrustedFederationIssuer` entries per `AppId`, which `ADR-047`'s schema already allows today. Extensible to future account-linking (the same human authenticating via two different IdPs) without a redesign, since the mapping is keyed by pair, not baked into `ActorId`'s own format. |
| **Cons** | Needs *something* to hold the pair — either `ActorId` itself becomes a composite/concatenated value (e.g. `"{iss}\|{sub}"`), which is spec-correct but couples the framework's own primary actor identifier to a specific external issuer's URL forever (awkward if that org ever migrates IdPs), or a separate lookup table is introduced, which is a real (if small) new entity, not just a naming convention. |

### Option C — full JIT provisioning + SCIM-shaped lifecycle

| | |
|---|---|
| **Pros** | Answers the question Option B alone doesn't: what happens on an external identity's *first* appearance, when no `Role`/`UserPermission` record exists for it yet. A framework-native `ActorId`, generated once and linked to the incoming (`iss`, `sub`) pair at first login, is issued rather than assumed pre-provisioned — the standard SAML/OIDC JIT shape, and it composes directly with Option B (the link table *is* the (`iss`, `sub`) → `ActorId` mapping). Borrowing SCIM's `active`-flag deprovisioning shape (deactivate, don't delete) gives a real place to hang "this federated identity should no longer resolve to any permissions" without inventing a bespoke revocation mechanism. |
| **Cons** | A real new write path at token-exchange time (first-seen detection + row creation), not just a keying convention. Full SCIM — a push endpoint the external IdP calls to proactively deprovision — is genuine additional infrastructure with no stated requirement driving it yet (nothing here currently needs *near-real-time* reaction to an external account's termination; `ADR-047`'s existing per-`AppId` `TrustedFederationIssuer` revocation already stops a whole issuer at the next token exchange, just not per-user). Building the full SCIM push protocol ahead of that requirement would be exactly the kind of unrequested mechanism this design's own conventions warn against building preemptively. |

## Recommendation

**Mandate the composite (`iss`, `sub`) as the real identity key (Option
B), realized as a new, identity-provider-scoped `FederatedIdentityMapping
{ AppId, Issuer, Sub, ActorId, CreatedAt }` record — populated
automatically via lightweight JIT provisioning (the mechanism half of
Option C) at `ADR-047`'s token-exchange step, but stopping short of full
SCIM push-based deprovisioning (the rest of Option C), since nothing has
stated a requirement for that yet.**

The deciding factor is not a preference — it is the OIDC Basic Client
Implementer's Guide's own explicit text: `sub` alone is never one of
"the only Claims that an RP can rely upon as a stable identifier,"
`iss`+`sub` together are. Option A is ruled out by the spec's own
guidance, not by this project's taste, and the risk it leaves open
(cross-issuer `sub` collision silently merging two people's permissions)
is exactly the shape of bug that guidance exists to prevent.

Concretely, at exchange time:
1. Look up `FederatedIdentityMapping` by `(AppId, Issuer, Sub)`.
2. If found, use its `ActorId` for the `Role`/`UserPermission` lookup
   `ADR-047` already specifies.
3. If not found (first-ever login for this external identity under this
   `AppId`), mint a new framework-native `ActorId`, insert the mapping
   row, and proceed — the JIT step. No pre-provisioning of every
   federated user is required in advance.

This keeps `ActorId` itself issuer-agnostic (never a concatenation baked
around one issuer's URL), directly enables the "same human, two IdPs"
account-linking case later without touching the mapping's shape, and
gives a natural home for a future `Active`-style deactivation flag
(SCIM's shape, not its protocol) if per-user deprovisioning ever becomes
a real requirement — without building the full SCIM push protocol now.

Per `ADR-046`'s own precedent for `UserPermission` and role-assignment
state, `FederatedIdentityMapping` is **identity-provider-scoped state,
not a schema-registry entity** — the core engine's `Role`/`UserPermission`
lookup only ever sees the resolved `ActorId`, unaware of whether it
arrived via `EventStore.DevIdp` directly or via this mapping.
