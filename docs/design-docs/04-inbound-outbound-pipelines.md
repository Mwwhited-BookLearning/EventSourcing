# 04 — Inbound & Outbound Pipelines

## 4.1 Inbound Pipeline

Client outbox items are transferred to the server inbox as a pure transport handoff —
no domain logic, no routing, no validation, no authority check occurs at this step
(see 01 §1.2, 07, 12). The same pattern is reused for server-to-server peer sync (09).

```plantuml
@startuml Inbound_Sequence
autonumber
actor Client
participant "Client Outbox" as CO
participant "Inbox Service" as IS
database "Event Store" as ES
participant "Router" as R
database "Schema Registry" as SR

Client -> CO: Enqueue patch/action (correlationId, expectedVersion)
CO -> IS: POST envelope (bearer token — ordinary or exchanged, see 12)
IS -> ES: Append "received" event (raw envelope, correlationId)
IS --> Client: 202 Accepted\n{correlationId, status: "received", entityId: null}
IS -> R: Notify new item (queue/poll)
R -> SR: Look up schema (entityType, version) — advisory only, never blocking
alt schema known & conformant
  R -> ES: Append "applied" event (entityId resolved)
else schema unknown/invalid
  R -> ES: Append "applied" event anyway, flagged (schemaStatus annotated; see 07)
end
@enduml
```

### 4.1.1 Status Envelope

```json
{
  "correlationId": "018f2a1e-...",
  "status": "received",
  "entityId": null,
  "schemaStatus": null,
  "authorityStatus": "unattested",
  "reason": null,
  "timestamp": "2026-07-29T14:32:00Z"
}
```

**Status values**

| Status | Terminal? | Meaning |
|---|---|---|
| `received` | No | Persisted to inbox/event store, not yet routed |
| `processing` | No | Picked up by router, in flight (optional — only if meaningfully slow) |
| `applied` | Yes | Routed, folded into entity store, `entityId` populated |
| `rejected` | Yes | Transport/structurally unusable (not the same as schema-invalid or unattested — see 07, 12 for why those are never `rejected` at this layer) |

Note: schema conformance and authority/attestation are **separate advisory axes**
(`schemaStatus`, `authorityStatus`) that ride alongside `status` — neither ever forces
`status` to `rejected`. See 07 §7.1 and 12 §12.1.

## 4.2 Outbound Pipeline

Two distinct flows share transport but differ in routing key and cardinality:

- **Responses** — keyed by `correlationId → clientId`, one-shot, terminal once resolved.
- **Subscription updates** — keyed by `entityId/streamId → [subscriberIds]`, ongoing, ordered per entity.

```plantuml
@startuml Outbound_Sequence
autonumber
database "Event Store" as ES
participant "Projector" as P
database "Entity Store" as EN
participant "Outbound Pipeline" as OP
participant "Client Inbox" as CI
actor Client

ES -> P: New event appended
P -> EN: Fold patch (Optional<T> semantics, see 06), bump version, recompute hash
P -> OP: Emit correlated response event
P -> OP: Emit subscription update event (if entity is watched)
OP -> CI: Deliver response (keyed by correlationId)
OP -> CI: Deliver subscription update (keyed by entityId, ordered)
CI -> Client: Apply to local entity cache / notify ViewModel
@enduml
```

## 4.3 Reliability Guarantees

- **Idempotent inbox insert** — unique constraint on `CorrelationId` (client-facing) or `(OriginId, SequenceNumber)` (peer sync — 09). Duplicate transfer returns the existing status rather than reprocessing.
- **Client inbox mirrors server inbox** — same durability/replay/ack semantics, just direction-reversed, so the client can reconnect and resume from a checkpoint rather than requiring a persistent connection.
- **Persist-before-route** (01 §1.2) — `received` is always achievable regardless of downstream router/schema/authority state; this is what makes rollback (11) and advisory schema resolution (07) safe.
