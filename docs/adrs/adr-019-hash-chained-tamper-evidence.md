[← ADR index](../07-adrs.md)

# ADR-019: Hash-chained events for tamper evidence

Status: Accepted

Context: `StoredEvent.PayloadHash` (`ADR-011`) already exists, but purely
as a content-equality check for idempotent-retry detection — it says
nothing about *when* or in what order an event was appended relative to
any other, and nothing detects whether a row in `Events` was ever altered
after the fact (e.g., a direct database edit bypassing the application
entirely). An event-sourced store of record is exactly the shape of system
where that guarantee has real value — Certificate Transparency (RFC 9162)
and Merkle-tree verifiable logs generally (see `docs/references.md`)
exist to solve precisely this: making tampering with *any* past entry
detectable, without needing to trust the store operator.

Decision:
- `StoredEvent` gains a `ChainHash` column:
  `ChainHash[n] = SHA-256(ChainHash[n-1] || PayloadHash[n] || SequenceNumber[n])`,
  computed by `EventAppender` at insert time, chained off the immediately
  preceding `SequenceNumber`'s `ChainHash` (a fixed seed value for
  `SequenceNumber = 1`, the store's first-ever event).
- This is a **linear hash chain, not a full Merkle tree** — deliberately
  simpler than Certificate Transparency's binary tree, since this design
  has no need for CT's specific inclusion/consistency-proof-against-a-
  partial-view use case (one store, not a federation of independently
  operated logs cross-checking each other). A linear chain gives the same
  tamper-evidence property (altering any past `Payload`/`PayloadHash`
  breaks every subsequent `ChainHash`) with a far simpler verification
  procedure: replay the chain from `SequenceNumber = 1` and compare the
  final `ChainHash` to what's stored.
- A read-only verification endpoint,
  `GET /events/verify?throughSequenceNumber=<n>` (or an offline tool —
  left as an implementation detail, not fixed here), recomputes the chain
  from `1` through `n` and reports the first `SequenceNumber` where the
  stored and recomputed `ChainHash` diverge, if any.
- `ChainHash` is computed once, at publish time, in the same transaction
  as the `StoredEvent` insert (`EventAppender`) — never recomputed or
  backfilled. There is no migration path today that alters historical
  `Payload` content (`ADR-009`'s closing note); if one ever existed, it
  would invalidate the chain from that point forward by design, not as an
  oversight to work around.

Consequences:
- Complementary to, not a replacement for, `PayloadHash`/`ADR-011` —
  `PayloadHash` answers "is this retry identical to what I already
  stored," `ChainHash` answers "has anything in this store's history been
  altered since it was written." Different questions, same SHA-256
  primitive, deliberately reused rather than introducing a second hash
  algorithm.
- Verification is `O(n)` from the seed — cheap for a periodic integrity
  audit, not designed for cheaply verifying one arbitrary event's position
  in isolation (that needs real Merkle inclusion proofs — an explicitly
  rejected complexity for v1, per the linear-chain choice above).
- This gives tamper-**evidence**, not tamper-**prevention** — an attacker
  with direct database write access could still rewrite `Events` and
  recompute every downstream `ChainHash` to match. What this closes is the
  *undetected* part: recomputing the entire chain from `1` is a far more
  detectable act (e.g., against an independently-stored periodic
  checkpoint of `ChainHash` at various `SequenceNumber`s) than simply
  editing one row and hoping no one checks.
- No provider-specific translation needed (unlike `IJsonPathTranslator`) —
  `ChainHash` computation is plain application code in `EventAppender`,
  identical on SQLite/Postgres/SQL Server; only the column itself (`TEXT`,
  portable per `ADR-004`) is persisted per provider.

**Compliance note** (a proving-ground compliance review, this session):
this mechanism is the load-bearing primitive behind several confirmed
non-gaps found across proving-ground candidates — SEC Rule 17a-4's
broker-dealer recordkeeping (`ADR-071`), SOX Section 404's change-
management ITGC (`ADR-067`), and digital forensics' evidentiary
authentication (US Federal Rules of Evidence 901/902, ISO/IEC 27037) —
none of which needed a new mechanism, all satisfied by this one already
existing.
