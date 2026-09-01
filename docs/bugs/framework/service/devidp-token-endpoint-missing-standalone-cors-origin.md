[← Bugs index](../../../changes/2026-08-31.md)

# DevIdp's `/connect/token` endpoint had no CORS allow-list entry for standalone client-web dev

**Scope**: `framework` · **Tier**: `service`

## What was wrong

Running `client-web` standalone (`npm run dev:vitals`, no `AppHost`)
against a manually-started `EventStore.DevIdp`, every feature that needs
a token failed at page load with a real browser console error:

```
Access to fetch at 'http://localhost:5799/connect/token' from origin
'http://localhost:5173' has been blocked by CORS policy: Response to
preflight request doesn't pass access control check: No
'Access-Control-Allow-Origin' header is present on the requested resource.
```

followed by an unhandled `TypeError: Failed to fetch` inside
`authClient.ts`'s `fetchToken`, breaking `App.vue`'s own mounted-hook
subscription — this affects every client-web view in standalone mode,
not just the new `/tasks` route being verified at the time.

## How and where it was found

Found while live-verifying the new `MyTasksView.vue`/`useMyTasks.ts`
(ADR-101) through a real headless Chromium (Playwright, driven from a
throwaway `scratchpad/PlaywrightCheck` console app referencing the
already-installed `Microsoft.Playwright` package) against a real Vite
dev server (`npm run dev:vitals`) pointed at manually-started
`EventStore.DevIdp`/`EventStore.Host.Sqlite` processes — exactly the
"run the real thing in a browser" verification this repo's own standing
rule requires for UI changes. `EventStore.Host.Sqlite`'s own CORS
allow-list already covered this: `appsettings.Development.json` has
`"Cors": { "AllowedOrigins": ["http://localhost:5173"] }`.
`EventStore.DevIdp`'s `appsettings.Development.json` had no `Cors`
section at all.

## Root cause

`EventStore.DevIdp/Program.cs` already implements the correct,
config-driven CORS mechanism (`Cors:AllowedOrigins`, plus a real,
previously-fixed `CorsBeforeOpenIddictStartupFilter` ensuring
`UseCors()` runs ahead of OpenIddict's own request interception — see
that file's own header comment on a prior session's fix for the ordering
half of this same class of problem). Only the *configuration value* was
missing: nothing ever populated `Cors:AllowedOrigins` for the standalone
dev origin, unlike `EventStore.Host.Sqlite`'s own
`appsettings.Development.json`, which already lists
`http://localhost:5173`. Every prior CORS verification of DevIdp's token
endpoint ran through `EventStore.AppHost`, whose `WithEnvironment` calls
inject `Cors:AllowedOrigins__N` dynamically per Aspire-assigned
`client-web-*` origin — that path was never affected. The standalone
(no-`AppHost`) path, which `client-web/packages/reference-app/.env`'s
own header comment explicitly documents as supported ("run both
directly (`dotnet run` in each project) for this file's own defaults to
actually resolve"), was simply never exercised against a real browser
until this pass.

## Resolution

`src/EventStore.DevIdp/appsettings.Development.json`: added
`"Cors": { "AllowedOrigins": ["http://localhost:5173"] }`, mirroring
`EventStore.Host.Sqlite/appsettings.Development.json`'s existing entry
exactly.

**Regression test**:
`EventStore.IntegrationTests.AuthScenarioAssertions.DevIdpTokenEndpointGetsCorsHeadersForAnAllowedOrigin`
(a new `Preflight(client, origin, path)` overload generalizes the
existing `Preflight` helper, previously hardcoded to `/publish/whatever`,
to accept `/connect/token`), called from `AuthSqliteTests.AllAuthScenarios`
— sends a real CORS preflight `OPTIONS /connect/token` against a
`WebApplicationFactory`-hosted DevIdp and asserts an allow-listed origin
gets `Access-Control-Allow-Origin` back while a disallowed one does not.
Verified live end to end afterward too: restarting `EventStore.DevIdp`
with the fixed config, then re-running the same Playwright script against
the same standalone Vite dev server, produced zero CORS console errors
(the run then surfaced the separate, already-documented
`devidp-duplicate-typed-extra-claims-collapse.md` bug instead, confirming
this fix actually cleared the CORS failure rather than just moving it).

No ADR update: a missing dev-environment config value for an
already-decided, already-implemented mechanism (`ADR-014`'s deny-by-default
CORS posture), not a new decision.
