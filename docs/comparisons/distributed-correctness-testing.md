[← Comparisons index](README.md)

# Validating the Hard Invariants: Property-Based Testing vs. In-Process Fault Injection vs. Network-Level Fault Injection vs. Jepsen-Style External Verification

**Resolved in [`ADR-063`](../adrs/adr-063-staged-distributed-correctness-testing.md)**,
adopting the staged path below directly. Raised by `ADR-055`'s own
closing note: ordinary test-pyramid coverage (unit/integration/e2e/UI)
is settled — this is about a different, narrower question, and it's
genuinely a fork with real trade-offs, not a case where one option is
obviously right. Written for a reader who doesn't already know this
territory (per direct request) — each concept is explained plainly
before it's compared, not assumed.

## What makes these three invariants different from ordinary test coverage

`ADR-055` already covers "does this code do what I expect for a normal
input" (unit tests) and "does the whole system work end-to-end against
a real database/browser" (integration/E2E tests). The three invariants
this comparison is about are a different *kind* of claim — each is a
promise that holds **for every possible input, timing, and failure**,
not just the examples a test author happened to think of:

- **Hash-chain tamper detection (`ADR-019`)**: "if even one byte of any
  past event is altered, replaying the chain detects it." A hand-picked
  test can prove this for *one* tampered byte at *one* position. It
  can't prove it for all of them.
- **Replication convergence under partition (`ADR-033`)**: "no matter
  what order messages arrive in, which site crashes when, or how long a
  network partition lasts, every site eventually ends up with the same
  data." This is a claim about *time and failure*, not just logic — a
  single-process unit test can't even represent "the network dropped a
  message here," because there's no real network involved.
- **Conflict resolution under concurrent/replayed writes (`ADR-024`)**:
  "stream-order last-write-wins is applied consistently no matter what
  order two genuinely concurrent patches arrive in, including after a
  crash-and-replay." A hand-picked test proves this for the two or three
  orderings the author thought to write down.

The four options below are four different ways to get *beyond*
hand-picked examples — each does it differently, and (this is the
important part) **no single one of them covers all three invariants
equally well**. Part of this comparison's job is showing which tool
fits which invariant, not picking one universal winner.

## The options

### Option A — Property-based testing (`FsCheck`)

**In plain terms**: instead of writing "given input X, expect output Y"
by hand, you write a *rule* the code must always satisfy (a "property"),
and the tool generates hundreds or thousands of random inputs trying to
find one that breaks the rule. If it finds one, it automatically
"shrinks" it down to the smallest input that still fails, so you get a
minimal repro instead of a 500-character random string.

**Concrete example for this design** — hash-chain tamper detection:

```csharp
[TestMethod]
public void AlteringAnyStoredEventBreaksTheChain()
{
    Prop.ForAll<StoredEvent[], int>((events, tamperIndex) =>
    {
        if (events.Length == 0) return true; // vacuously true, nothing to tamper
        var chain = BuildChain(events);
        var i = Math.Abs(tamperIndex) % events.Length;
        chain[i].Payload += "x"; // tamper with one event, anywhere in the chain
        return !VerifyChain(chain); // the chain MUST now report broken
    }).QuickCheckThrowOnFailure();
}
```

This one test effectively replaces hundreds of hand-picked "tamper with
event #3 of 7" style unit tests, and will find an edge case (e.g.
tampering with the very last event, or a chain of length 1) a human
author might not have thought to write by hand.

| | |
|---|---|
| **What it's good at** | Pure logic/data invariants with no real concurrency or network involved — exactly `ADR-019`'s hash-chain claim, and much of `ADR-024`'s conflict-resolution *policy* ("given these two patches in this order, is the result what stream-order LWW says it should be?" — can be tested as pure logic against an in-memory fold, no real timing needed). |
| **What it's not good at** | Anything where the *interesting* failure is about timing, crashes, or the network — `ADR-033`'s replication convergence isn't really a pure-function property; "what happens if this site crashes mid-sync" isn't something you can express as `Prop.ForAll` over plain data. |
| **Cost to adopt** | Low. `FsCheck` is a library, not infrastructure — no containers, no test environment beyond what `ADR-055`'s `EventStore.UnitTests` already has. Runs in milliseconds to seconds per property, fits in an ordinary CI unit-test stage. |
| **Fits this design's stack?** | Yes, cleanly. `FsCheck`'s core library is framework-agnostic — call it directly from an ordinary MSTest `[TestMethod]` with zero extra dependency, or add the community `fscheck-mstest` package for `[Property]`-attribute sugar (optional, not required) — either way it sits inside `ADR-055`'s existing `MSTest`-based `EventStore.UnitTests`, no new test project. |

### Option B — In-process fault injection (`Polly` + `Simmy`)

**In plain terms**: your code already calls a database, an HTTP
endpoint, or another service. `Simmy` (built into `Polly` v8) wraps those
calls and, some percentage of the time, injects a fake failure — a
timeout, an exception, a fake slow response — so you can test "does my
code handle this dependency being flaky" without actually breaking the
real dependency.

**Implementation note, added per direct request — this comparison had
gone stale relative to what actually happened, the same correction
`docs/patterns/fault-injection-chaos-engineering.md` already carries**:
`ADR-063` did adopt `Polly`+`Polly.Contrib.Simmy` for exactly this, for
real — but it was later **removed**, direct request, once `Polly`
announced a per-organization Open Source Maintenance Fee
([thepollyproject.org, 2026-07-14](https://thepollyproject.org/2026/07/14/polly-osmf-announcement.html))
that would apply to this reference framework's own test suite if an
adopter ever commercialized it. What this project ever actually used
`Simmy` for — inject a fake exception before a real call, at a
configurable `[0,1]` rate — turned out small enough not to need a
third-party dependency at all: a ~10-line hand-rolled `FaultInjector`
(`tests/EventStore.UnitTests/FaultInjector.cs`) replaced it, same tests,
same coverage, same rate convention. The comparison below is left as
originally written (a real option, correctly evaluated at the time) —
only this note reflects that the actual mechanism in place today is the
hand-rolled one, not `Simmy` itself.

**Concrete example for this design** — does the durable Peer Sync
Outbox/Inbox (`ADR-033`) actually resume correctly after a simulated
mid-write crash:

```csharp
var chaosPolicy = MonkeyPolicy.InjectException(with =>
    with.Fault(new IOException("simulated abend"))
        .InjectionRate(0.3)
        .Enabled());

// Wrap the peer-sync delivery call; assert the durable cursor
// (PeerSyncCursor) still resumes from the last real checkpoint
// after the injected failure, never re-delivering or losing an event.
```

| | |
|---|---|
| **What it's good at** | Testing *this process's own* resilience to a dependency failing — a real, useful check that `ADR-033`'s "durable table, not memory" claim actually survives a simulated abend at the code level. Cheaper and faster than standing up real multiple nodes. |
| **What it's not good at** | It only simulates failure *inside one process's own calls* — it can't represent "two independent server processes, a real network between them, and a real partition splitting them for 30 seconds." That's a fundamentally multi-process concern `Simmy` doesn't reach. |
| **Cost to adopt** | Low-to-moderate. A library, runs in-process, no new test infrastructure — but writing *meaningful* chaos scenarios (not just "throw sometimes") takes real thought about which failure at which point actually matters. |
| **Fits this design's stack?** | Yes in principle — `Polly` is the de facto standard .NET resilience library, first-party-adjacent and extremely widely used. **Adopted, then removed** (see the implementation note above) — the actual mechanism today is a hand-rolled `FaultInjector`, not `Polly`/`Simmy`. Runs inside `EventStore.UnitTests`, no new infrastructure beyond what's already there. |

### Option C — Network-level fault injection (`Testcontainers` + `Toxiproxy`)

**In plain terms**: stand up the *real* system — multiple real server
instances, a real database per `ADR-001`'s providers — inside real
Docker containers (`ADR-055`'s `Testcontainers`, already adopted), but
route the traffic *between* them through `Toxiproxy` (a small proxy
built for exactly this purpose), which can inject *real* network
conditions: added latency, a full connection cut ("partition"), reduced
bandwidth, or a connection reset — at the actual TCP level, not
simulated in code.

**Concrete example for this design** — replication convergence under a
real partition:

```csharp
// Two EventStore.Host.Postgres instances (ADR-033 peer sites), traffic
// between them routed through a Toxiproxy container.
await toxiproxy.AddToxicAsync("partition-site-b", "timeout", stream: "downstream", attributes: new() { ["timeout"] = 0 });
// ^ fully cuts site A -> site B traffic for the test's duration

await PublishEventsToSiteA(50);
await Task.Delay(TimeSpan.FromSeconds(10)); // partition held open
await toxiproxy.RemoveToxicAsync("partition-site-b"); // heal the partition

await WaitForConvergenceAsync(siteA, siteB, timeout: TimeSpan.FromSeconds(30));
Assert.AreEqual(await siteA.GetEntityStoreHashAsync(), await siteB.GetEntityStoreHashAsync());
```

| | |
|---|---|
| **What it's good at** | The closest thing to *real* multi-process, multi-network distributed testing without a full Jepsen setup — genuinely exercises `ADR-033`'s gossip-sync resumption, `ADR-024`'s cross-server `ConflictFlag` path, and "does the system actually converge after a real partition heals," not a simulation of one. |
| **What it's not good at** | Doesn't give you a *formal* consistency checker — you still have to write the assertion ("do both sites' hashes match after convergence") yourself, and a subtle correctness bug that doesn't show up as an obvious mismatch (e.g. a rare interleaving that produces a *plausible but wrong* result) can still slip through undetected. |
| **Cost to adopt** | Moderate. Both `Testcontainers` and `Toxiproxy` are already-adopted/well-precedented tools (`ADR-055`), so the infrastructure cost is mostly "one more container in the same test project," not a new platform — but writing genuinely useful partition/timing scenarios (not just "cut the network for 10 seconds") takes real distributed-systems thinking, the kind of expertise this question was raised because it's *not* already in-house. |
| **Fits this design's stack?** | Yes — same `EventStore.IntegrationTests` project (`ADR-055`), same `Testcontainers` dependency already there, `Toxiproxy` is one more well-supported Testcontainers module, not a new tool family. |

### Option D — Jepsen-style external, black-box verification

**In plain terms**: [Jepsen](https://jepsen.io/) is the tool the
distributed-systems industry treats as the gold standard for this exact
question — it's found real, serious consistency bugs in MongoDB,
CockroachDB, etcd, and many others. Critically, **Jepsen doesn't require
rewriting anything in Clojure (the language Jepsen itself is written
in)** — it treats the system under test as an *opaque black box*,
talking to it only over its real network protocol (here: plain
HTTPS/GraphQL), exactly the way `CockroachDB`'s own Jepsen tests talk to
it over the ordinary PostgreSQL wire protocol. A Jepsen test harness
(1) runs a real cluster of this framework's nodes, (2) generates random
concurrent operations against the real API, (3) injects real faults
(partitions, clock skew, process kills — a superset of what `Toxiproxy`
does), and (4) **formally checks** the recorded history of operations
against a stated consistency model (e.g., "was every read consistent
with *some* valid ordering of the writes that actually happened") —
this last step is what neither Option B nor C do: a real, published
verification algorithm, not a hand-written assertion that might itself
have a blind spot.

| | |
|---|---|
| **What it's good at** | The most rigorous option, by a wide margin, for exactly `ADR-033`'s replication-convergence claim and `ADR-024`'s cross-server conflict-resolution claim under real, adversarial concurrent load — this is the one place a formal consistency checker (not a hand-written assertion) genuinely changes what bugs get found. |
| **What it's not good at** | Overkill for `ADR-019`'s hash-chain claim (a pure-logic property, Option A already covers it completely) and for anything that isn't fundamentally a multi-node consistency question. |
| **Cost to adopt** | High. Requires learning Jepsen's own Clojure-based test-harness DSL (even though the *system under test* stays .NET) — a real, separate skill investment, which is exactly the "I'm not a test/QA expert" gap this comparison was requested to help navigate. Real-world Jepsen engagements are often done by consultants (Jepsen's own team offers this as paid analysis work) rather than built in-house from scratch. |
| **Fits this design's stack?** | Yes, mechanically (talks to the real HTTPS/GraphQL API, no code changes to the framework itself needed) — but organizationally this is the option requiring outside expertise or a real internal investment to stand up, unlike A/B/C which all sit inside tools this project already has some footprint in. |

## Which option actually covers which invariant

| Invariant | Best-fit option(s) | Why |
|---|---|---|
| Hash-chain tamper detection (`ADR-019`) | **A (property-based)** | Pure data/logic claim — no timing, no network, no concurrency. Fully solvable by generating random tamper scenarios against an in-memory chain. |
| Conflict resolution *policy* correctness (`ADR-024`, single process) | **A (property-based)** | "Given these patches in this order, does stream-order LWW produce the stated result" is also pure logic, testable in-memory against the fold function directly. |
| Conflict resolution under *real* concurrent/cross-server writes (`ADR-024` + `ADR-033`) | **C (network fault injection)**, or **D** for the rigorous version | Now it's genuinely about timing and multiple real processes — Option A can't represent "two servers, a real race" at all. |
| Replication convergence under partition (`ADR-033`) | **C (network fault injection)** as the practical middle ground; **D (Jepsen)** as the rigorous ceiling | This is the one invariant that's *fundamentally* about real network/process failure — Option A doesn't apply, Option B doesn't reach across processes, so it's C or D or nothing. |
| Durable-outbox resumption after an abend (`ADR-033`/`ADR-039`'s standing fault-tolerance requirement) | **B (in-process fault injection)** | This is specifically about *this process's own* crash-recovery code path, not a multi-node network condition — `Simmy` is the right-sized tool, `Toxiproxy`/Jepsen would be overkill for it specifically. |

## A staged way to decide, given no in-house test/QA expertise

**This is the path `ADR-063` adopted directly.** Not a single forced
pick — a way to invest incrementally, cheapest and most certain value
first, so a decision doesn't have to be made all at once before any of
this exists:

1. **Adopt `FsCheck` (Option A) first, regardless of anything else
   decided here.** It's the cheapest (no new infrastructure), the
   highest-confidence win (fully covers `ADR-019`'s claim and much of
   `ADR-024`'s policy logic), and fits directly inside `ADR-055`'s
   already-decided `MSTest`-based unit tests — this alone closes the
   least-defensible gap (an *unbounded* correctness claim currently
   checked only by hand-picked examples).
2. **Add in-process fault injection (Option B) for the specific,
   narrower question of "does our own crash-recovery code actually
   work"** — the durable outbox/inbox resumption logic `ADR-033`/
   `ADR-039` already require. Cheap, in-process, and answers a real
   question A can't. Adopted as `Simmy` originally, replaced by a
   hand-rolled `FaultInjector` once `Polly` announced its Open Source
   Maintenance Fee — see the implementation note under Option B above.
3. **Treat `Toxiproxy` (Option C) as the practical ceiling for
   replication-convergence testing** unless this system is headed
   somewhere the cost of a subtle consistency bug is severe enough to
   justify Option D's cost (regulated financial transaction settlement,
   safety-critical data, or similar) — C reuses infrastructure this
   design already has (`Testcontainers`) and answers the real question
   ("does it actually converge after a real partition") well enough for
   most deployments.
4. **Option D (real Jepsen) stays a named, available escalation, not a
   default** — worth revisiting specifically if a real production
   incident ever suggests a convergence bug C's testing didn't catch, or
   if this framework is ever deployed somewhere the stakes justify the
   expertise investment up front.

This isn't a forced single pick — items 1–3 can all be adopted
independently and incrementally; item 4 is a deliberate, expensive
escalation, not a "you're not done until you do this" checkbox.
