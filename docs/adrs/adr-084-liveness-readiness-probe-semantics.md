[← ADR index](../07-adrs.md)

# ADR-084: Liveness/readiness probe semantics — readiness stays healthy through degraded peers by default, consistent with `ADR-023`'s never-block posture

Status: Accepted

Context: `06-solution-structure.md` wired health checks without
detailing their actual semantics ("not detailed further in this doc").
`docs/10-open-questions.md` named the real tension left unresolved:
should a degraded dependency (an unreachable peer, replica lag) fail
readiness, given `ADR-023`'s never-block-publish posture? Direct design
conversation resolved this session: this has a clear framework-level
default, not a domain-specific one, because it follows directly from a
principle this design already committed to — with a configurable
override left available for a deployment whose own risk tolerance
genuinely differs.

Decision:
- **Liveness answers only "is this process capable of handling requests
  at all"** — standard, uncontroversial semantics: fails only on
  unrecoverable internal failure (deadlock, unhandled fatal state), never
  on a dependency's health. An orchestrator restarts on liveness failure;
  restarting a healthy process because a *peer* is unhealthy would fix
  nothing.
- **Readiness does NOT fail merely because a downstream dependency is
  degraded (an unreachable peer, a lagging replica) — this is the actual
  resolution, not left ambiguous.** Failing readiness causes an
  orchestrator/load balancer to stop routing traffic to that instance —
  functionally equivalent, from a caller's perspective, to the instance
  refusing writes itself. Doing that in response to peer/replica
  degradation would silently reintroduce exactly the "block on trouble"
  behavior `ADR-023`'s persist-first, flag-don't-reject posture already
  explicitly rejected. An instance whose own local database is reachable
  and whose own core loop is functioning **is ready**, even while a peer
  is unreachable (`ADR-033`'s outbox/catch-up sync exists precisely to
  tolerate this) or a replica is lagging.
- **Readiness fails only for what makes the instance itself incapable of
  its core job** — its own primary database unreachable, an
  unrecoverable startup failure (e.g. a required migration never
  applied) — not for a peer's or a replica's condition.
- **A deployment may configure stricter readiness semantics on top of
  this default, if its own domain's risk tolerance genuinely demands
  it** — e.g. a safety-critical continuous-monitoring deployment that
  would rather stop accepting new data than risk any replica divergence
  could opt into failing readiness on replica lag past a threshold. This
  is a deployment-time configuration choice layered on the framework
  default, the same shape `ADR-058`'s per-tenant rate limits and
  `ADR-077`'s feature flags already use for "framework sets a sane
  default, deployment can override" — not a second, competing default
  baked into the framework itself.

Consequences:
- Confirms `ADR-023`'s never-block posture extends coherently to the
  operational/health-check layer, not just the publish path — a real
  consistency gap this ADR closes, not just documents.
- `06-solution-structure.md`'s health-check wiring should eventually
  reference this ADR's semantics explicitly rather than staying silent
  on them — flagged as propagation work, not done in this pass.
- Resolves the design fork logged in `docs/changes/2026-07-31.md`
  (formerly `docs/10-open-questions.md` row 8).
