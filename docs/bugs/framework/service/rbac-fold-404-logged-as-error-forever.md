[← Bugs index](../../../changes/2026-08-30.md)

# `RbacProjectionWorker` logs a routine, expected 404 as an `Error`-level lost connection, forever

**Scope**: `framework` · **Tier**: `service`

## What was wrong

Running the real `AppHost`, `EventStore.DevIdp`'s own logs (pasted directly
from a live run's OTLP export) showed a steady stream of `Error`-severity
entries: `"RBAC fold for trial1/RoleRevoked lost its connection;
reconnecting"` and `"RBAC fold for trial1/AppTrustRootRegistered lost its
connection; reconnecting"`, each carrying an `HttpRequestException:
Response status code does not indicate success: 404 (Not Found)` from
`FollowClient.TailAsync`.

## How and where it was found

Found while investigating a separate, already-fixed Polly resilience
issue (`follow-client-faults-under-default-http-resilience-timeout.md`)
— the user pasted two real OTLP log records from a genuinely running
`EventStore.DevIdp` process, not a hypothetical. The two entries repeat
every `ReconnectDelay` (2 seconds) indefinitely.

## Root cause

`RbacProjectionWorker` runs one `TailForeverAsync` loop per (AppId ×
reserved event type) — 2 AppIds × 4 types (`RoleGranted`/`RoleRevoked`/
`PermissionGranted`/`AppTrustRootRegistered`) = 8 concurrent loops,
started unconditionally at worker startup regardless of whether that
combination has ever actually happened. A reserved event type only gets
a registered schema for a given `AppId` once something of that kind is
actually published there (`ADR-067`) — an `AppId` that's only ever had
roles *granted*, never *revoked*, genuinely has no `RoleRevoked` type
registered yet, and `FollowClient.TailAsync`'s own
`EnsureSuccessStatusCode()` surfaces that as an ordinary `404`. This is
an expected, recoverable, and entirely benign state — not a connection
that was ever established and then lost. `TailForeverAsync`'s single
`catch (Exception ex)` block treated it identically to a genuine
connection failure: logged at `Error`, then retried on the same 2-second
cadence, forever, for as long as that combination simply never occurs.
With demo/seed data that (plausibly, and in this run, actually) never
revokes a role or registers a second trust root for a given `AppId`,
this produces continuous `Error`-level noise for the entire lifetime of
the process — a real production observability concern were this ever
deployed as-is: false-positive `Error` volume this large would either
get filtered out of any real alerting pipeline (burying genuine errors
in noise) or, worse, page someone for nothing.

## Fix

`src/EventStore.DevIdp/RbacProjectionWorker.cs`'s `TailForeverAsync`
gains a specific `catch (HttpRequestException ex) when (ex.StatusCode ==
HttpStatusCode.NotFound)` branch, ahead of the generic `catch
(Exception)`, logged at **Warning** (direct request — visible in a real
deployment's default log level, not `Debug`, since a combination that
never resolves is still worth a human eventually noticing) with an
honest message ("has no registered schema yet; will retry") instead of
the misleading "lost its connection." The generic `catch (Exception)`
branch (a genuine reconnect) is unchanged in spirit but now also feeds
the new instrumentation below.

**Also bound into Aspire/OTel directly, direct request** ("bind to
aspire/otel... so bugs can be logged correctly... consider this an
exception"), rather than left as an `ILogger` call alone:
- `DuplexInstrumentation.WorkerTailReconnects` (new `Counter<long>`,
  `ADR-088`'s shared `Meter`) — incremented on every reconnect, tagged
  `worker`/`app.id`/`event.type`/`reason` (`"not_yet_registered"` or
  `"error"`), graphable/alertable in the Aspire dashboard (or any OTel
  backend) with no log-grepping required. Named generically (`worker`,
  not `rbac`) so the same instrument can cover another worker's own
  tail-reconnect loop later without a schema change, though only
  `RbacProjectionWorker` uses it this pass.
- A genuine reconnect (the `catch (Exception)` branch) now calls
  `Activity.AddException(ex)` on a `DuplexInstrumentation.ActivitySource`
  -started `Activity` wrapping each tail attempt — a real .NET 9+ BCL
  method (no `OpenTelemetry` package dependency needed), producing the
  standard OTel `"exception"` event with `exception.type`/
  `exception.message`/`exception.stacktrace` tags, which the Aspire
  dashboard's own Traces view surfaces automatically — a real tracked
  exception, not just text in a log line someone has to be looking at.

## Verification

- New test: `RbacProjectionWorkerHttpSqliteTests
  .CatchUpOnceAsyncThrowsAnHttpRequestExceptionWithNotFoundStatusForAGenuinelyUnregisteredEventType`
  — confirms the real exception `FollowClient` throws for a genuinely
  unregistered type actually has `StatusCode == HttpStatusCode.NotFound`
  (the exact condition the new `catch` filter depends on), not assumed
  from reading the fix's own code.
- Full `EventStore.UnitTests` (48/48) and the full SQLite
  `EventStore.IntegrationTests` (160/160, including all
  `RbacProjectionWorkerHttpSqliteTests`) green; full solution build
  clean.
- `TailForeverAsync`'s own infinite retry loop is not directly exercised
  end-to-end by any test (the class's own header comment documents why:
  driving the real `BackgroundService.ExecuteAsync` inside a
  `WebApplicationFactory` hits a self-referential-`HttpClient`
  construction hazard) — the fix's correctness rests on the confirmed
  exception shape above plus direct code inspection of the
  now-unconditional counter/log-severity changes, not a full timing-based
  end-to-end assertion. Flagged honestly rather than implied covered.

No ADR update: an internal logging-severity/telemetry correction to an
already-decided mechanism (`ADR-067`), not a new architectural decision
or a change to any persisted shape/contract. `ADR-088`'s own instrument
catalog (`DuplexInstrumentation.cs`) gained one new counter, additive to
its existing list, no existing instrument changed.
