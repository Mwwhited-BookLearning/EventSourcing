[← Comparisons index](README.md)

# Streaming-Channel Redaction Mechanism: substitution content per `ContentKind`, and materialized vs. read-time

**Not yet decided.** `ADR-031` named the `RedactedRange` shape
(`ChannelId`, `FromTimestamp`, `ToTimestamp`, `RequiredClaim`) and
explicitly stopped there: "a full ADR if/when it's actually built." This
comparison does the prior-art search and lays out the fork that ADR will
need to resolve — it doesn't force a premature pick where the evidence
doesn't cleanly support one, following this project's own precedent
(`upcast-transform-language.md`). Tracked in `docs/10-open-questions.md`
until the queued streaming-redaction ADR actually gets written.

**Stated requirement driving this comparison:** `ADR-031`'s own Decision
text already commits to two constraints before this comparison starts:

1. Redaction must keep the same **claims-gate-the-*value*-never-the-
   *existence*** posture `ADR-009` established for JSON payload masking —
   "just with a redaction *result* appropriate to binary content instead
   of a JSON wrapper," in `ADR-031`'s own words. Whatever mechanism this
   comparison lands on has to actually honor that, not just gesture at it.
2. It has to work generically across all three `ContentKind`s
   (`RawScalar`, `RawBinary`, `Media`) without the core engine acquiring
   domain-specific knowledge of any one channel's content — the same
   "zero domain-specific knowledge" principle (`ADR-030`) `ADR-031`
   already invoked for detection, and which applies here with equal
   force.

## Prior art searched

- **Body-worn-camera / law-enforcement video-and-audio redaction** is a
  real, mature, well-documented industry practice, not a from-scratch
  problem: SWGDE's *Video and Audio Redaction Guidelines*
  (SWGDE M-18-001, current as v2.3) is the closest thing to a governing
  standard, written by the Scientific Working Group on Digital Evidence
  for exactly this use case (body cams, in-car video, 911 calls, jail
  calls). It's process/best-practice guidance, not a wire format or file
  spec — but it's specific about substitution content, and its stated
  reasoning matters directly here (below).
- **Live-camera privacy masking has a real formal spec**: the [ONVIF
  Media 2 Service Specification](https://www.onvif.org/specs/srv/media/ONVIF-Media2-Service-Spec-v1612.pdf)
  defines `PrivacyMask` — a polygon region, in normalized coordinates,
  overlaid on a live video source's stream. It's a genuine standard, but
  it solves a *different* shape of problem than `RedactedRange`: a
  spatial mask (blur/blackout a region *within* every frame, e.g. a
  bystander's face) baked in at the source for every viewer alike — not
  a temporal, per-viewer-claim-gated span. Real, but not directly
  reusable.
- **No formal codec- or container-level standard defines a
  "redacted region" a decoder/player is expected to substitute for.**
  H.264/H.265 Region-of-Interest (ROI) signaling and ISOBMFF's ROI boxes
  are real and standardized, but for a different purpose entirely —
  telling an *encoder* which regions deserve more bits for quality,
  nothing to do with privacy or access control. Searched specifically
  for this and came up empty: this is a case where nothing formal exists
  for the actual problem, worth stating plainly rather than forcing a
  citation to something adjacent.
- **The closest formal analogue for *signaling* a span for
  entitlement-based substitution is broadcast, not access control**:
  [SCTE-35](https://en.wikipedia.org/wiki/SCTE-35) (Digital Program
  Insertion Cueing Message, ANSI/SCTE-recognized) is the real,
  standardized in-band signaling mechanism cable/streaming distributors
  use for regional blackouts — a `time_signal`/segmentation descriptor
  marks a span, and downstream systems decide what to do with it
  (hide the feed, splice in local content). It's a genuine, real-world
  precedent for "a standard exists for marking *where* to substitute,"
  but it explicitly leaves *what* to substitute and *who* it applies to
  entirely to downstream/receiver-side systems — it doesn't define the
  redaction transform itself, which is exactly the gap this comparison
  has to fill.
- **Differential privacy** (Dwork, McSherry, Nissim, Smith — *Calibrating
  Noise to Sensitivity in Private Data Analysis*, TCC 2006) is the real,
  formally-defined framework behind statistical-noise substitution: an
  algorithm is `ε`-differentially-private if, for neighboring datasets
  differing by one element, no output is more than a factor of `e^ε`
  more likely — achieved by adding Laplace- or Gaussian-distributed
  noise calibrated to the query's sensitivity. Time-series anonymization
  literature applies the same additive-noise idea directly to sensor/
  telemetry spans, and separately discusses zero-fill (replacing a span
  with zeros) as a simpler, cruder alternative with more information
  loss — both are real, named techniques, not invented for this
  comparison.

## Fork 1 — what to substitute, per `ContentKind`

### `RawScalar` / `RawBinary` — zero-fill vs. statistical-noise substitution

| | |
|---|---|
| **Option A — zero-fill** (a run of `0`/`0x00` matching the original sample count and cadence) | **Pros:** Requires zero knowledge of the channel's actual signal — a memset-shaped operation over a byte range, computable identically whatever `SampleType`/byte layout a channel declares, satisfying `ADR-030`'s zero-domain-knowledge constraint outright. Preserves sample count and timing exactly (a `Derived` channel's resampler/filter still sees the expected cadence, just with zeroed input over the redacted span). **Cons:** A run of exact zeros can, for some signals, be indistinguishable from a legitimate real zero reading (a voltage sensor genuinely reading `0V`, a `RawBinary` chunk whose real content happens to be all-zero bytes) — the same ambiguity SWGDE's guidance explicitly names for audio silence (below), just for scalar data instead of sound. |
| **Option B — statistical-noise substitution** (Laplace/Gaussian noise calibrated to the channel's own observed distribution, differential-privacy-style) | **Pros:** Avoids the "looks like a real reading" ambiguity differently — visibly noisy rather than suspiciously flat — and, for a `Derived` channel applying a filter/resampler across the redacted span, avoids the abrupt-discontinuity artifact a hard zero-run can introduce at the span's edges (a bandpass filter, for instance, can ring on a sudden drop to zero). **Cons:** Requires the transform to know something about the channel's real distribution to calibrate noise sensibly — which is exactly the domain-specific knowledge `ADR-030`/`ADR-031` keep out of the core engine on principle. It also risks the opposite failure from zero-fill: *plausible-looking* fake data is arguably worse for a security-sensitive redaction primitive than *obviously* fake data, since a downstream consumer with no reason to check for redaction could mistake it for degraded-but-real signal. |

### `Media` (audio/video) — silence/tone vs. blank/black frame, and a scope disambiguation

Audio and video need to be split from each other here, and both need one
disambiguation the search surfaced clearly:

- **Audio — silence vs. a distinctive tone.** SWGDE's guidance is
  specific and comes down against silence: *"a redacted segment can be
  confused with original content that contains silence"* — recommending
  a consistent, distinctive tone instead, reserving silence for the
  narrower case of multi-channel audio where a tone would mask other,
  non-redacted channels. This is the *exact same ambiguity* `ADR-009`
  already solved for JSON payloads by making `masked`/absent
  distinguishable from a real value rather than collapsing both into
  `null` — real-world audio-redaction practice independently arrived at
  the identical principle for a completely different medium. `ADR-031`'s
  own Decision text named "silence" first; this is a case where the
  prior-art search should override that first-draft framing, not just
  supplement it.
- **Video — blank/black frame.** Here the ambiguity concern is much
  weaker: a black frame is unambiguous in a way silence isn't, since
  almost no real video content is *actually* solid black for an extended
  span the way real audio content is often actually silent. `ADR-031`'s
  "blank frames" framing holds up.
- **The scope disambiguation**: nearly every commercial redaction tool
  the search turned up (Axon Redact, CaseGuard, VIDIZMO, Reduct.video,
  and the AI-driven face/plate-tracking tools SWGDE's own guidance
  discusses) solves a **spatial** problem — blur or box out a *moving
  region within* an otherwise-visible frame, tracked across time. That's
  a genuinely harder problem (object tracking across frames) than what
  `RedactedRange`'s shape actually describes: `RedactedRange` has no
  spatial bounding box, only `FromTimestamp`/`ToTimestamp` — it's a
  **temporal**, whole-frame redaction (black out the *entire* frame for
  a span), not a spatial one (blur *part of* each frame). Worth stating
  plainly: the bulk of the industry tooling this search found solves a
  different, harder problem than the one this design's field shape
  actually supports. If spatial (in-frame) redaction is ever wanted, that
  needs its own field shape (a bounding box, most likely per-frame or
  per-keyframe) — genuinely out of scope for the shape as named, not
  silently covered by it.

## Fork 2 — materialized (`ADR-027`-style) vs. read-time (`ADR-028`-style)

| | |
|---|---|
| **Option A — materialize the redacted view**, the same shape `ADR-027` uses for upcasts: compute the substitution once, persist it as a parallel record. | **Pros:** No repeated compute cost on every read. **Cons:** `ADR-027`'s materialization works because an upcast has exactly one legitimate target — "the" current schema version — so there's one thing to build. Redaction doesn't have that property in general: different `RedactedRange`s on the same channel can carry different `RequiredClaim`s held by different, non-overlapping populations of caller, so "the" redacted view isn't singular — it's potentially one distinct view per claim combination a real caller population actually holds. That's structurally `ADR-028`'s downcast problem (unbounded targets), not `ADR-027`'s upcast problem (one target), even though the shape looks more like a copy-and-persist operation on the surface. |
| **Option B — compute the redacted view fresh, per caller, at read/tail/replay time**, the same shape `ADR-028` uses for downcasts, and the same "claims fixed for the connection's lifetime, checked once at connect" pattern `ADR-009` already uses for masking. | **Pros:** Matches `ADR-028`'s own reasoning for exactly this "unbounded targets" shape — compute fresh because there's no single canonical output to precompute. The actual transform for either substitution option above (a byte-range memset, or a bounded noise draw) is far cheaper than `ADR-018`/`ADR-028`'s arbitrary declarative upcast/downcast expression evaluation, so the performance argument that would otherwise favor materialization barely applies — there's much less to gain from precomputing here than there was for upcast. **Cons:** Still real, repeated per-read cost, non-zero at high sample rates — just a small one relative to the alternative it's being weighed against. |

One nuance worth naming honestly rather than collapsing into the table:
unlike upcast/downcast, redaction already gets a "materialization" for
free on one side — the unredacted original `TelemetrySample` rows are
exactly what a claim-holding caller should see, no work needed. The only
real question is whether a *redacted* view is *also* worth precomputing
for the common no-claim case. That's bounded (and worth doing, as a pure
performance optimization layered on top of Option B — the same
"optional, not a correctness requirement" framing `ADR-027`'s Follow
integration already uses for materializations) *if* a channel's
`RedactedRange`s in practice converge on one shared "public" claim tier;
it's unbounded again (back to the downcast shape) if they don't. That's
a deployment-shape fact this comparison can't settle in the abstract —
worth a build-time check, not a guess now.

## A requirement the prior art search surfaced, not just an option

Every substitution option above — including the least ambiguous ones —
still needs the caller to be able to tell "this span was redacted" apart
from "this is genuinely what the data looks like here," for the same
reason `ADR-009`'s wrapper shape exists at all: a caller who can't tell
the two apart can't safely act on either. SWGDE's own stated reason for
preferring a tone over silence is this exact problem, discovered
independently in a completely different medium. Whatever the eventual
ADR picks for substitution *content*, it should pair it with an explicit,
out-of-band existence signal at the read/tail/replay response boundary —
structurally the same shape `TelemetrySample.LateArrivalFlag`
(`ADR-029`/`ADR-031`) already uses (a boolean riding alongside the value,
computed by the store rather than trusted from the producer) — so a
caller lacking the claim still learns *that* a `RedactedRange` applied at
this position, even though the content itself stays substituted. This
keeps the mechanism honest to `ADR-009`'s "gates the value, never the
existence" posture in the same way the JSON wrapper's `masked`/`value`
branches already are, rather than relying on the substitution content
alone to be "obviously fake enough" — which, per the options above, isn't
reliably true for every `ContentKind`.

## Recommendation

**Read-time (Option B, Fork 2)** is the confident pick: it's the same
shape `ADR-028` already chose for the same reason (unbounded targets),
and the cost argument that would favor materialization is unusually weak
here given how cheap the actual transform is. A bounded "public"
materialization layered on top, later, as a pure performance
optimization, is worth revisiting once real deployment shape is known —
not a blocker now.

**Zero-fill for `RawScalar`/`RawBinary`, at the core-engine level** is
the right default for the same reason CEL won the upcast-language
comparison: narrowness matched to the actual constraint
(`ADR-030`'s zero-domain-knowledge principle rules out noise calibrated
to a signal's real distribution, which requires exactly the domain
knowledge the core engine isn't supposed to have). Statistical-noise
substitution stays a legitimate, real technique — worth offering as an
opt-in `Derived`-channel `TransformKind` for an application that
specifically wants it and is willing to supply its own calibration, the
same "detection is an application concern" carve-out `ADR-031` already
uses — not something the core engine does by default.

**Tone (or another distinctive, non-silent marker) over plain silence
for audio**, reversing `ADR-031`'s own first-draft "silence" framing,
specifically because SWGDE's real, cited reasoning is the audio-domain
instance of a problem this design already solved once (`ADR-009`) and
shouldn't solve inconsistently the second time. **Blank/black frame for
video** stands as `ADR-031` already named it — the ambiguity concern
that overturns silence for audio doesn't apply with comparable force to
video.

**The existence-signal requirement is not optional** — whichever
substitution content the eventual ADR locks in, it needs a sideband
"redaction applied here" flag at the read boundary, or the mechanism
doesn't actually honor `ADR-009`'s already-established posture, just
resembles it.

**What's still genuinely open, honestly**: whether a "public"
materialized redacted view is worth building depends on a real-world
fact (how many distinct claim populations a deployment's `RedactedRange`s
actually produce) this comparison can't observe in advance — flagged
above, not glossed over, the same way `upcast-transform-language.md`
flagged its own build-time-dependent tension rather than forcing a
premature answer.
