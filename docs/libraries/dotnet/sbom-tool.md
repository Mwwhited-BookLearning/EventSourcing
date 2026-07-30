[← Libraries index](../README.md)

# microsoft/sbom-tool (dotnet)

**What it's for:** Microsoft's own open-source Software Bill of Materials
generator ([microsoft/sbom-tool](https://github.com/microsoft/sbom-tool))
— scans a build's actual dependency graph (via the same **Component
Detection** libraries used elsewhere in Microsoft's OSS tooling) and
produces a machine-readable [SPDX](https://spdx.dev/) 2.2 (or 3.0) SBOM
document, ready to submit or publish alongside a release.

**Why bought, not built:** an SBOM's entire value depends on it
faithfully reflecting what was *actually* built, not what a human
remembers to list — hand-maintaining one drifts from reality the moment a
dependency is bumped and defeats its own purpose. `sbom-tool` is
Microsoft's own first-party tool (`ADR-041`'s preference), auto-detects
**both** NuGet and npm dependency graphs in one pass — exactly the two
ecosystems this design spans (the .NET engine, the Vue/TypeScript client)
— rather than needing one tool per ecosystem.

## General usage

```bash
# Installed as a .NET global tool
dotnet tool install --global Microsoft.Sbom.DotNetTool

# Run against a build's output/drop folder
sbom-tool generate \
  -b ./artifacts/publish \
  -bc . \
  -pn EventStore.Core \
  -pv 1.4.0 \
  -ps "EventStore Project" \
  -nsb https://example.org/eventstore
```

Run in CI at build/release time, against the actual resolved dependency
graph — never hand-edited afterward.

## Where this project uses it

`ADR-074` — generates an SPDX SBOM at release time, satisfying Executive
Order 14028 generally and, concretely, FDA Section 524B's mandatory SBOM
for a "cyber device" premarket submission — directly relevant to the
clinical-trials-plus-device-telemetry proving-ground domain. Complements,
rather than duplicates, this same catalog's role as the project's IEC
62304 SOUP list (`docs/libraries/README.md`) — the SBOM is the automated
"what's actually built" fact; this catalog is the human-curated "why, and
what's the risk" rationale.

## Links

- [github.com/microsoft/sbom-tool](https://github.com/microsoft/sbom-tool)
- [spdx.dev](https://spdx.dev/) (the SBOM format it produces)
