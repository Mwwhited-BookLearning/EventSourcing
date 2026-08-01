[← ADR index](../07-adrs.md)

# ADR-077: Instant feature-flag toggles via a chained, reloadable `IConfigurationProvider` — not a contradiction with `ADR-041`/`ADR-058`

Status: Accepted

Context: `docs/10-open-questions.md` flagged this as its highest-
priority row — an apparent internal contradiction between two already-
Accepted ADRs. `ADR-038` promises a bad rollout can be "disabled
instantly" via feature flags; `ADR-041` decides configuration "stays
`Microsoft.Extensions.Configuration`"; `ADR-058` leaves its own
rate-limit configuration source explicitly unsettled ("a build-time
detail, not a new design question this ADR needs to settle"). Read
together, these were interpreted as `IConfiguration` being necessarily
static (files/env vars/command line), which cannot toggle instantly with
no restart — the perceived contradiction. Resolved by direct design
conversation, this session: **the premise was wrong, not the ADRs.**

Decision:
- **Not a contradiction — `IConfiguration`'s provider model already
  supports live, no-restart change propagation.** Every
  `IConfigurationProvider` can implement `GetReloadToken()`; when a
  provider's backing data changes, it fires that token,
  `IConfigurationRoot` recomposes the merged configuration, and anything
  reading via `IOptionsMonitor<T>` (or a direct `IConfiguration` re-read,
  or `IOptionsSnapshot<T>` per request) sees the new value with no
  restart. The file provider already does this for `appsettings.json`
  locally (`reloadOnChange: true`); a custom provider backed by a live
  store does the same thing for a value that needs to change across an
  entire fleet, not just one process's local disk.
- **`ADR-041` already embraces exactly this pattern — this ADR applies
  it to flags, doesn't invent it.** `ADR-041`'s own secrets addendum
  chains live, network-backed providers (Key Vault, Vault) alongside the
  static ones for secrets, explicitly stating `Microsoft.Extensions.
  Configuration` "is built to chain multiple providers in one pipeline."
  A custom flag provider is the same composition, one more provider in
  the same chain.
- **Flag state is a reserved Event Log event, reusing `ADR-067`'s
  control-plane pattern — not a bespoke, unaudited admin table.** A
  `FeatureFlagSet` event (`ActorId`, `AppId`, flag key, value) is a
  reserved event type exactly like `ADR-067`'s `RoleGranted`/
  `SchemaRegistered`, hash-chained (`ADR-019`) for free. It folds into a
  small, current-state `FeatureFlagState` table — the same write/read
  split `ADR-067` already established for schema/RBAC/trust-root data —
  which a custom `EventLogFeatureFlagConfigurationProvider` reads from.
- **Propagation: short-interval polling against `FeatureFlagState`, no
  new push infrastructure.** `ADR-038`'s "instantly" means "no redeploy-
  rollback cycle," not sub-second — a default poll interval of a few
  seconds (configurable) satisfies that without adding a new dependency
  (Postgres `LISTEN`/`NOTIFY` is provider-specific and would break
  `ADR-004`'s portability; a push mechanism can be added later if a
  concrete need for sub-second propagation ever arises, but isn't
  justified today, consistent with `ADR-041`'s first-party/no-new-
  dependency preference).
- **Scope: flags are `AppId`-scoped, per `ADR-075`'s silo model.** A
  tenant's flag state, its folding event stream, and its
  `FeatureFlagState` table all live inside that tenant's own deployment/
  database, like everything else under the silo model — no cross-tenant
  flag state.
- **The flag/static-config boundary is exactly what `ADR-038` already
  calls a feature flag**: a gate on new schema/routing/view-definition
  behavior meant to be flippable without a redeploy. Connection strings,
  secrets, and deployment topology stay on `ADR-041`'s static providers
  unchanged. `ADR-058`'s tenant rate limits *may* use this same dynamic
  mechanism if a deployment wants no-restart limit changes — this ADR
  gives `ADR-058`'s previously-open config-source question a concrete,
  available answer without forcing it; `ADR-058`'s own "build-time
  detail" framing stands.

Consequences:
- **`FeatureFlagState` is defined in `docs/data/schema-registry.md`,
  landed in this same pass** per this project's data-model-ownership
  convention. The `FeatureFlagSet` reserved event type itself, and a
  `DbSet<FeatureFlagState>` registration in `docs/data/dbcontext-and-
  conventions.md`, remain not yet done — tracked in `TODO.md`'s existing
  data-model drift-table item.
- A new, small `EventLogFeatureFlagConfigurationProvider` component —
  not yet built.
- `ADR-058` is unaffected, not revised — this ADR only adds an available
  answer to a question `ADR-058` deliberately left open, it doesn't
  change `ADR-058`'s own decision.
- Resolves the design fork logged in `docs/changes/2026-07-31.md`
  (formerly `docs/10-open-questions.md` row 13).
