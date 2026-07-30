[← ADR index](../07-adrs.md)

# ADR-056: Data lifecycle designed for easy backup/restore — authoritative vs. rebuildable stores, native provider PITR, replay-based recovery

Status: Accepted

Context: Direction received this session: retention/backup/disaster
recovery execution is correctly an operations concern, post-deployment —
but the **design** should make backup/restore possible and easy, not
leave it to be discovered painfully later. `ADR-033`'s gossip replication
gives live fault tolerance across replicas; it does not answer "how do
we recover from an actual data-loss/corruption incident" (a bug that
corrupts data replicates the corruption everywhere just as reliably as
it replicates good data) — a genuinely different failure mode, standard
industry terminology: **RPO** (how much data can be lost, bounded by
backup/WAL-shipping frequency) and **RTO** (how long restore takes).

Decision:
- **Classify every store by whether it's authoritative (must be backed
  up) or rebuildable (backup is a pure RTO optimization, not required
  for correctness)** — stated explicitly, since this design has stores
  of both kinds and conflating them would either waste backup effort or
  miss something that actually needed it:
  - **Authoritative — must be backed up**: the Event Log + `EventParent`
    (`event-log.md`, hash-chained, `ADR-019`), the Schema Registry
    (`schema-registry.md`), the Streaming Channel Store (`TelemetryChannel`/
    `TelemetrySample`, `ADR-031`) — original captured samples, never
    derived from anything else — and the Attachment Store (`Attachment`/
    `AttachmentRef`, `ADR-032`) — original content-addressed bytes,
    likewise never derived. Losing any of these is a genuine, irreversible
    data loss.
  - **Rebuildable — backup is optional, purely for faster RTO**: the
    Entity Store (`ADR-021`, always rebuildable by re-folding the Event
    Log — `ADR-024`/`ADR-029`), every CQRS read model/snapshot (`ADR-015`'s
    own stated design: "a full rebuild is just replaying from sequence `0`
    again"), and any materialized upcast (`ADR-027`, recomputable from the
    original event + the upcast chain). None of these can be *lost* in the
    sense that matters — restoring them is re-running a mechanism this
    design already has, not a new recovery procedure.
  - The Read Access Audit Log (`ADR-045`) is authoritative in the same
    sense as the Event Log — its own independent hash chain means it
    can't be regenerated after the fact any more than the Event Log can.
- **Restore-then-replay is already a first-class, existing path, not new
  mechanism**: recovering an authoritative store from its native backup,
  then re-running the existing fold (`ADR-021`)/projection-rebuild
  (`ADR-015`) machinery against it, *is* how a rebuildable store recovers
  — this ADR's contribution is naming that property explicitly as the
  disaster-recovery story for those stores, not inventing anything.
- **Nothing in this design's storage choices blocks each provider's own
  native backup/point-in-time-recovery tooling** — and this was already
  true before this ADR, not a new constraint: `ADR-004`'s choice of
  portable text columns for `Payload`/`JsonSchema` (over native/
  provider-specific JSON column types) was originally justified for
  cross-provider portability, and the same property means standard
  backup tooling (PostgreSQL WAL archiving/`pg_basebackup`, SQL Server
  transaction log backups, SQLite file-level/`.backup` copies) works
  against ordinary rows with no exotic column types to special-case.
  Nothing further is required here beyond deployment-time configuration
  of whichever native mechanism the provider offers.
- **Replication (`ADR-033`) and backup are complementary, not
  substitutes, stated explicitly to prevent the two being conflated**:
  replication protects against a single site's hardware/availability
  failure with near-zero RPO for replicated shards; backup/PITR protects
  against logical corruption or a bug that a replicated write would
  faithfully propagate everywhere. A deployment needs both, not either.

Consequences:
- Resolves `docs/10-open-questions.md`'s data-lifecycle row for the
  *design* half of the question (does the design make backup/restore
  possible and easy — yes, and largely already did). The *operational*
  half — actual retention windows, backup cadence/RPO target, cold-
  storage tiering for cost management — remains a deployment-time
  configuration choice, correctly out of a framework design's scope, not
  reopened as a new open question.
- No schema or storage-shape change results from this ADR — it is a
  clarifying decision (what needs backing up, and confirmation that
  nothing blocks doing so with standard tooling), not new mechanism.
- `06-solution-structure.md` gains a short "Data lifecycle" pointer to
  this ADR's authoritative/rebuildable classification, so a reader
  sizing a backup plan doesn't have to reconstruct it from first
  principles across `event-log.md`/`streaming-and-attachments.md`/
  `09-cqrs-read-models.md`.

**Compliance note** (a proving-ground compliance review, this session):
this ADR's authoritative/must-back-up classification is exactly what
HIPAA's Security Rule already requires for the clinical-trials domain —
45 CFR §164.308(a)(7)(ii)(A)'s Data Backup Plan mandates "procedures to
create and maintain retrievable exact copies of electronic protected
health information," the same bar this ADR draws around the Event Log,
Schema Registry, Streaming Channel Store, Attachment Store, and Read
Access Audit Log.
