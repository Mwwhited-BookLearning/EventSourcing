[← ADR index](../07-adrs.md)

# ADR-014: CORS policy — configurable allowlist, deny by default

Status: Accepted

Context: A browser calling any of these APIs directly from a web page's
JavaScript is subject to CORS (the WHATWG Fetch standard's Cross-Origin
Resource Sharing protocol) — the *browser's* enforcement, not the
server's; it doesn't affect server-to-server calls at all. Nothing in the
design said which origins, if any, are allowed.

Decision:
- ASP.NET Core's built-in CORS middleware (implementing that same Fetch
  standard protocol), one named policy, wired in
  `EventStore.Host.Core` (`app.UseCors(...)`) so it's identical across all
  three `EventStore.Host.<Provider>` deployables (`ADR-001`).
- Allowed origins come from configuration (`Cors:AllowedOrigins`, a plain
  string array), not a hardcoded list — unlike the database provider
  (`ADR-001`), there's no reason this needs to be a build-time choice: it's
  an ordinary environment-varying setting with no NuGet/migrations
  implications, so runtime configuration is the right fit here.
- **Deny by default**: an empty/unset `Cors:AllowedOrigins` means no
  cross-origin browser call succeeds, for any origin. Server-to-server
  calls (the majority of this system's traffic — none of the three system
  actors are literally browsers) are entirely unaffected either way.
- The policy explicitly allows: the `Authorization` header (needed once a
  browser client uses `fetch()` with a real Bearer header instead of the
  old `access_token`-in-URL workaround — see `ADR-012`); and every method
  actually used, including `QUERY` (`ADR-012`) — a "non-simple" method
  that always triggers a browser preflight (`OPTIONS`) request, which
  ASP.NET Core's CORS middleware answers automatically once the method is
  listed, no extra code needed.
- `AllowCredentials()` is **not** set — auth is Bearer-token-in-header
  only, never cookies, so there's nothing that needs it, and leaving it
  off keeps the policy simpler (credentialed CORS has stricter rules
  around wildcard origins that don't need to apply here).

Consequences:
- Exact-string origin matching by default (ASP.NET Core's standard
  `WithOrigins(...)`); wildcard-port localhost matching for local dev (if
  wanted) needs `SetIsOriginAllowed(...)` with a predicate instead of the
  plain list — a small addition, not designed further here since it's a
  dev-convenience detail, not a behavioral decision.
- A fresh deployment with nothing configured is CORS-closed to every
  browser origin — safe-by-default, but means "why can't my browser client
  connect" is the first thing to check `Cors:AllowedOrigins` for.

**Corrected, later pass**: `EventStore.DevIdp` — a fourth deployable
this Decision's own text didn't name (it only lists "all three
`EventStore.Host.<Provider>` deployables") — independently carries its
own copy of this identical policy shape (same `Cors:AllowedOrigins`
config key, same deny-by-default posture, same header allow-list plus
`DPoP`), added once `client-web`'s browser calls to `DevIdp`'s own
`/connect/token` needed it (found only by actually driving a real
browser against it, this codebase's own repeated discipline). Not
duplicated by oversight — `DevIdp`'s CORS wiring also needs a
`CorsBeforeOpenIddictStartupFilter` (`src/EventStore.DevIdp/Program.cs`)
that `EventStore.Host.Core`'s three deployables never needed, since
OpenIddict's own token-endpoint middleware intercepts a request before
ASP.NET Core's ordinary `UseCors()` placement would ever run for it —
genuinely different enough plumbing that sharing `HostCoreExtensions`'
implementation outright wasn't a good fit, even though the *policy*
itself is identical. Found missing from this ADR's own closed
enumeration by a design-compliance audit.
