[← ADR index](../07-adrs.md)

# ADR-080: Dependency-vulnerability scanning and build-signing/provenance, on top of `ADR-074`'s SBOM generation

Status: Accepted

Context: `docs/10-open-questions.md` asked whether this design should
adopt dependency-vulnerability scanning and a build-signing/provenance
standard on top of `ADR-074`'s SBOM generation — `ADR-074` only covers
*producing* an SBOM and the SOUP catalog; nothing decided how a
discovered vulnerability gets surfaced, or whether a published package
carries verifiable proof of where/how it was built. Direct answer
received this session: **yes**, adopting the specific tools/standards
the question itself named, each verified before adopting.

Decision:
- **Dependency-vulnerability scanning — layer a hosting-platform feature
  with first-party, SDK-native commands, no new tool to run locally:**
  Dependabot (GitHub-native dependency alerts + auto-remediation PRs,
  covering both `ADR-062`'s NuGet and npm ecosystems) at the repository-
  hosting layer, plus `dotnet list package --vulnerable` (built into the
  .NET SDK) and `npm audit` (built into npm) as commands any contributor
  or CI job can run directly — consistent with `ADR-041`'s first-party
  preference, no bespoke scanning pipeline.
- **NuGet build signing: author package signing via a registered X.509
  certificate.** Every published NuGet package is signed with a
  certificate (RSA, 2048-bit minimum, chained to a trusted root)
  registered to this project's own NuGet.org account — verified: NuGet.org
  validates a signed package's certificate against the author's
  registered one at submission time and rejects self-issued
  certificates, so this isn't optional theater once registered.
- **npm build provenance: `npm publish --provenance`, Sigstore-backed,
  SLSA-shaped.** Verified: this generates a signed attestation (source
  repo URI, commit hash, build instructions) logged to Sigstore's public
  Rekor transparency log, and requires publishing from a supported CI
  provider — **currently GitHub Actions or GitLab CI/CD only**, not an
  arbitrary CI platform. This is a real, narrowing consequence for
  `docs/10-open-questions.md` row 5's still-deferred CI-platform choice
  — noted there, not resolved by this ADR.
- **SLSA target: Level 2 now, Level 3 as a named future escalation, not
  decided yet — the same staged-adoption shape `ADR-063` already applies
  to distributed-correctness testing.** Verified SLSA's own level
  definitions: **Level 1** is unsigned provenance (a record, not a
  guarantee); **Level 2** adds *signed* provenance plus dedicated build
  infrastructure — exactly what `npm publish --provenance` and NuGet
  author signing already give, together, once CI publishes from a
  supported provider; **Level 3** additionally requires hermetic,
  ephemeral build environments — a real infrastructure commitment this
  design isn't ready to make while `docs/10-open-questions.md` row 5's
  CI platform/environment-promotion path remains deliberately
  back-burnered. Level 2 is the adopted target; Level 3 is named as the
  next escalation, not committed to now.
- **Directly relevant to `ADR-074`'s own FDA Section 524B compliance
  driver** — Section 524B's premise is that a device manufacturer's SBOM
  exists specifically so a discovered vulnerability *can* be tracked and
  remediated; `ADR-074` supplied the SBOM half, this ADR supplies the
  half that was still missing (the actual scanning/remediation loop).

Consequences:
- No new tool to build — Dependabot, `dotnet list package --vulnerable`,
  `npm audit`, NuGet author signing, and `npm publish --provenance` are
  all off-the-shelf, already-shipped capabilities; adopting this ADR is
  a configuration/process commitment, not new framework code.
- Real, narrow dependency on `docs/10-open-questions.md` row 5: whichever
  CI platform is eventually chosen must support npm provenance
  (currently GitHub Actions or GitLab CI/CD) for this ADR's npm half to
  actually work as decided — flagged there, not forcing row 5's
  resolution.
- Resolves `docs/10-open-questions.md` row 6.

**Compliance note**: beyond `ADR-074`'s own FDA Section 524B driver
(above), this ADR is directly relevant to the SLSA-provenance/dependency-
tracking expectations several of this design's regulated proving-ground
domains carry (medical-device software supply chain for clinical
trials/device telemetry; financial-services vendor risk management for
KYC's relying parties) — a supporting control, not a new compliance
driver of its own.
