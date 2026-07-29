# 02 — Architecture Context

See 01 for terminology and governing philosophy.

## 2.1 System Context

```plantuml
@startuml C4_Context
!include <C4/C4_Context>

Person(user, "End User", "Uses the client application")
System(client, "Client Application", "MVVM UI, local outbox/inbox, ViewModels, entity view rendering")
System(platform, "Event-Sourced Entity Platform", "Inbox/outbox transport, event store, entity store, schema registry, query API")
System_Ext(otherOrigin, "Peer Server / Other Site", "Another server instance replicating the same entity set")
System_Ext(idp, "OAuth/OIDC Identity Provider", "Issues bearer JWTs; performs token exchange for self-attested (DID/UCAN) credentials")

Rel(user, client, "Interacts with")
Rel(client, platform, "Submits patches/actions (outbox->inbox); receives responses & subscription updates")
Rel(platform, otherOrigin, "Peer sync: replicates entity events (eventually consistent)")
Rel(otherOrigin, platform, "Peer sync: replicates entity events (eventually consistent)")
Rel(client, idp, "Obtains bearer token (ordinary auth, or via token exchange from a self-attested credential)")
Rel(platform, idp, "Validates bearer tokens; may perform token exchange server-side for offline-captured credentials")

@enduml
```

## 2.2 Container View

```plantuml
@startuml C4_Container
!include <C4/C4_Container>

Person(user, "End User")

System_Boundary(clientBoundary, "Client Application") {
  Container(view, "View (Native + Embedded Web)", "Structure & Style", "Bindings only; entity-specific views may be HTML+JS hosted in an embedded web engine")
  Container(vm, "ViewModel Layer", ".NET / Rx", "State projection, ICommand bindings")
  Container(clientOutbox, "Client Outbox", "Local durable store", "Pending submissions")
  Container(clientInbox, "Client Inbox", "Local durable store", "Received responses & subscription updates")
}

System_Boundary(serverBoundary, "Server Platform (one node/peer)") {
  Container(inboxSvc, "Inbox Service", "API endpoint", "Accepts raw envelopes, returns 202 + status; never gates on schema or authority")
  Container(router, "Router / Command Handler", "Background service", "Resolves entity type/stream, applies advisory schema checks")
  ContainerDb(eventStore, "Event Store", "Insert-only table", "Patch/action chain, source of truth")
  Container(projector, "Projector", "Background service", "Folds patches into entity store, applies upcasters, detects conflicts")
  ContainerDb(entityStore, "Entity Store", "Mutable, versioned, hashed, sharded", "Materialized current state")
  ContainerDb(schemaRegistry, "Schema Registry", "Versioned, hashed, replicated table", "Known entity shapes, upcasters, schema maps")
  Container(outboundSvc, "Outbound Pipeline", "Projection + push service", "Responses + subscription fan-out")
  Container(queryApi, "Query API", "GraphQL / OData", "Client-driven reads over entity store & event history")
  Container(peerSync, "Peer Sync Outbox/Inbox", "Durable store + background service", "Server-to-server event replication")
}

Rel(user, view, "Uses")
Rel(view, vm, "Data/Command binding")
Rel(vm, clientOutbox, "Dispatches commands via Mediator")
Rel(clientOutbox, inboxSvc, "Transfers envelope (HTTP/queue)")
Rel(inboxSvc, eventStore, "Appends raw envelope (received)")
Rel(inboxSvc, router, "Signals new inbox item")
Rel(router, schemaRegistry, "Checks known schema (advisory only)")
Rel(router, eventStore, "Appends routed/applied event")
Rel(projector, eventStore, "Replays in order")
Rel(projector, entityStore, "Writes materialized version")
Rel(projector, outboundSvc, "Emits status/subscription events")
Rel(outboundSvc, clientInbox, "Delivers responses & updates")
Rel(queryApi, entityStore, "Reads current state")
Rel(queryApi, eventStore, "Reads change history")
Rel(vm, clientInbox, "Observes via Rx for state updates")
Rel(eventStore, peerSync, "Feeds outbound peer sync")
Rel(peerSync, eventStore, "Delivers events from peers (same path as client inbox)")

@enduml
```

## 2.3 Design Reading Guide

- **04** expands the Inbox Service / Router / Outbound Pipeline containers above.
- **05** defines the Event Store / Entity Store / Schema Registry table shapes.
- **09** expands the Peer Sync container and multi-server topology.
- **10** expands the Query API container.
- **12** adds identity/attestation concerns to the Inbox Service.

No container in this diagram is permitted to synchronously block on another remote
container's availability or agreement before making local progress — see 01 §1.2.
