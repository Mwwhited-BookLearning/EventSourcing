# 14 — Open Questions

Unresolved decisions, collected across the design discussion, kept in one place so
none get lost.

## Schema & Evolution (see 07)
- Exact conflict-flag propagation: should earlier events be retroactively flagged once
  a later conflicting event arrives, or only the later event? (08, 07)
- Reconciliation strategy for unresolved-schema history: background reconciliation pass
  (recommended) vs. forward-only acceptance of permanent gaps (07 §7.2.1).
- CEL (.NET port maturity) vs. Jint for the declarative transform tier — needs a
  concrete evaluation before committing (07 §7.3.3).

## Sharding & Replication (see 09)
- Entity-type-based sharding vs. hash-based consistent hashing as the initial default
  (09 §9.2).
- Final peer-sync topology choice: gossip/full mesh (recommended) vs. hub-and-spoke vs.
  leaderless pull, pending actual deployment topology/scale (09 §9.4.1).

## Query API (see 10)
- GraphQL vs. OData — final decision pending frontend team input on tooling
  preferences; GraphQL recommended as primary (10 §10.1).
- Whether a fully dynamic/schema-less resolver layer is ever needed for truly
  arbitrary unknown-field queries, or whether the `extensions` field is sufficient
  (10 §10.2).

## Compatibility & Deployment (see 11)
- Formal, per-field consistency guarantee to document/expose at query time (e.g., can
  certain fields opt into stronger read consistency?) (11, 09 §9.3, 10 §10.6).

## Non-Authoritative Capture (see 12)
- Per-entity-type default for `RejectionBehavior` (`annotate` vs. `compensate`) — needs
  input from domains with legal/evidentiary requirements (12 §12.4).
- Whether `AttestedClaims` should have its own formal schema-registry entity type from
  day one, or evolve organically (12 §12.6).

## Client Architecture (see 03)
- One view per entity type vs. multiple views per type (list/detail/edit) —
  tentatively resolved as multiple, independently versioned (`ViewKind`), pending real
  UX validation (03 §3.4).
- Template engine choice: raw HTML+JS with a small injected binding runtime vs. a
  lightweight templating syntax compiled client-side (03 §3.4).

## Cross-Cutting
- A formal workflow/saga engine for multi-step actions (flagged as likely future need,
  not yet designed — 01 §1.4 non-goals).
- Retention/compaction policy for the event store given the "never delete" stance —
  snapshot/checkpoint strategy for very long-lived, high-churn entities.
