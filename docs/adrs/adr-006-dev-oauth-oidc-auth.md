[← ADR index](../07-adrs.md)

# ADR-006: Dev-mode OAuth2/OIDC bearer-token auth via an in-process OpenIddict host, orchestrated with .NET Aspire

Status: Accepted — OpenIddict confirmed as the dev/POC provider.

Context: All four API surfaces (Publish, Follow, Lineage, Registry) are
currently unauthenticated. The three system actors (Publishing System,
Consuming System, Platform Operator) are automated services, not
interactive users, so machine-to-machine token acquisition is the natural
fit rather than an interactive login flow. For local development and this
POC, standing up a real OIDC provider by hand is pure overhead — but a full
IdP like Keycloak (JVM, admin console, realm database) is more machinery
than a `client_credentials`-only, no-human-login POC actually needs.

Decision:
- Authentication: OAuth2 **Client Credentials** grant (RFC 6749 §4.4)
  against an OIDC provider; every API request carries `Authorization:
  Bearer <JWT>` per RFC 6750's Bearer Token Usage. APIs
  validate the token via standard JWT-bearer middleware against the
  provider's OIDC discovery document (`Authentication:Authority` config
  value) — no custom token-validation code.
- Dev/POC provider: **OpenIddict**, hosted in-process in a new, small
  ASP.NET Core project, `EventStore.DevIdp` — not a separate off-the-shelf
  container. It uses OpenIddict's EF Core **InMemory** store, seeded at
  startup by a few lines of C# with the three clients
  (`publisher-client`, `follower-client`, `operator-client`) and their
  scopes — no realm-export JSON, no admin console, no persistent identity
  database to provision. Token endpoint is `/connect/token`; the standard
  `/.well-known/openid-configuration` discovery document is exposed so
  every `EventStore.Host.<Provider>`'s shared JWT-bearer validation
  (`EventStore.Host.Core`) needs zero OpenIddict-specific code. This is a
  dev-only choice — pointing `Authority` at a production IdP
  (Entra ID, Auth0, Keycloak, Duende IdentityServer, etc.) requires no code
  change, only configuration, since validation is generic OIDC.
- Authorization: one policy per required scope
  (`events:publish`, `events:follow`, `events:lineage:read`,
  `registry:admin`), mapped 1:1 to the endpoints in `03-api-contracts.md`.
  `/openapi.json` and `/asyncapi.json` remain anonymous — they expose
  contract shape only, never event data.
- Local multi-service orchestration: a new `EventStore.AppHost` (.NET
  Aspire) project wires whichever single `EventStore.Host.<Provider>` it
  targets (per `ADR-001` — the AppHost picks one, there's no runtime
  `Database:Provider` switch) together with that provider's database
  container and `EventStore.DevIdp` — as an Aspire **project** resource
  (`AddProject<Projects.EventStore_DevIdp>`), not a container resource,
  since it's just another .NET project in the same solution — injecting
  connection strings and the OIDC `Authority` via Aspire service discovery.
  A `docker-compose.yml` at the repo root provides an equivalent path for
  tooling that doesn't run the Aspire CLI (e.g. CI); both the chosen
  `EventStore.Host.<Provider>` and `EventStore.DevIdp` are built as
  ordinary app images there, with no third-party image or volume-mounted
  config to manage.

Consequences:
- No user-interactive login flow is implemented or needed for v1 — all
  three actors use `client_credentials`, keeping the auth surface small.
- Scope-based authorization needs a custom `IAuthorizationHandler`
  (`ScopeRequirement`) rather than a bare `RequireClaim`, since OAuth2
  `scope` is a single space-delimited string claim, not a repeated claim —
  a naive `RequireClaim` check silently fails to match a token carrying
  multiple scopes.
- ~~The browser `EventSource` API cannot set an `Authorization` header, so
  the Follow API must additionally accept the bearer token via an
  `access_token` query-string parameter for browser-based followers.~~
  **Superseded by `ADR-012`**: Follow moved from `GET` to the HTTP `QUERY`
  method specifically for its OData query capabilities, which as a side
  effect rules out `EventSource` entirely (it can only issue `GET`) —
  browser clients now use `fetch()`, which sets a real header, so this
  workaround (and the query-string-token leakage risk it carried) no
  longer exists for Follow at all.
- **Plain bearer tokens are usable by anyone who possesses them (RFC
  6750's own stated risk)** — this ADR doesn't address that on its own;
  `ADR-017` hardens every token this identity provider issues into a
  DPoP-bound one (RFC 9449), closing that specific gap without changing
  anything about the grant type or client model decided here.
- Client/scope seeding lives in `EventStore.DevIdp`'s own startup code, not
  a committed realm-export file — simpler than Keycloak's JSON import, but
  means the seed data is C#, not declarative config; keep it in one place
  (a single `DevIdpSeeder` class) so it doesn't drift from
  `03-api-contracts.md`'s scope table.
- Using an EF Core InMemory store means `EventStore.DevIdp` has **no
  persistence** — every restart re-seeds from scratch. That's the right
  trade for a dev/POC token issuer (nothing about it should be treated as
  durable state) but would need revisiting (a real database) if this ever
  became more than throwaway dev infrastructure.
- No admin console exists to eyeball the seeded clients (unlike Keycloak) —
  verify the seed via the discovery document / a token request, not a UI.
- Aspire changes *how the process is launched and wired* (connection
  strings, `Authority`, service discovery) — it does not change the
  per-deployment provider build from `ADR-001`, which still determines the
  DbContext/migrations wiring exactly as described there, independent of
  whether Aspire or plain `docker run` launched the process.
