# 01 — Overview & Goals

## 1.1 Overview

This document set describes a client/server architecture built around durable
messaging, event sourcing, CQRS, an advisory schema registry, application-managed
sharding and multi-origin replication, and non-authoritative data capture. Clients
submit **patches** (partial or full entity changes) and **actions** (commands) through
a durable outbox/inbox transport. The server persists everything it receives — even
data that fails validation, arrives from an unproven identity, or references a schema
version it doesn't yet know — appends it to an insert-only **event store**, and folds
it into a mutable, versioned, hashed **entity store** used for reads. Clients receive
**responses** (to their own submissions) and **subscription updates** (for watched
entities) through a symmetric outbound pipeline into a client-side inbox.

Servers themselves are peers: the same durable outbox/inbox transport used for
client↔server communication is reused for server↔server replication, so that no
server is authoritative and no server is required to be reachable, current, or
consistent with any other server at any point in time.

## 1.2 Core Design Philosophy

A single governing principle threads through nearly every decision in this design,
worth stating explicitly up front:

> **Never let unrecognized, unverified, or currently-unroutable data block, delay, or
> corrupt anything else in the system. Persist first. Understand, validate, route,
> authorize, and reconcile as separate, non-blocking, eventually-completed steps.**

This shows up repeatedly, under different names, throughout the documents:

- **Soft/advisory schema resolution** (07) — unknown or invalid shape is persisted and flagged, never rejected.
- **Persist-before-route** (04) — receipt (`received`) is a distinct, always-achievable state, separate from routing/application (`applied`).
- **Tolerant Reader / additive-only compatibility** (11) — unknown fields are ignored, not fatal; missing fields default or null, never fatal.
- **Non-authoritative capture** (12) — data from an unproven identity is still captured, flagged, and reconciled later, never blocked at the door.
- **Eventually-consistent replication** (09) — no server waits for another to agree before making local progress.
- **Rollback-safe, expand-only migrations** (11) — nothing is ever in a state that can't be recovered from without data loss.

Everything else in this design is, in effect, an application of this one rule to a
specific concern (identity, schema, network partition, deployment version, or
concurrent writers).

## 1.3 Goals

- Never lose an inbound message, even if it is malformed, unauthorized, or references an unknown schema.
- Decouple "received" from "valid" from "authorized" from "applied" as distinct, independently observable, non-blocking states.
- Support partial updates that distinguish *property not sent* from *property explicitly set to null*.
- Allow entity/schema evolution over time without breaking replay of historical events, and without requiring synchronized deployment of clients and servers.
- Support application-level sharding and multi-origin (geo-distributed) replication with eventual consistency, including servers that may never observe a fully agreeing global state.
- Expose flexible, client-driven, hierarchical reads (GraphQL primary, OData where needed) without requiring the backend to pre-shape every response.
- Give clients and humans the ability to inspect the full change history of any entity, including contested/concurrent writes and rejected/unattested claims.
- Client UI is MVVM, with clean separation of structure (View — potentially an embedded HTML+JS surface), style, state (ViewModel), and transport (Commands → Outbox).
- Support rolling deployment and rollback of server code without requiring database restore or forced client upgrades.
- Support data capture from actors whose authority cannot be verified at capture time (offline, disconnected, or pending review), using cryptographically self-attesting credentials (DID/UCAN) exchanged for ordinary bearer tokens so downstream services remain simple.

## 1.4 Non-Goals (for this version)

- Strong/linearizable consistency across replicas.
- Fully automatic, semantic conflict resolution for arbitrary field types (only a default last-write-wins policy plus per-field/per-entity-type override hooks).
- A final, binding choice between GraphQL and OData (GraphQL is recommended as primary; OData is evaluated as a secondary option — see 10).
- A general-purpose workflow/saga engine (multi-step action orchestration is noted as a likely future need but not designed here).

## 1.5 Terminology

| Term | Meaning |
|---|---|
| **Correlation ID** | Client-generated ID (UUID) identifying a single inbound submission. Tracks that submission through the pipeline until resolved. Not a domain concept. |
| **Entity ID** | Server-assigned canonical identity, `{appId}:{entityType}:{uniqueId}`. Only exists once a submission has been routed/applied. |
| **Patch** | Inbound or outbound message describing property-level changes to an entity. `full` (entire snapshot) or `partial` (only changed properties present). |
| **Action** | An inbound command that isn't itself a data change but may trigger one or more patches (e.g., "approve", "archive"). Shares the same transport/event path as patches, discriminated by message type. |
| **Optional\<T\>** | Wrapper type representing three states in a patch payload: *unspecified*, *specified as null*, *specified with a value*. See 06. |
| **Inbox / Outbox** | Durable holding areas (client-side and server-side, and peer-to-peer) for messages received/generated but not yet processed/transmitted. |
| **Event Store** | Insert-only, append-only log of all patches/actions — the system's source of truth. |
| **Entity Store** | Mutable, versioned, hashed materialized projection of the event store — "current state," rebuildable by replay. |
| **Schema Registry** | Versioned, hashed, itself-replicated registry of known entity shapes — advisory, not gating. See 07. |
| **Schema Map** | Versioned forward (upcast) / backward (downcast) transform between schema versions, expressed as JS functions or restricted expressions (CEL). See 07. |
| **Shard** | Application-level partition of the entity store (by key — entity type, or hash via consistent hashing). |
| **Replica** | A copy of a shard's data, potentially originated/written at a different physical location; replicas converge via eventual consistency. |
| **Authority Status** | Whether a self-attested submission has been reviewed/accepted/rejected — advisory metadata, never a processing gate. See 12. |

## 1.6 How to Use This Document Set

Each numbered document can largely be read on its own, but the reading order in
`00-README.md` reflects the natural build-up of concepts: transport → data model →
evolution → concurrency → distribution → query → compatibility → trust. Cross-document
references are given as "(NN §Section)".
