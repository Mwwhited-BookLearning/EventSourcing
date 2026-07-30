# Protocol: verify before citing / search for prior art before designing

This project's single most repeated standing instruction, stated
explicitly more than once in `CLAUDE.md`. Applies to every ADR, pattern
doc, comparison doc, domain doc, and glossary entry — not just ones
that obviously touch a named standard.

## The rule

1. **Before designing anything new**, search for real prior art —
   RFCs, standards, or a commonly-named practice — even when the
   request doesn't name one. Two clear examples from this session:
   `ADR-040`'s ticket-exchange mechanism was designed only after
   searching turned up CAS service tickets, RFC 7662 introspection, and
   CDN signed-URL conventions, none of which the original request
   named; the "silo vs. pool multi-tenancy" decision was grounded in
   AWS/Azure's own real terminology before anything was decided.
2. **Before citing a spec/standard/library by name or number**, verify
   it via WebFetch/WebSearch against the real source — don't recall
   from memory and assume correct. This includes pattern names
   (confirm "Idempotent Receiver" is really Hohpe/Woolf's term before
   citing it that way), RFC numbers (confirm RFC 9449 is really DPoP
   before writing "RFC 9449 (DPoP)"), and library claims (confirm
   NWebDav is really archived before citing that as a reason not to
   adopt it).
3. **A close-call synonym or terminology overlap gets checked, not
   assumed.** If two terms LOOK equivalent, verify they're true
   synonyms before treating them as such — this session found several
   that weren't (KYC vs. CDD, Clearinghouse vs. CCP, De-identification
   vs. Anonymization). If verification comes back "related but
   distinct," say so explicitly in the doc rather than silently picking
   one reading.
4. **If you can't verify something confidently, don't write it down.**
   Leave it out and say so in your own report/summary — a missing
   citation is honest; a wrong one erodes trust in every other citation
   in the doc.

## When this catches a real product/library

Adopting something real (a library, a standard) gets a
`docs/libraries/{platform}/{library}.md` write-up (buy-over-build) or a
`references.md` row (adopted, with where it's used). Rejecting
something real still gets a `references.md` row (reference-only, with
the specific reason) — so a later reader doesn't wonder whether it was
overlooked. A rejection can later flip to adopted if a new requirement
removes the original reason for rejecting it (this has happened
multiple times this session — SPIFFE/SPIRE, YARP, content-addressable
storage all went reference-only → adopted once a real trigger
appeared) — check for a stale rejection before assuming something is
still out of scope.

## Delegating verification to a background agent

When the verification workload is large (several candidate
citations, or a whole domain's worth of terminology), dispatch a
research-only agent with explicit instructions: use WebSearch/WebFetch,
report exactly which claims were verified vs. which were left out for
lack of confidence, and never edit files directly if the task is
research rather than authoring — keep research and writing as separate
steps when the volume warrants it, so a hallucinated citation can't
slip straight into a design doc without a checkpoint.
