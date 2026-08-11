[← ADR index](../07-adrs.md)

# ADR-058: Per-tenant rate limiting via ASP.NET Core's built-in `RateLimiting` middleware

Status: Accepted

Context: `docs/10-open-questions.md` asked whether per-`AppId` rate
limiting/quota/backpressure should be added, and where — nothing today
stops one tenant's volume from starving every other tenant sharing a
deployment, given `ADR-023`'s persist-everything, always-`202` ingestion
posture and `ADR-030`'s multi-tenancy. Direction received this session:
"as standard as it comes in .NET/ASP.NET Core, the better."

`ASP.NET Core` has shipped first-party rate-limiting middleware since
.NET 7 ([`System.Threading.RateLimiting`/`Microsoft.AspNetCore.
RateLimiting`](../libraries/dotnet/aspnetcore-ratelimiting.md), [Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)),
with four built-in algorithms — Fixed Window, Sliding Window, Token
Bucket, Concurrency Limiter — partitionable per-key, `429` rejection, and
`Retry-After` headers out of the box. Exactly the "as standard as it
comes" answer: no third-party library, no bespoke token-bucket
implementation, consistent with `ADR-041`'s first-party preference.

Decision:
- **Partition every rate limiter by `AppId`** (`ADR-030`'s existing
  tenant-scoping key) — a tenant's limit is independent of every other
  tenant's; one noisy or hostile publisher can't exhaust another
  tenant's share of the deployment's capacity.
- **Token Bucket for publish (`Inbox`)** — allows legitimate bursts
  (a real publisher catching up after a brief outage) while still
  bounding sustained volume, a better fit than Fixed/Sliding Window for
  `ADR-023`'s "always `202`" ingestion, where the danger is *sustained*
  volume, not one burst.
- **Concurrency Limiter for GraphQL Subscriptions and Follow-style
  long-lived connections** — bounds concurrent open connections per
  tenant, a different resource (connection slots) than request rate.
- **Sliding Window for ordinary GraphQL queries/OpenAPI publish
  bursts at the Gateway** — smoother than Fixed Window at bucket-boundary
  edges, the standard general-purpose choice absent a more specific
  reason to pick Token Bucket or Concurrency Limiter.
- **Enforced at the API Gateway (`ADR-049`, YARP) first**, since YARP
  *is* an ASP.NET Core app and this middleware attaches the same way it
  would to any ASP.NET Core pipeline — one enforcement point in front of
  every backend service, not duplicated per-service. A service behind the
  gateway may layer its own additional limiter only if it has a
  resource-specific reason to (e.g., the Streaming Channel Service
  bounding ingest throughput independently of request count) — not as a
  default, to avoid duplicate/inconsistent limits for the same tenant.
- **Limits themselves are deployment-time configuration, not hardcoded**
  — `PermitLimit`/`Window`/`QueueLimit` per algorithm are ordinary
  `Microsoft.Extensions.Configuration` values (`ADR-041`), read per-
  `AppId` from the Schema Registry's existing tenant-scoping records
  (`ADR-030`) or a simple tenant-limits configuration section — which
  exact source is a build-time detail, not a new design question this
  ADR needs to settle.

Consequences:
- **Corrected, later pass**: this ADR's Context section describes the
  four built-in algorithms as offering "`429` rejection, and
  `Retry-After` headers out of the box," reading as uniform across all
  of them. Building and testing the actual Gateway wiring
  (`docs/08-build-plan.md` item 33, "Per-Tenant Rate Limiting") found
  this isn't so: verified directly against `System.Threading.
  RateLimiting` outside ASP.NET Core (a throwaway console probe
  acquiring past each limiter's own limit and checking
  `lease.TryGetMetadata(MetadataName.RetryAfter, ...)`), only
  `TokenBucketRateLimiter` ever attaches `Retry-After` metadata in this
  library version — `ConcurrencyLimiter` and `SlidingWindowRateLimiter`
  never do. [`RateLimiterPolicies.cs`](../../src/EventStore.Gateway/RateLimiterPolicies.cs)'s
  `OnRejected` handler was already guarded with `TryGetMetadata` before
  this was found, so no production code needed to change — only this
  ADR's own framing and this item's test assertions needed correcting.
  See `docs/changes/2026-08-10.md`'s "Finding 2" for item 33.
- **Also found while building item 33**: `TenantPartitionKey.Resolve`
  ([`src/EventStore.Gateway/TenantPartitionKey.cs`](../../src/EventStore.Gateway/TenantPartitionKey.cs))
  cannot resolve the caller's `AppId` from `ClaimsPrincipal`/
  `HttpContext.User` the way the rest of this design does — per
  `ADR-049`'s own design, the Gateway forwards the `Authorization` header
  unvalidated and never populates `HttpContext.User` itself (real JWT/
  DPoP validation happens only at the Host). The real partition-key
  resolution is a tiered fallback: `HttpContext.Items` (populated by
  `AppIdBufferingMiddleware` peeking a buffered `/publish`/`/follow` JSON
  body) → an **unvalidated peek at the raw JWT payload's base64url
  segment** for a `client_id`/`sub` claim (explicitly commented in code
  as a traffic-bucketing heuristic only, never a security/authorization
  decision) → a fixed `"anonymous"` bucket. Cross-reference `ADR-049`,
  whose own unvalidated-header-forwarding design is exactly why this
  Gateway-side resolution has no authenticated claims to read in the
  first place.
- Resolves `docs/10-open-questions.md`'s tenant-fairness row.
- `ADR-037`'s GraphQL depth/cost limiter is unaffected and still needed —
  it bounds one query's *shape*; this ADR bounds sustained *volume*
  across many requests. The two are complementary, not overlapping.
- A tenant that legitimately needs a higher limit is a configuration
  change, not a code change — consistent with this design's existing
  preference for configuration over code for anything deployment-shaped.
- No new library dependency — `Microsoft.AspNetCore.RateLimiting` ships
  in the shared framework already referenced by every `EventStore.
  Host.<Provider>` project and by the Gateway.
