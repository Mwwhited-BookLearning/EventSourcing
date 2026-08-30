[← Bugs index](../../../changes/2026-08-30.md)

# Every cross-service HttpClient faults under Polly's default 10s/30s timeouts, repeatedly, under real concurrent startup load

**Scope**: `framework` · **Tier**: `service`

## What was wrong

Running the real `AppHost` produced a steady stream of visible log lines:
`Resilience event occurred. EventName: 'OnTimeout', Source: '-standard//
Standard-AttemptTimeout', Operation Key: '', Result: ''` (and, under
sustained load, the same for `Standard-TotalRequestTimeout`), repeated
many times. Direct report: "while the application is running I am
getting this error... as well as many others." "Do this correctly.. it
should not fault for no reason."

## How and where it was found

`EventStore.ServiceDefaults/Extensions.cs`'s `AddServiceDefaults` calls
`http.AddStandardResilienceHandler()` inside `ConfigureHttpClientDefaults`
— applied to **every** `HttpClient` in **every** service that calls it
(the three `Host.<Provider>` projects, `EventStore.DevIdp`,
`EventStore.Gateway`). This is Aspire's own unmodified project-template
default (confirmed by re-reading the file's own header comment, never
actually examined before this pass), not a decision this project ever
made deliberately. `Microsoft.Extensions.Http.Resilience`'s "standard"
pipeline defaults to a **10-second per-attempt timeout** and a
**30-second total-request timeout**.

## Root cause (corrected once, before shipping — see "Wrong theory" below)

`EventStore.DevIdp`'s `RbacProjectionWorker` runs **8 concurrent**
long-lived tail loops (`TailForeverAsync`, 2 AppIds × 4 reserved event
types, per `AppHost.cs`'s `Rbac__AppIds` config), each of which first
acquires a fresh DPoP key, requests an access token from `/connect/token`,
then opens one `QUERY /follow/{eventType}` request. Under real Aspire
startup — 8 of these firing via `Task.WhenAll` at once, against a
`DevIdp`/`eventstore` pair that's also still JIT-warming, running EF
migrations, and building its own GraphQL schema for the first time — any
one of these genuinely bounded round trips (the token POST, or the
initial connect-and-authenticate phase of the tail request) can take
longer than 10 seconds under that real contention. `Microsoft.Extensions
.Http.Resilience`'s `AttemptTimeout`/`TotalRequestTimeout` strategies
wrap the `HttpClient.SendAsync` invocation itself — they have no way to
distinguish "the server is just slow under real load" from "this
connection is actually hung" — so they cancel and (per the standard
pipeline's own retry strategy) retry, logging `OnTimeout` each time. With
8 concurrent tails all doing this independently, the result is a steady,
recurring stream of these events, not one isolated failure.

**Wrong theory, caught and corrected before shipping**: an earlier pass
of this diagnosis assumed the vulnerable phase was `FollowClient
.TailAsync`'s own long-lived SSE body read (the tail connection is
*meant* to stay open indefinitely once established, waiting for new
events). That turned out to be **false** — `TailAsync` calls
`SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)`,
which returns as soon as response *headers* arrive; Polly's
`DelegatingHandler`-based strategies only wrap that one `SendAsync`
call, not whatever the caller does with the response afterward. A test
built against that theory (driving a real Follow tail idle for 12
seconds mid-stream) passed even with the old, buggy resilience handler
still in place — a real, useful negative result, not a bug in the test:
it proved the theory wrong instead of confirming it. Verified the
correct mechanism directly afterward: a minimal `HttpClient` whose
primary handler simply delays 12 seconds before returning any response
(simulating a slow-to-even-start-responding backend, not a slow stream)
reproduced the *exact* reported log line, including both
`Standard-Retry` and `Standard-TotalRequestTimeout` events, confirming
the vulnerable phase is genuinely "the server hasn't responded yet at
all," which is exactly what real startup contention among many
concurrent callers produces.

## Fix

Removed `AddStandardResilienceHandler()` (and the
`Microsoft.Extensions.Http.Resilience` package it comes from) from
`EventStore.ServiceDefaults` entirely, rather than retuning its timeout
values, for two independent reasons:

1. **Not necessary anywhere in this design.** Every real cross-service
   retry need this project actually has already owns its own
   purpose-built, correctly-tuned mechanism: `ADR-033`'s durable outbox/
   inbox, `ADR-060`'s webhook dispatcher, `FollowClient`/
   `EventTailReader`'s own reconnect loops (`RbacProjectionWorker
   .TailForeverAsync`'s own catch-and-retry), and EF Core's
   `EnableRetryOnFailure` for transient DB failures. A generic HTTP-level
   wrapper was never adding real, non-redundant value — only picking an
   arbitrary timeout ceiling (10s) that happens to be wrong for this
   project's own real multi-service startup topology.
2. **Direct request**: Polly's new Open Source Maintenance Fee
   ([thepollyproject.org, 2026-07-14](https://thepollyproject.org/2026/07/14/polly-osmf-announcement.html))
   — a per-organization usage fee triggered by revenue, not by which
   Polly package or version is used — was cause enough to remove every
   *deliberate* use of Polly in this solution, not just retune the one
   causing visible symptoms. See `docs/references.md`'s own "considered
   and rejected" entry for the full reasoning, and this repo's
   `EventStore.UnitTests`' `Polly.Contrib.Simmy`-based fault injection
   (`ADR-063`), replaced the same pass with a small hand-rolled
   `FaultInjector` (`tests/EventStore.UnitTests/FaultInjector.cs`) —
   the only other place this solution deliberately depended on Polly.

**Not fully eliminated from the dependency graph, and can't be without
disproportionate cost**: `Aspire.Hosting.*` (this project's own local
orchestration framework, `ADR-026`) and `OpenIddict.*.SystemNetHttp`
(the adopted OAuth2/OIDC provider, `ADR-006`) both depend on Polly
internally, for their own unrelated purposes we don't invoke directly.
Dropping either is not a reasonable response to a licensing-fee
consideration on top of an already-fixed timeout bug — stated honestly
here rather than overclaiming a fully Polly-free solution.

- `src/EventStore.ServiceDefaults/Extensions.cs`,
  `EventStore.ServiceDefaults.csproj`: resilience handler and package
  reference removed.
- `tests/EventStore.UnitTests/FaultInjector.cs` (new),
  `PublishCrashRecoveryFaultInjectionTests.cs`,
  `EventStore.UnitTests.csproj`: `Polly.Contrib.Simmy` replaced with a
  minimal, dependency-free equivalent (same `[0,1]` injection-rate
  convention, both existing tests used rate `1.0`).
- **Regression tests**:
  - `tests/EventStore.UnitTests/ServiceDefaultsHttpResilienceTests.cs`
    — `AddServiceDefaultsAppliesNoAttemptTimeoutSoASlowButEventuallySuccessfulResponseIsNotAborted`:
    a minimal `HttpClient` built through the real `AddServiceDefaults()`
    code path, with a primary handler that delays 12 real seconds before
    responding 200. Confirmed **red** against the old
    `AddStandardResilienceHandler()` (reproduced the exact reported
    `OnTimeout` log line, both `Standard-Retry` and
    `Standard-TotalRequestTimeout`, request ultimately thrown as
    canceled) and **green** against the fix (the full ~12s delay elapses,
    response returned successfully) — verified both ways via a temporary
    `git stash` of the fix, not assumed.
  - `EventStore.UnitTests`' `ARetryAfterTheCallerNeverLearnsOfA...`/
    `ARetryWithTheSameEventIdButGenuinelyDifferentContentIs...` (`ADR-063`)
    still pass unchanged against the new `FaultInjector`.
  - Full `EventStore.UnitTests` (48/48) and the full SQLite
    `EventStore.IntegrationTests` (159/159, including
    `RbacProjectionWorkerHttpSqliteTests`) green; full solution build
    clean.

No ADR reversal: `AddStandardResilienceHandler()` was never itself the
subject of any ADR (an unexamined template default, not a decision), so
there's nothing to formally supersede for that half. `ADR-063` gained an
additive implementation note for the `Polly.Contrib.Simmy` replacement,
per this project's own additive-history-editing convention.
