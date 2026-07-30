[← ADR index](../07-adrs.md)

# ADR-071: PCI-DSS Sensitive Authentication Data can never be registered as a schema field — a hard boundary at registration, not publish

Status: Accepted

Context: A review of the proving-ground domains *not* chosen (`docs/
comparisons/proving-ground-domain.md`'s brokerage/capital-markets and
insurance candidates) surfaced a real requirement neither of the two
chosen domains (clinical trials, digital identity/KYC) exercises: **PCI
compliance for payment card data**. Checked against the actual
standard rather than assumed: **PCI-DSS Requirement 3.2/3.2.2**
prohibits storing **Sensitive Authentication Data (SAD)** — the card
verification code/value (CVV2/CVC2/CID), full magnetic-stripe/track
data, and PIN blocks — **after authorization, under any
circumstances, including encrypted.** This isn't a masking or
key-management question; the standard is explicit that encryption does
**not** create an exception. No exception exists for issuers/their
service providers acting in that specific role — for every other
organization, SAD must be rendered unrecoverable, full stop.

**This is a genuine, structural tension with this design's own
foundational posture, not a case existing mechanisms already cover.**
`ADR-023`'s persist-everything ingestion ("never let unrecognized,
unverified, or currently-unroutable data block, delay, or corrupt
anything else — persist first, always") and `README.md`'s governing
"never lose or corrupt data" principle are both unconditional in a way
that's simply incompatible with a rule saying a specific class of data
must **never be written down in the first place, regardless of
protection applied afterward**. `ADR-009`'s masking and `ADR-057`'s
crypto-shredding both still write the real value into `Payload` before
anything happens to it — masking hides it at read time, crypto-
shredding makes it unrecoverable *later*, but PCI-DSS requires it never
be persisted *at all*, which neither mechanism does or could be made to
do without contradicting the append-only architecture this entire
design is built on.

Decision:
- **Full PAN (the card number itself) is explicitly not SAD, and is
  already fully covered** — `ADR-009`'s masking plus `ADR-057`'s
  crypto-shredding are the correct, sufficient mechanism for a full
  card number, no different from any other classified PII/PHI field.
  This ADR is scoped narrowly to the SAD subset PCI-DSS singles out for
  absolute non-persistence: CVV2/CVC2/CID, full track/magnetic-stripe
  data, and PIN blocks.
- **A reserved `x-masking.regulatoryClassification` value,
  `"PCI-SAD"`, makes schema *registration* — not publish — hard-reject
  the event type outright (`400`).** This is deliberately the one place
  in this design that still enforces reject-on-invalid after `ADR-023`:
  registration (`PUT /registry/{event-type}`) is a distinct operation
  from publish, already allowed to reject on its own terms (an invalid
  `JsonSchema` document, a duplicate name/version) — extending that
  existing, narrower rejection surface to a self-declared `PCI-SAD`
  field is consistent with what registration already does, not a new
  exception carved into the publish path `ADR-023` governs.
- **This relies on honest self-declaration by the schema author, the
  same trust model `regulatoryClassification`'s other values already
  use** — this framework cannot reliably detect "this field holds a
  CVV" by inspection (a name, a pattern, a length are all defeatable
  heuristics); `PCI-SAD` is a schema author's own declaration, checked
  at registration, the same as declaring `"PHI"` or `"PCI"` already is
  purely informational metadata elsewhere.
- **The real-world answer is exclusion at the boundary, not a
  mechanism inside this framework** — stated as explicit guidance, not
  built as a feature: an application handling payment card data should
  never let raw SAD reach this framework's publish endpoint at all.
  The standard, PCI-scope-reducing architecture is a PCI-compliant
  tokenization/hosted-fields payment processor (Stripe, Adyen, or
  similar) capturing SAD directly from the payer, authorizing the
  transaction, and returning only a token/authorization result — this
  framework only ever sees the token, never the SAD, the same "keep
  PII/PHI out of the URL/log/proxy cache" instinct `ADR-012`/`ADR-037`
  already apply to a different kind of sensitive data in transit.

Consequences:
- Resolves the one concrete new requirement the proving-ground-domain
  review (`docs/comparisons/proving-ground-domain.md`) surfaced from a
  domain not chosen as a build target — recorded here since the
  *framework* itself, being domain-agnostic (`ADR-030`), should still
  have an answer for it even though neither proving-ground application
  needs it directly.
- `docs/data/schema-registry.md`'s masking section gains the
  `PCI-SAD` reserved classification value and this ADR's cross-
  reference — not yet added, flagged as remaining propagation work.
- **A confirming, non-action finding from the same review, worth
  recording rather than silently dropping**: brokerage/capital-markets'
  other headline requirement — SEC Rule 17a-4's broker-dealer
  recordkeeping rule — is **already satisfied by this design's existing
  architecture**, not a gap. The rule's traditional WORM (write-once-
  read-many) format requirement was amended (2022–2023) to add an
  **audit-trail alternative** — a tamper-evident, append-only record of
  every change — which is exactly `ADR-019`'s hash-chained Event Log
  already provides. If brokerage is ever built as a third proving-ground
  domain, this requirement needs no new mechanism.
