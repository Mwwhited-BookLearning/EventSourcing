[← ADR index](../07-adrs.md)

# ADR-074: Software Bill of Materials (SBOM) generation, and the library catalog doubles as a SOUP list

Status: Accepted

Context: Direction received this session: this design needs an SBOM and
SOUP treatment. Both are real, verified requirements, not speculative —
checked against the actual sources before designing anything:

- **SBOM**: [Executive Order 14028](https://www.nist.gov/itl/executive-order-14028-improving-nations-cybersecurity/software-security-supply-chains-software-1)
  (2021) requires federal agencies and their software vendors to produce
  machine-readable SBOMs, in NTIA/CISA-accepted formats (SPDX, CycloneDX,
  or SWID). More specifically relevant to this design's own chosen
  proving-ground domain: **FDA Section 524B** of the FD&C Act (effective
  March 2023, grace period ended October 2023) requires a machine-
  readable SBOM listing *every* commercial, open-source, and off-the-
  shelf component for any "cyber device" — a medical device containing
  software that could be vulnerable to cybersecurity threats —
  submitted via 510(k)/PMA/De Novo/HDE/PDP pathways. This lands directly
  on the clinical-trials-plus-device-telemetry proving-ground domain,
  not a hypothetical concern.
- **SOUP** (Software of Unknown Provenance): [IEC 62304](https://openregulatory.com/document_templates/soup-list-software-of-unknown-provenance)
  (medical device software life-cycle processes) requires every
  off-the-shelf software item **not developed specifically for the
  medical device** — operating systems, open-source libraries,
  third-party networking stacks, cloud APIs — to be documented and risk-
  assessed: version, known anomalies, and the functional requirements it
  fulfills. By this definition, **every library in `docs/libraries/` is
  SOUP** the moment this framework or a derivative is used in a medical-
  device software context.

Decision:
- **SBOM generation via [`microsoft/sbom-tool`](../libraries/dotnet/sbom-tool.md)**
  — Microsoft's own, open-sourced, SPDX 2.2-compatible generator,
  consistent with `ADR-041`'s first-party preference. Chosen specifically
  because it auto-detects **both** NuGet and npm dependency graphs in
  one pass — this design spans exactly those two ecosystems (the .NET
  engine, the Vue/TypeScript client) — rather than needing two separate
  tools. Run at build/release time against the actual resolved
  dependency graph, not hand-maintained — an SBOM that's manually
  curated and drifts from what's actually shipped defeats its own
  purpose.
- **`docs/libraries/README.md`'s existing catalog is formalized as this
  project's SOUP list** — not a new, parallel document. It already
  carries most of what IEC 62304 requires (name, what it's for, where
  it's adopted); it gains, over time, the fields IEC 62304 specifically
  asks for and the SBOM doesn't capture on its own: known anomalies,
  and the functional requirement(s) each library fulfills in this
  design specifically. Retrofitting every existing entry with full
  IEC 62304 detail is real, substantial work — not done this pass,
  flagged as remaining propagation work the same way other large
  retrofits in this design are named rather than silently deferred.
- **SBOM and SOUP list are complementary, not duplicates, stated
  explicitly so the distinction isn't lost**: the SBOM is an automated,
  machine-readable, point-in-time *fact* about what's actually built
  (generated, never hand-edited); the SOUP list/library catalog is a
  human-curated *risk assessment and rationale* (why each was chosen,
  what could go wrong, what it's relied on to do) — the SBOM answers
  "what," the SOUP list answers "why, and is that acceptable."

Consequences:
- `06-solution-structure.md` gains an SBOM-generation step in the
  build/release pipeline — not yet detailed, flagged as remaining
  propagation work.
- This is now a real, standing discipline going forward, not a one-time
  artifact: every future `docs/libraries/{platform}/{library}.md`
  addition should be understood as also adding a SOUP-list entry, the
  same way `CLAUDE.md` already treats that folder as a living catalog
  that must stay in sync.
- No new dependency for the .NET side beyond the `sbom-tool` itself
  (a build-time tool, not a runtime dependency) — consistent with this
  design's preference for build/release-time tooling over anything
  shipped in the running application.
