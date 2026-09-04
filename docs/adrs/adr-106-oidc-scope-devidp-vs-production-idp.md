[← ADR index](../07-adrs.md)

# ADR-106: OIDC/OAuth2 scope — Duplex validates against any compliant IdP; `EventStore.DevIdp` deliberately stays client-credentials-only

Status: Accepted

Context: `TODO.md` asked what "full OIDC+OAuth2" means for this
framework. Today `EventStore.DevIdp` implements `client_credentials` +
RFC 8693 Token Exchange only — no `authorization_code`/PKCE, no ID
tokens, no interactive login (`src/EventStore.DevIdp/Program.cs:133,
143,279-280`). `ADR-105` decided the authorization *model* (generalized
roles as JWT claims, per-application permission expansion) but left the
authentication *flow* scope — real interactive human login vs.
machine-only credentials — undecided.

Decision:
- **The resource-server side (every `EventStore.Host.<Provider>`,
  `EventStore.GraphQL`, etc.) already supports "full OIDC/OAuth2" in the
  sense that actually matters, and this is now stated as a real
  decision, not left implicit**: JWT-bearer validation (`ADR-006`)
  authenticates against a standard OIDC discovery document
  (`options.Authority`) and a `scope`/claims check — it never hardcodes
  which grant type produced the token. Any real, compliant external IdP
  (Okta, Auth0, Entra ID, Keycloak) doing full `authorization_code`+PKCE
  with real interactive login and real ID tokens plugs in today with
  **zero framework changes** — confirmed by inspection, not assumed:
  nothing in `HostCoreExtensions.AddEventStoreCommonServices` or any
  downstream claim check is DevIdp-specific.
- **`EventStore.DevIdp` deliberately stays `client_credentials` +
  Token-Exchange only — not expanded into a real interactive OIDC
  Provider.** It is dev/demo tooling standing in for a real IdP
  (`ADR-006`'s own original framing: "the production IdP remains a
  separate, later decision, out of scope for this POC"), not the
  production answer. Building real login UI, session management, and
  PKCE into `EventStore.DevIdp` would be exactly the kind of POC scope
  creep this project has repeatedly declined elsewhere this session (the
  CI/CD pipeline, `ADR-091`) — a real deployment swaps `DevIdp` for a
  real IdP, which the resource-server side already supports doing with
  no code change, per the bullet above.
- **Adopt RFC 7591/7592 (OAuth 2.0 Dynamic Client Registration/
  Management) for `EventStore.DevIdp` specifically** — an application
  self-registers via API instead of `DevIdpSeeder.cs`'s current
  hardcoded C# client list. This is worth building even for a POC,
  unlike interactive login: it directly serves `ADR-105`'s "application
  scope as first-class" premise, and is a small, genuinely useful
  addition to dev tooling regardless of whether `DevIdp` ever becomes
  more than that.
- **Adopt RFC 8414 (OAuth 2.0 Authorization Server Metadata)** alongside
  the OIDC Discovery document `ADR-006` already exposes — the OAuth-only
  generalization of the same idea, for a caller that only speaks OAuth2
  and doesn't want to assume the OIDC layer on top. Both real, verified
  standards (confirmed this session, not guessed).

Consequences:
- **Resolves `TODO.md`'s OIDC/OAuth2 scope item.** No ID tokens, no
  `authorization_code`/PKCE, and no interactive login are being added to
  `EventStore.DevIdp` — this is a deliberate scope decision, not an
  oversight left for later. If a real interactive-login *demo* is
  wanted, the answer is standing up a real (even free-tier) OIDC
  Provider pointed at this framework's own existing, unmodified
  resource-server side — not building one into `DevIdp`.
- `docs/references.md` gains RFC 7591/7592/8414 as adopted rows (already
  verified this session, cited here for the first time as adopted
  rather than merely "found").
- `DevIdpSeeder.cs`'s hardcoded client array becomes real code work once
  RFC 7591/7592 is actually built — out of scope for this design-phase
  session, tracked as future implementation, not decided further here.
- Does not change `ADR-105`'s own decision at all — that ADR's
  generalized-role/permission-expansion model is authentication-flow-
  agnostic by construction: it only cares that a JWT arrives with role
  claims, never how the subject authenticated to get one.
