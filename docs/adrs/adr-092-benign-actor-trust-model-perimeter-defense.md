[← ADR index](../07-adrs.md)

# ADR-092: Core-engine trust model assumes non-malicious actors; hostile-traffic defense is a deployment-perimeter concern, not a framework one

Status: Accepted

Context: `docs/10-open-questions.md` row 9 asked whether this design
should adopt a maintained threat model (STRIDE or similar) plus a risk
register, given ~20 ADRs each make individually-sound security decisions
never checked together against one adversary/trust-boundary model —
specifically naming the combination of `ADR-035`'s unauthenticated-
submitter posture, `ADR-023`'s persist-everything ingestion, and
`ADR-058`'s volume-only rate limiting as a plausible hostile-tenant/
insider exposure. Direct design conversation resolved this session: the
core engine's own trust model **assumes non-malicious actors** — that
combination isn't an unexamined gap, it's the direct, intended
consequence of decisions already made for a different reason (tolerant,
non-authoritative capture), and defending against a genuinely hostile
actor is explicitly pushed to the deployment perimeter, not designed
into the core engine.

Decision:
- **No formal STRIDE threat model or risk register is adopted as a
  framework artifact.** Building and maintaining one implies the core
  engine itself is the thing being hardened against an adversary — it
  isn't, by design. `ADR-035`'s unauthenticated-submitter posture and
  `ADR-023`'s persist-everything ingestion exist to serve *tolerant,
  non-authoritative capture* (an honest device that got its own claim
  wrong), not to make the engine safe against a party actively trying to
  do harm — conflating the two would misdescribe what those ADRs
  actually optimize for.
- **Hostile-traffic defense (DDoS, malicious payload floods, credential
  stuffing) is a deployment-perimeter concern, satisfied by an ordinary
  API gateway/WAF layer a production deployment adds in front of this
  framework, not something the core engine defends against internally.**
  `ADR-049` already puts YARP at the single external entry point — a
  production deployment's natural place to add a WAF, IP-reputation
  filtering, or a cloud provider's own DDoS-protection layer, ahead of
  ever reaching `ADR-058`'s volume-only rate limiting. This isn't a new
  mechanism; it's naming where a real deployment's defense-in-depth
  already belongs, on infrastructure this design already routes every
  request through.
- **`ADR-058`'s rate limiting stays scoped to fairness, not security** —
  its own text already frames it as preventing one noisy tenant from
  starving another, not as a defense against a hostile actor; this ADR
  confirms that scope explicitly rather than leaving it ambiguous
  whether rate limiting was quietly expected to double as attack
  mitigation.
- **A genuinely regulated/high-assurance deployment that needs a real
  STRIDE analysis can still do one — at the deployment level, informed
  by its own actual threat surface (which tenants, which network
  topology, which perimeter controls)**, not as a framework-maintained
  artifact trying to anticipate every deployment's adversary model in
  the abstract.

Consequences:
- `01-c4-architecture.md`'s container diagram should eventually note
  this trust boundary explicitly (core engine trusts its perimeter; the
  perimeter is where hostile-traffic defense lives) — flagged as
  propagation work, not done in this pass.
- Confirms, rather than changes, every cited ADR's actual scope
  (`ADR-035`, `ADR-023`, `ADR-058`) — no decision reversed.
- Resolves `docs/10-open-questions.md` row 9.
