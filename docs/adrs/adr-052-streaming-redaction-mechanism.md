[← ADR index](../07-adrs.md)

# ADR-052: Streaming-channel redaction mechanism — configurable strategy, read-time, zero-fill default

Status: Accepted — formalizes [`docs/comparisons/streaming-redaction-mechanism.md`](../comparisons/streaming-redaction-mechanism.md)

Context: `ADR-031` named `RedactedRange`'s shape (`ChannelId`,
`FromTimestamp`, `ToTimestamp`, `RequiredClaim`) and explicitly deferred
the substitution mechanism itself. The comparison searched real prior
art (SWGDE audio/video redaction guidance, ONVIF `PrivacyMask`, SCTE-35,
differential privacy) and narrowed the fork without forcing a premature
pick. Direction received this session confirms the two open pieces:
zero-fill is fine as the `RawScalar`/`RawBinary` default, and the
redaction *content* should be configurable — generalizing beyond a
single fixed substitution, up to and including a format-preserving
partial reveal (e.g. an SSN-shaped value rendered `XXX-XX-1234`) where
the channel's content shape actually supports it.

Decision:
- **Read-time, not materialized** — the comparison's confident pick:
  `RedactedRange`s on one channel can carry different `RequiredClaim`s
  held by different, non-overlapping caller populations, so there's no
  single canonical "the" redacted view to precompute (`ADR-028`'s
  downcast shape — unbounded targets — not `ADR-027`'s upcast shape —
  one target).
- **Default substitution per `ContentKind`, confirmed**: zero-fill (a
  run of `0x00` matching sample count/cadence) for `RawScalar`/
  `RawBinary`; a distinctive tone (not silence — SWGDE's own reasoning:
  "a redacted segment can be confused with original content that
  contains silence") for audio; a blank/black frame for video. These
  require zero domain-specific knowledge of the channel's real content,
  matching `ADR-030`'s core-engine constraint.
- **`RedactedRange` gains a configurable `Strategy` field, reusing
  `ADR-009`'s masking-strategy taxonomy rather than inventing a second
  one**: `Strategy` defaults to the `ContentKind`-appropriate
  substitution above, and can instead be set to `PartialReveal` —
  **the same strategy this ADR promotes out of `ADR-009`'s "proposal,
  not decided" section into a real, built strategy** (see that ADR's
  own update) — for channels/content whose shape actually supports a
  format-preserving partial reveal. This is a real, content-shape-
  dependent option, not universal: it fits a `RawBinary` channel
  carrying structured, string-like records (an SSN-shaped field
  embedded in a binary record, revealed as `XXX-XX-1234`) the same way
  it fits a JSON property; it does **not** meaningfully apply to a pure
  numeric waveform or a video/audio `Media` frame — there's no
  "format-preserving partial reveal" of a raw signal or a video frame,
  so those `ContentKind`s keep their zero-fill/tone/blank-frame default
  as the only real option.
- **Statistical-noise substitution stays an opt-in `Derived`-channel
  `TransformKind`**, not a core-engine default — calibrating noise to a
  channel's real distribution requires exactly the domain-specific
  knowledge `ADR-030` keeps out of the core engine; an application that
  wants it supplies its own calibration, the same "detection is an
  application concern" carve-out `ADR-031` already uses.
- **Resolved through the same Strategy-pattern seam `ADR-009` uses, not
  a duplicate mechanism** — a sibling `IStreamRedactionStrategy`
  interface (operating over raw sample/frame bytes rather than a
  `JsonNode`, since that's a genuinely different value shape — the two
  are parallel implementations of the same pattern, not literally one
  shared interface), keyed-registered identically
  (`AddKeyedSingleton<IStreamRedactionStrategy, ZeroFillStrategy>
  ("Default")`, one per `ContentKind`'s default plus `"PartialReveal"`).
  The `"PartialReveal"` key reuses `ADR-009`'s
  `PartialRevealMaskingStrategy` reveal computation directly where a
  channel's content is structured/string-shaped; `ZeroFillStrategy`/
  `ToneStrategy`/`BlankFrameStrategy` are new implementations
  `IPayloadMasker` never needs. Adding a channel-specific redaction
  option later is, again, a new class plus one registration line.
- **The existence-signal requirement is not optional**: every
  `RedactedRange` application also sets a sideband flag at the read/
  tail/replay boundary — structurally the same shape
  `TelemetrySample.LateArrivalFlag` already uses — so a caller lacking
  the claim still learns *that* redaction applied at this position,
  never relying on the substitution content alone to be self-evidently
  fake. This is `ADR-009`'s "gates the value, never the existence"
  posture, applied here rather than merely gestured at.

Consequences:
- `docs/data/streaming-and-attachments.md` gains the `RedactedRange`
  entity (it was named in `ADR-031` but never actually added to the
  data model) with the `Strategy`/reveal-pattern fields above — done
  this pass.
- **Whether a bounded "public" materialized view is worth building as a
  pure performance optimization remains genuinely open** — it depends
  on how many distinct claim populations a real deployment's
  `RedactedRange`s produce, a build-time fact this ADR can't settle in
  the abstract. Not a blocker; revisit once real deployment shape is
  known.
- Resolves and removes the open question `docs/10-open-questions.md`
  tracked for the redaction mechanism itself; the `PartialReveal`
  promotion also narrows (not fully resolves) the separate
  masking-strategies open question — see `ADR-009`'s update.

**Compliance note** (a proving-ground compliance review, this session):
the video channel's blank-frame default addresses a real, named HIPAA
identifier — "full-face photographic images and comparable images" is
#17 of the 18 Safe Harbor identifiers (45 CFR § 164.514(b)(2)) — meaning
claim-gated video redaction isn't just a privacy nicety here, it's
covering an identifier class HIPAA explicitly names as needing removal
for de-identification.
