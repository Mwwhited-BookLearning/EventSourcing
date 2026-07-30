[← ADR index](../07-adrs.md)

# ADR-045: Read access audit log — every read logged against the reader's identity and trust basis

Status: Accepted — resolves the open question `ADR-043` raised

Context: `ADR-043` flagged, as a genuinely open question, whether every
read made under a delegated access grant should be its own auditable
event. Resolved this session, and generalized beyond that one trigger:
**every** read — not only ones made under a delegated grant — must be
logged against the reading user, explicitly recording whether that
user's own credential is `Authoritative` (ordinary direct
authentication, `ADR-006`) or `Attested` (derived from a self-attested
UCAN, `ADR-036`, or a delegated access grant, `ADR-043` — both trace
back to the same UCAN mechanism, so both collapse into one value here).
This is the same trust-axis idea `ADR-035`/`ADR-042` already apply to
*data* (`AuthorityStatus`), now applied to the *reader*.

This is a real, well-precedented regulatory pattern, not a bespoke
idea: [HIPAA §164.312(b), Audit Controls](https://docs.alertlogic.com/analyze/reports/compliance/HIPAA-164.312-audit-controls.htm)
requires exactly "who accessed what, when, and what action was
performed," logs "protected from alteration," and a minimum six-year
retention (§164.316(b)(2)(i)) — this design isn't HIPAA-specific, but
the shape generalizes cleanly and the framework should support it
without an application having to build it from scratch.

Decision:
- **A new, separate, append-only `AccessLog`** — deliberately **not**
  mixed into `StoredEvent`/the Event Log. Same reasoning this design
  already applies to streaming channels (`ADR-031`) and attachments
  (`ADR-032`): a genuinely different volume/performance profile (reads
  vastly outnumber writes) and a different reader (an auditor, not a
  domain consumer) earn their own store, not a shared one.
- **Every read through any surface** — GraphQL queries against the
  authoritative Entity Store or the Live View (`ADR-037`/`ADR-042`),
  WebDAV/attachment retrieval (`ADR-032`), streaming channel playback
  (`ADR-031`), ticket-authenticated headerless access (`ADR-040`) —
  writes one `AccessLogEntry`:
  ```csharp
  public class AccessLogEntry
  {
      public long SequenceNumber { get; set; }            // own append-only sequence, independent of StoredEvent's
      public string ReaderActorId { get; set; } = default!;
      public string ReaderTrustBasis { get; set; } = default!; // "Authoritative" | "Attested"
      public Guid? GrantRef { get; set; }                  // set when ReaderTrustBasis=Attested via an ADR-043 delegated grant specifically
      public string ViewAccessed { get; set; } = default!;  // "Authoritative" | "Live" (ADR-042) -- which of the two views was read
      public string ResourceRef { get; set; } = default!;   // EntityId / AttachmentRef / channel+position / etc.
      public string Action { get; set; } = default!;        // "query" | "stream" | "download" | ...
      public DateTimeOffset AccessedAt { get; set; }
      public string ChainHash { get; set; } = default!;      // see below
  }
  ```
- **Hash-chained, reusing `ADR-019`'s primitive** — HIPAA's own
  requirement that audit logs be "protected from alteration" is exactly
  the problem `ADR-019` already solved for the Event Log. `AccessLog`
  gets its **own, independent** chain (`ChainHash[n] = SHA-256(ChainHash[n-1]
  || <entry fields> || SequenceNumber[n])`) rather than sharing the Event
  Log's chain — different append source, different reader, no reason to
  couple their tamper-evidence.
- **Retention: never deleted by default**, consistent with this design's
  governing "never lose or corrupt data" principle (`README.md`) — this
  already exceeds HIPAA's six-year minimum without a bespoke retention
  policy; an application needing a shorter, compliant *deletion* policy
  would be a deliberate, explicit exception to that default, not
  designed further here.
- **Explicit composition, not an auto-injected aspect (`ADR-041`)**:
  each read endpoint/resolver calls the access-logging step explicitly
  in its own composition — no reflection-based interceptor silently
  wrapping every query. The write itself is fire-and-forget relative to
  the read's response (the same "don't let a durability write block the
  critical path" shape this design's outbox patterns already use
  elsewhere), not a synchronous dependency of returning read results.

Consequences:
- **A genuinely new mechanism, not a reuse-with-no-cost one**: this is
  the first time this design's *read* side gets a durable write path.
  Real added write volume on every read is an accepted cost given the
  explicit requirement — not something to quietly hope stays cheap.
- `docs/data/access-log.md` is a new classification group (see
  `02-data-model.md`) — `AccessLog` lives in its own store, the same way
  streaming channels/attachments do, not inside `EventStoreContext` or
  `ProjectionsDbContext`.
- This resolves and removes the open question `ADR-043` raised — updated
  in `docs/10-open-questions.md`.
- Auditing-the-audit (who reads `AccessLog` itself) is deliberately not
  addressed further — the hash chain is this design's answer to
  tampering detection everywhere else it applies this pattern, and
  there's no reason `AccessLog` needs a second, recursive audit trail on
  top of that.
- **`ADR-009`'s `revealOnDemand` reuses this unchanged** — a
  `revealField` call writes an ordinary `AccessLogEntry` with
  `Action: "reveal"` and `ResourceRef` naming the specific field path,
  no schema change needed here. This gives field-level reveal actions
  sharper audit granularity than an ordinary bulk query already has —
  "this field was actually looked at," not just "this response
  contained it."

**Compliance note** (a proving-ground compliance review, this session):
beyond HIPAA §164.312(b) (already this ADR's own driving citation),
`AccessLog` is real, load-bearing infrastructure for two more
requirements this design hadn't explicitly connected it to: **GDPR
Art. 33/34 breach notification** — establishing a breach's scope and
timeline ("who accessed what, when") is exactly what `AccessLog`
already records, though the *notification workflow itself* (a 72-hour
authority-notification clock, plus Art. 33(5)'s mandatory breach
register covering even non-notifiable incidents) is not yet designed —
tracked as an open question; and **SOX Section 404 IT General
Controls** — the access-control ITGC specifically is already satisfied
by this mechanism, a confirming non-gap for the brokerage proving-
ground candidate, the same pattern `ADR-071` already found for SEC
Rule 17a-4.
