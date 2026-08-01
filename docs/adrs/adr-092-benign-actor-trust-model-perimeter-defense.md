[← ADR index](../07-adrs.md)

# ADR-092: Core-engine trust model assumes non-malicious actors; hostile-traffic defense is a deployment-perimeter concern, not a framework one

Status: Accepted

Context: `docs/10-open-questions.md` row 9 asked whether this design
should adopt a maintained threat model (STRIDE or similar) plus a risk
register, given ~20 ADRs each make individually-sound security decisions
never checked together against one adversary/trust-boundary model —
specifically naming the combination of `ADR-035`'s non-authoritative-
capture posture, `ADR-023`'s persist-everything ingestion, and
`ADR-058`'s volume-only rate limiting as a plausible hostile-tenant/
insider exposure. Direct design conversation resolved this session: the
core engine's own trust model **assumes non-malicious actors** — that
combination isn't an unexamined gap, it's the direct, intended
consequence of decisions already made for a different reason (tolerant,
non-authoritative capture), and defending against a genuinely hostile
actor is explicitly pushed to the deployment perimeter, not designed
into the core engine.

**Correction on scope, verified against the actual cited ADRs**: this
isn't an "unauthenticated submitter" gap — `ADR-042` already revised
`ADR-035` so an ordinary, already-authenticated publish (`ADR-006`'s
bearer-token auth) defaults `AuthorityStatus` to `accepted`, not
`unattested`. The real, narrower surface is self-attested/UCAN
submitters (`ADR-036`) and a detector's own unconfirmed output
(`ADR-042`) — both *authenticated* as a caller, just not yet *trusted*
as a claim. The trust-model point below holds for this narrower surface
too, but the earlier framing overstated it.

Decision:
- **No formal STRIDE threat model or risk register is adopted as a
  framework artifact.** Building and maintaining one implies the core
  engine itself is the thing being hardened against an adversary — it
  isn't, by design. `ADR-035`/`ADR-042`'s non-authoritative capture and
  `ADR-023`'s persist-everything ingestion exist to serve *tolerant
  capture of an authenticated-but-not-yet-trusted claim* (an honest,
  authenticated device that got its own claim wrong), not to make the
  engine safe against a party actively trying to do harm — conflating
  the two would misdescribe what those ADRs actually optimize for.
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
- **`ADR-058`'s rate limiting stays scoped to tenant fairness, not
  perimeter defense.** **Correction, verified against `ADR-058`'s
  actual text**: it already names "a noisy **or hostile** publisher" —
  so it isn't blind to hostile actors, it just answers a different
  question than perimeter defense does. Its job is bounding *sustained
  volume from a caller already inside the system* (any tenant, hostile
  or merely noisy) so one doesn't starve another; it was never designed
  as, and this ADR doesn't ask it to become, a WAF-shaped defense
  against unauthenticated attack traffic, credential stuffing, or DDoS —
  those stay the deployment-perimeter layer's job, above.
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
