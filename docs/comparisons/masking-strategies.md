[← Comparisons index](README.md)

# Masking Content Strategies: Partial Reveal vs. Format-Preserving vs. Generalization/Bucketing vs. Tokenization

**Decided.** Raised by `ADR-009`'s own "Future: definable masking
strategies" section and `docs/10-open-questions.md`'s masking-strategies
row (now removed — resolved, not just narrowed). `FixedValue`,
`PartialReveal`, and keyed `Hash` are decided and built (`ADR-009`, shared
by `ADR-052`); format-preserving encryption, generalization/bucketing, and
tokenization are all **declined**, applying KISS — no stated need for any
of the three, and each would add real surface (key management, a fourth
strategy class, or a whole second component) for a requirement that
hasn't shown up. See the Recommendation below for why each specifically.

**Stated requirement driving this comparison:** `ADR-009` originally
shipped exactly one masking-content strategy, `"FixedValue"` (a
configured literal string, default `"***"`), inside a claims-gated
`{"value": ...}` / `{"masked": ...}` wrapper that is a **read-time
presentation transform, never a storage-layer change** — `Payload` is
persisted and published exactly as received, forever; masking only
affects what a query/stream response serializes back. Two follow-on
strategies, originally named as a future proposal, have since been
promoted into the Decision: `"PartialReveal"` (named, human-readable
prefix/suffix reveal — `showFirst`/`showLast`/`maskChar`/
`preserveSeparators`) and `"Hash"` (a *keyed* HMAC via
`Microsoft.Extensions.Compliance.Redaction`'s `HmacRedactor`, letting a
caller correlate masked values across events without ever seeing the
real one — keyed specifically so a small value space, like a 9-digit
SSN, can't be reversed by brute-forcing every possibility against a bare
hash). Both fit the existing wrapper unchanged — "only the content of
`masked` changes, never the shape." Any option still considered here has
to be checked against that same bar: does it still fit inside a
plain-string `masked` slot computed as a pure function of `(schema, one
payload)` with no I/O, or does it need something genuinely new?

## Prior art

Real-world data-masking taxonomies distinguish several formal terms this
design has so far used loosely (`ADR-009` calls the whole feature
"masking"); worth disambiguating before comparing options, per this
project's own "disambiguate terminology collisions" convention:

- **Redaction** — outright removal/blackout of a value with no attempt to
  preserve statistical usefulness. [NIST SP 800-188 (Sept. 2023),
  "De-Identifying Government Datasets: Techniques and
  Governance"](https://csrc.nist.gov/pubs/sp/800/188/final) treats bare
  redaction as insufficient on its own for a formal privacy guarantee.
  `ADR-009`'s `FixedValue` strategy is, in this vocabulary, a redaction —
  it discloses nothing about the underlying value.
- **Pseudonymization** — per [GDPR Article 4(5)](https://gdprlocal.com/data-pseudonymisation-vs-anonymisation/)
  and NIST's aligned definition, processing that replaces an identifier
  with a substitute *while keeping a separately-held, access-controlled
  means of reversing it* — explicitly still personal data under GDPR
  (Recital 26), because reversal remains possible "by the use of
  additional information." `ADR-009`'s claims-gated `{value}` branch is
  arguably closer to *access control on the original value* than true
  pseudonymization: there is no separate pseudonym artifact stored
  anywhere, just two branches of the same read.
- **Anonymization** — de-identification intended to be irreversible; no
  additional information exists anywhere that would let the record be
  re-attributed to a subject. Nothing in this design's masking feature
  claims this — the real value is retained, always, by design (`ADR-009`'s
  no-deletion decision) — so none of the options below are anonymization
  either, regardless of strategy.
- **Static vs. dynamic data masking** — an industry-standard split (see
  e.g. [SQL Server's Dynamic Data Masking](https://learn.microsoft.com/en-us/sql/relational-databases/security/dynamic-data-masking)):
  *static* masking produces a separately-stored, permanently sanitized
  copy (for non-prod/QA); *dynamic* masking computes the masked view
  per-query against the one real copy, for callers who lack a privilege.
  `ADR-009` is entirely the dynamic kind — there is no second, sanitized
  copy of `Payload` anywhere, consistent with the append-only,
  single-authoritative-copy principle this whole design already commits to.

With that vocabulary settled, the options below are content strategies for
what the *dynamic* mask actually contains, not different privacy models:

## The options

### Option A — Configurable partial reveal (prefix/suffix)

Reveal a configurable number of characters from the start and/or end of
the real value, masking the middle — e.g. "show last 4 digits of a card
number," "show first character of a name." The best-known concrete
precedent is [SQL Server's Dynamic Data Masking `Partial(prefix,
[padding], suffix)` function](https://www.mssqltips.com/sqlservertip/7887/dynamic-data-masking-in-sql-server-for-sensitive-data-protection/)
— `Partial(0,"XXXXXXXXXXXX",4)` for "mask everything except the last 4
digits." This generalizes `ADR-009`'s own already-sketched
`"PartialReveal"` (which only described "keep the last N characters") to
a symmetric prefix-*and*-suffix rule, matching what real DDM products
actually expose.

| | |
|---|---|
| **Added cost over `FixedValue`** | Small. The transform is still a pure function of `(configured prefix count, suffix count, padding char, real value)` — no key material, no external state, no new storage. The only new surface is two or three extra `x-masking` config fields (`revealPrefix`, `revealSuffix`, optional `paddingChar`) and a length-guard (SQL Server's own rule: if the value is too short for prefix+suffix, reveal nothing) that needs the same explicit call here. |
| **Fits `ADR-009`'s wrapper?** | Cleanly. The result is still a plain string dropped into the existing `masked` slot — no shape change, no new enforcement point, no change to the claims-checking primitive. This is the smallest possible increment beyond `FixedValue`. |

### Option B — Format-preserving masking

Keep the value's *shape* (same length, same digit/letter pattern) but
scramble the actual characters — "a credit-card-shaped string that isn't
the real card number," digit-for-digit. The real-world version of this is
**format-preserving encryption (FPE)**: [NIST SP 800-38G (final, updated
2016)](https://csrc.nist.gov/pubs/sp/800/38/g/upd1/final), "Recommendation
for Block Cipher Modes of Operation: Methods for Format-Preserving
Encryption," standardizes two AES-based modes, FF1 and FF3(-1), exactly
for this — encrypting a PAN or SSN into a same-length, same-alphabet
string. A non-cryptographic, non-reversible variant (deterministic
per-character substitution with no real key) is also possible and
strictly simpler, at the cost of no formal security guarantee.

| | |
|---|---|
| **Added cost over `FixedValue`** | Real, if done as actual FPE: a symmetric key (and, per FF1/FF3-1, a "tweak") has to be generated, stored, and rotated somewhere — this design has no key-management primitive anywhere else today, so this would be the first one. The non-cryptographic substitution variant avoids that but then isn't actually format-preserving *encryption*, just format-preserving *obfuscation* — worth being explicit about which is meant, since the two have very different guarantees. |
| **Fits `ADR-009`'s wrapper?** | Cleanly, mechanically — the output is still one string in the `masked` slot, computed from `(configured mode/key reference, real value)`, still a pure function with no ambient I/O if the key is passed in rather than fetched. The wrapper doesn't need to change at all. What's new is entirely the key-management surface *around* the transform, not the transform's shape. |

### Option C — Generalization / bucketing

Replace an exact value with a coarser, less specific one that's still
semantically consistent — exact age → age range (`"30-39"`), exact ZIP →
region, exact timestamp → day/week. This is the mechanism from the
statistical-disclosure-control literature, most associated with
**k-anonymity** ([Sweeney, "Achieving k-Anonymity Privacy Protection Using
Generalization and Suppression," *International Journal of Uncertainty,
Fuzziness and Knowledge-Based Systems*, 2002](https://www.worldscientific.com/doi/abs/10.1142/S021848850200165X)),
which formally guarantees each released record is indistinguishable from
at least *k*-1 others on the generalized fields.

| | |
|---|---|
| **Added cost over `FixedValue`** | Moderate, and split in two importantly different pieces. **Single-value bucketing** (map this one field's value into a configured bucket — "30-39" for an age between 30 and 39) is cheap: still a pure function of `(configured bucket boundaries, real value)`, no cross-record state. **The formal k-anonymity guarantee** the technique is usually cited for is a fundamentally different, much bigger thing: it requires knowing the *other records currently being released alongside this one* to guarantee k-1 indistinguishable matches — which is not a property of one event's payload at all, and not computable by `ADR-009`'s per-event pure transform (`(schema, one payload)`, no I/O) no matter how it's configured. |
| **Fits `ADR-009`'s wrapper?** | Cleanly for single-value bucketing — the bucket label is a plain string, same slot, same shape. **Does not fit, and should not be claimed, for real k-anonymity** — that needs dataset-wide analysis at query time (which records are being returned together, and are their generalized values actually indistinguishable), a materially different mechanism than a per-event read-time transform. If this option is ever built, the `x-masking` strategy name and its documentation must be explicit that it's single-value bucketing, not a k-anonymity guarantee — conflating the two would overclaim a privacy property the mechanism doesn't actually deliver. |

### Option D — Tokenization

Replace the real value with an opaque reference (a "token") that a
*separate, more-privileged party* can later resolve back to the real
value through its own lookup — not the same claims check that gated the
current response. Real-world tokenization comes in two flavors:
**vaulted** (a token↔value mapping stored in a secure vault) and
**vaultless** (the token is deterministically derived, e.g. via a keyed
function, and reversal is re-deriving/looking up via that key rather than
a stored table) — see e.g. [PKWARE's comparison of encryption,
tokenization, masking, and redaction](https://www.pkware.com/blog/encryption-tokenization-masking-and-redaction-choosing-the-right-approach).

| | |
|---|---|
| **Added cost over `FixedValue`** | Large, and not really a "content strategy" tweak at all. Vaulted tokenization needs a genuinely new, durable component — a token vault — that this design has nowhere else. Vaultless tokenization is, mechanically, indistinguishable from Option B's keyed format-preserving transform *for the token-generation half* — but tokenization's defining feature, "resolvable back to the real value by an authorized party later," implies a **second, separate resolution path and authority** distinct from the claims check that already exists at read time. `ADR-009`'s wrapper already gives a claims-holder the real value in the very same response — there is no stated need here for a *different* party than the reader to reverse it *later*, through a *different* mechanism. |
| **Fits `ADR-009`'s wrapper?** | Not cleanly. The token *string* fits the `masked` slot trivially, same as every other option — but that's the uninteresting half. The half that makes it tokenization rather than a `Hash` strategy with extra steps (its actual defining property) is a new mechanism entirely: a resolution endpoint, a vault or keyed-reversal authority, and a decision about *who* that authority is and how they're authorized — none of which `ADR-009`'s single-pass, claims-gated read touches today. This would be a new ADR-sized mechanism bolted alongside masking, not a `strategy` enum value inside it. |

## Recommendation

**Decided, applying KISS: ship the two built increments, decline the
other two rather than leave them open indefinitely.** Partial reveal and
keyed hashing are both decided and built; format-preserving encryption,
generalization/bucketing, and tokenization are all **declined for now** —
not because they wouldn't ever fit, but because Keep It Simple (KISS —
prefer the simplest design that satisfies a *stated* requirement; don't
build speculative capability for a need that hasn't shown up) says a
still-open "maybe later" is worse than an honest "no, until something
concrete asks for it." Nothing here forecloses adding one later — it's
just no longer tracked as an open question, since there is nothing to
resolve without a real requirement driving the choice:

- **Partial reveal (Option A)** is decided and built as `ADR-009`'s
  `"PartialReveal"` strategy — the safest, cheapest real increment beyond
  `FixedValue`, generalized to a prefix-*and*-suffix rule matching the
  real-world precedent (SQL Server's `Partial()`), specified with named,
  human-readable fields rather than a mask-template string.
- **Keyed hashing**, closely related to but distinct from Option B below
  (it doesn't preserve the value's format/length — a hash output is fixed
  size), is decided and built as `ADR-009`'s `"Hash"` strategy, reusing
  `Microsoft.Extensions.Compliance.Redaction`'s `HmacRedactor` rather than
  a bare/unsalted hash, specifically to avoid the small-value-space
  reversal risk a bare hash would carry.
- **Format-preserving masking (Option B) — declined.** It would fit the
  wrapper cleanly, but real FPE (FF1/FF3-1) is the first thing in this
  design that would need key management as a *new* capability (`Hash`
  reuses an existing keyed primitive; this would not), and the
  non-cryptographic substitution variant only saves that cost by not
  actually being encryption — not a trade this design needs to make
  absent a stated requirement for it.
- **Generalization/bucketing (Option C) — declined.** Fits as a
  single-value transform, but adds a fourth `IMaskingStrategy` for a need
  nobody has stated yet, and would have to be documented carefully to
  never be marketed as k-anonymity (a dataset-wide guarantee this
  design's per-event transform structurally cannot compute) — extra
  surface and an ongoing documentation burden for a speculative case.
- **Tokenization (Option D) — declined, and doesn't fit anyway.** Its
  defining property — reversal by a different party through a different
  mechanism, later — needs a whole new component (vault or
  keyed-reversal authority plus a resolution endpoint), not a
  `x-masking.strategy` value at all. The clearest KISS call of the three:
  no stated need for "someone other than the reader reverses this
  later," and even if there were, this isn't where it would be built.

`docs/10-open-questions.md`'s masking-strategies row is removed — this is
no longer an open fork, it's a decision (decline all three, revisit only
if a real requirement shows up).
