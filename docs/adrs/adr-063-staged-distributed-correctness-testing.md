[← ADR index](../07-adrs.md)

# ADR-063: Staged adoption of distributed-correctness testing — start with property-based + in-process fault injection, escalate toward production

Status: Accepted — adopts [`docs/comparisons/distributed-correctness-testing.md`](../comparisons/distributed-correctness-testing.md)'s suggested staged path as a real decision

Context: That comparison mapped four options — property-based testing
(`FsCheck`), in-process fault injection (`Polly`+`Simmy`), network-level
fault injection (`Testcontainers`+`Toxiproxy`), and Jepsen-style external
black-box verification — against `ADR-019`/`ADR-024`/`ADR-033`'s hard
correctness invariants, and suggested a staged, non-forced adoption path
since no single option covers all three. Direction received this
session: **start with the low-cost options, propose ramping up if/when
this moves toward production** — adopting that staged path directly
rather than picking one option permanently.

Decision:
- **Adopt now, alongside `ADR-055`'s `EventStore.UnitTests`**:
  - **`FsCheck`** (property-based testing) for `ADR-019`'s hash-chain
    tamper-detection claim and the pure-logic half of `ADR-024`'s
    conflict-resolution policy (stream-order LWW correctness, checked
    in-memory against the fold function directly) — the cheapest,
    highest-confidence win the comparison identified, no new
    infrastructure beyond `ADR-055`'s existing `MSTest`-based suite.
  - **`Polly`+`Simmy`** (in-process fault injection) for the narrower,
    specific question of whether `ADR-033`/`ADR-039`'s durable outbox/
    inbox actually resumes correctly after a simulated crash — cheap,
    in-process, answers a real question `FsCheck` can't reach.
- **Not adopted now, named as a deliberate, deferred escalation, not an
  open question left dangling**:
  - **`Testcontainers`+`Toxiproxy`** (real network-level fault
    injection — genuine multi-process partition testing) is the
    proposed **first move if/when this system heads toward a real
    production deployment** — it reuses infrastructure already adopted
    (`Testcontainers`, `ADR-055`) and directly exercises `ADR-033`'s
    replication-convergence claim and `ADR-024`'s cross-server conflict
    path under real (not simulated) network conditions.
  - **Jepsen-style external verification** stays the named ceiling for
    if a real production incident ever suggests a convergence bug the
    `Toxiproxy`-based tier didn't catch, or if a specific deployment's
    stakes (regulated financial settlement, safety-critical data)
    justify the up-front expertise investment — not a default, and not
    expected to be reached without a concrete trigger.
- **Trigger for escalation is "moving toward production," not a fixed
  calendar date or arbitrary maturity milestone** — revisit this ADR
  when a real production deployment is actually being planned, at which
  point `Testcontainers`+`Toxiproxy` scenarios should be written for
  `ADR-033`'s convergence claim specifically, before that deployment
  goes live.

Consequences:
- Resolves `docs/10-open-questions.md`'s distributed-correctness-testing
  row — the comparison's suggested path is now a real decision, not just
  a suggestion sitting in a comparison doc.
- `EventStore.UnitTests` gains `FsCheck`-based property tests for the
  hash chain and conflict-resolution policy; a new lightweight fault-
  injection suite (using `Polly`+`Simmy`, added as a new dependency —
  not currently referenced anywhere else in this design) covers outbox/
  inbox crash-recovery.
- `docs/libraries/dotnet/fscheck.md` and `docs/libraries/dotnet/polly-
  simmy.md` are the concrete usage write-ups — added this pass.
- No production-readiness claim is made about replication convergence
  or cross-server conflict resolution until the `Toxiproxy` tier is
  actually built — stated honestly rather than implied by having *some*
  testing in place.
