# Zeebe / Camunda 8 spike — Option D

Proves the "real BPMN 2.0 engine" option from
[`docs/comparisons/user-flow-dsl.md`](../../../docs/comparisons/user-flow-dsl.md):
a real `.bpmn` process definition (`Bpmn/AdverseEventReview.bpmn`,
authored by hand, not exported from Camunda Modeler — see its own notes
below), deployed to and executed by a real Zeebe broker via the
`zb-client` NuGet package.

## Running it

Unlike `TemporalSpike/` in this same folder, there is **no embedded/
local dev-server equivalent for Zeebe** — a real broker container has to
be started first:

```bash
docker run -d --name zeebe-spike -p 26500-26502:26500-26502 \
  -e CAMUNDA_DATA_SECONDARY_STORAGE_TYPE=none \
  -e CAMUNDA_SECURITY_AUTHENTICATION_UNPROTECTEDAPI=true \
  -e ZEEBE_GATEWAY_SECURITY_AUTHENTICATION_MODE=none \
  -e CAMUNDA_SECURITY_AUTHORIZATIONS_ENABLED=false \
  camunda/zeebe:8.8.0
```

Then `dotnet run` from this directory. Tear the container down with
`docker rm -f zeebe-spike` afterward — it holds no state worth keeping.

**Why three environment variables just to start a local broker**, found
only by running it and reading the real errors, one at a time:

1. Without `CAMUNDA_DATA_SECONDARY_STORAGE_TYPE=none`, the broker
   endlessly retries initializing a search-engine schema against an
   Elasticsearch cluster that was never started — 8.8's unified image
   assumes Operate/Tasklist's storage layer by default even for a bare
   broker.
2. Without `ZEEBE_GATEWAY_SECURITY_AUTHENTICATION_MODE=none` (paired
   with `CAMUNDA_SECURITY_AUTHENTICATION_UNPROTECTEDAPI=true`), every
   gRPC call fails with `Unauthenticated: Expected authentication
   information at header with key [authorization], but found nothing`
   — with no Identity/Keycloak anywhere in this setup to authenticate
   against.
3. Without `CAMUNDA_SECURITY_AUTHORIZATIONS_ENABLED=false`, deployment
   fails with `PermissionDenied ... FORBIDDEN: Insufficient permissions
   to perform operation 'CREATE' on resource 'RESOURCE'` — 8.8's
   fine-grained resource-authorization model is on by default even with
   authentication itself disabled.

None of this is documented in one place; each was found by running the
container, reading the real gRPC/log error, and searching for that
specific message.

## The BPMN file

`Bpmn/AdverseEventReview.bpmn` — a real BPMN 2.0 XML file, hand-authored
(never an inline C# string, per direct request), covering the same
eight actions and two branch points as every other spike in this
folder:

- Two `bpmn:exclusiveGateway` elements (`SeriousAdverseEvent?`,
  `accepted?`), each with one FEEL `conditionExpression` flow and one
  explicit `default=` flow — Zeebe rejects an exclusive gateway where a
  non-default flow has no condition, a real deployment-time validation
  error, not a suggestion.
- A `bpmn:message`/`bpmn:intermediateCatchEvent` pair
  (`AuthorityDecisionPublished`, correlated on `=entityId`) for "wait
  for the PI's real decision" — the same role Temporal's signal and
  Elsa's bookmark play, expressed here as a first-class, diagrammable
  BPMN construct rather than an API call invisible to any diagram.
- Every `bpmn:conditionExpression`'s `xsi:type` must be written as
  `bpmn:tFormalExpression` (namespace-qualified), not bare
  `tFormalExpression` — Zeebe's deployment-time XML Schema validation
  rejects the unqualified form with a `cvc-elt.4.2` error naming the
  exact line.

## Findings

All three scenarios (accepted / rejected / non-serious) pass, with a
genuine message-correlation pause/resume round trip for the two
`SeriousAdverseEvent` cases — not simulated.

**The real friction here was operational, not the BPMN authoring.**
Once the three environment variables above were found, deployment and
the two XML-validation fixes above, everything about the broker and the
process definition worked as documented.

**The one finding worth flagging as a real, reproducible gap**: `zb-
client`'s high-level `client.NewWorker()....Open()` builder — the
documented, idiomatic way to run a job worker — **never activated a
single job**, silently, no exception anywhere, across a full broker
container restart and several builder-option combinations (explicit
`StreamEnabled(false)`, shorter `PollInterval`, keeping every returned
`IJobWorker` handle alive in a list so nothing could be garbage-
collected out from under it). A throwaway manual
`client.NewActivateJobsCommand()...Send()` call against the exact same
broker, same job type, same pending jobs, **worked on the first try**
and returned real, correctly-populated job data. `Program.cs` therefore
hand-rolls the same activate/complete polling loop `NewWorker` wraps
internally — `NewActivateJobsCommand`/`NewCompleteJobCommand` are the
SDK's own documented lower-level primitives, so this is still real,
idiomatic Zeebe client code, just without the convenience wrapper. This
reproduced identically after a clean broker restart, so it reads as a
real compatibility gap between this client version (`zb-client
2.10.0`) and this broker version (`camunda/zeebe:8.8.0`) rather than a
one-off fluke.

**Retested across four client/broker combinations, not just the one
above** — `zb-client` 2.10.0 is already NuGet's current latest, so the
matrix instead varied the broker (`camunda/zeebe:8.8.0`, the newer patch
`8.8.36`, and the newer minor `8.9.11`, `latest` at the time of testing)
and tried one older client (`zb-client` 2.9.0) against the newest
broker. Same isolated `NewWorker()` reproduction (register all eight
job types, create one process instance, wait 15s for the first job to
activate) run each time. **Every single combination reproduced the
identical failure** — no job ever activated via `NewWorker()`, on any
pairing tried. This rules out "stale client" or "stale broker" as the
explanation; whatever the gap is, it isn't fixed by moving either side
forward within this recent version range, and the hand-rolled
`NewActivateJobsCommand`/`NewCompleteJobCommand` polling loop in
`Program.cs` remains the only combination in this whole exercise that
actually worked.
