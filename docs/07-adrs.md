# Architecture Decision Records

Each ADR now lives in its own file under [`adrs/`](adrs/) — this file
stays small on purpose: the template every ADR follows, plus a table of
contents. `references.md` is the consolidated bibliography across every
ADR plus every other numbered doc; individual ADRs cite the specific
RFC/standard/library they're grounded in inline, where relevant.

## ADR template

```
## ADR-NNN: <title>
Status: Proposed | Accepted | Superseded | Deferred
Context: <why this decision was needed>
Decision: <what was decided>
Consequences: <trade-offs accepted>
```

## Index

| ADR | Title | Status |
|---|---|---|
| [001](adrs/adr-001-per-deployment-db-provider.md) | Per-deployment database provider build (vs. runtime switch) | Accepted |
| [002](adrs/adr-002-on-demand-spec-generation.md) | On-demand OpenAPI/AsyncAPI generation (vs. materialized cache) | Accepted |
| [003](adrs/adr-003-reject-unfilterable-fields.md) | Reject filtering on non-indexed/undeclared fields (vs. silent full scan) | Accepted |
| [004](adrs/adr-004-portable-json-columns.md) | JSON payload/schema columns stored as portable text, not native JSON column types | Accepted |
| [005](adrs/adr-005-event-parenting-dag.md) | Event parenting as an envelope-level DAG, validation mode per event type | Accepted |
| [006](adrs/adr-006-dev-oauth-oidc-auth.md) | Dev-mode OAuth2/OIDC bearer-token auth via an in-process OpenIddict host, orchestrated with .NET Aspire | Accepted |
| [007](adrs/adr-007-derived-event-types.md) | Derived/materialized event types via cross-stream join+projection | Deferred (fully designed, pure scheduling call) |
| [008](adrs/adr-008-event-type-security.md) | Event-type security via per-event-type required claims | Accepted |
| [009](adrs/adr-009-property-level-masking.md) | Property-level masking via a value/masked wrapper | Design Accepted; build deprioritized to Phase 8 |
| [010](adrs/adr-010-tail-vs-replay-mode.md) | Explicit tail-vs-replay mode on Follow, via a `mode` parameter | Accepted |
| [011](adrs/adr-011-publish-idempotency.md) | Publish idempotency via an optional client-supplied `eventId` + a stored payload hash | Accepted |
| [012](adrs/adr-012-http-query-method.md) | HTTP `QUERY` method (RFC 10008) for OData data-queries, replacing `GET` | Accepted |
| [013](adrs/adr-013-problem-details.md) | Canonical error responses via RFC 9457 Problem Details | Accepted |
| [014](adrs/adr-014-cors-policy.md) | CORS policy — configurable allowlist, deny by default | Accepted |
| [015](adrs/adr-015-cqrs-projections-follow-api.md) | Read-model projections consume the public Follow API, not a private hook | Accepted |
| [016](adrs/adr-016-changekind-centralized-merge.md) | Event-type `ChangeKind` (Full \| Partial) and centralized snapshot merge | Accepted |
| [017](adrs/adr-017-dpop-bound-tokens.md) | DPoP-bound access tokens (RFC 9449) | Accepted; built in Phase 10 |
| [018](adrs/adr-018-event-upcasting.md) | Event upcasting for schema evolution | Accepted |
| [019](adrs/adr-019-hash-chained-tamper-evidence.md) | Hash-chained events for tamper evidence | Accepted |
| [020](adrs/adr-020-schemaversion-on-publish.md) | Explicit `schemaVersion` on publish, with publish-time upcast validation and a reserved dead-letter event type | Accepted |
| [021](adrs/adr-021-entity-concept.md) | Entity as a first-class concept (`EntityId`, Entity Store, `ExpectedVersion`) | Accepted |
| [022](adrs/adr-022-optional-t-property-patches.md) | `Optional<T>` property-level patches (refines `ADR-016`) | Accepted |
| [023](adrs/adr-023-persist-everything-ingestion.md) | Persist-everything ingestion posture (supersedes reject-on-invalid framing in `ADR-011`/`ADR-013`/`ADR-020`) | Accepted |
| [024](adrs/adr-024-optimistic-concurrency-conflict-flagging.md) | Optimistic concurrency + conflict flagging | Accepted |
| [025](adrs/adr-025-api-documentation-ui.md) | API documentation UI — Scalar for OpenAPI, `@asyncapi/react-component` for AsyncAPI | Accepted |
| [026](adrs/adr-026-dev-aspire-otel-prod-compose.md) | Development via .NET Aspire + OpenTelemetry (logging, tracing, metrics); production via Docker Compose | Accepted |
| [027](adrs/adr-027-materialized-upcasts.md) | Materialized upcasts persisted to the event log, folded exactly once | Accepted |
| [028](adrs/adr-028-downcast-on-retrieval.md) | Downcast on retrieval for an explicitly requested older schema version | Accepted |
| [029](adrs/adr-029-logical-order-fold.md) | Logical-order fold for out-of-order/lagged event arrival | Accepted |
| [030](adrs/adr-030-multi-tenant-framework.md) | Multi-tenant framework — `appId`-scoped schemas, domain-agnostic core | Accepted |
| [031](adrs/adr-031-telemetry-channels.md) | Streaming channels (telemetry, audio/video) — a separate fast path, linked to events via `TelemetryPointer` | Accepted |
| [032](adrs/adr-032-binary-attachments.md) | Binary attachments — content-addressed, linked to an entity or event | Accepted |
| [033](adrs/adr-033-multi-origin-replication.md) | Multi-origin replication — gossip topology, fault-tolerant peer-sync outbox/inbox | Accepted |
| [034](adrs/adr-034-application-level-sharding.md) | Application-level sharding by `EntityType` | Accepted |
| [035](adrs/adr-035-non-authoritative-capture.md) | Non-authoritative capture — `AuthorityStatus` as a trust axis independent of `SchemaStatus` | Accepted |
| [036](adrs/adr-036-did-ucan-token-exchange.md) | DID + UCAN for offline self-attestation, exchanged via OAuth Token Exchange (RFC 8693) | Accepted |
| [037](adrs/adr-037-graphql-only-query-layer.md) | GraphQL as the sole query layer — supersedes `ADR-003`/`04-odata-filter-pushdown.md` | Accepted |
| [038](adrs/adr-038-compatibility-and-deployment.md) | Compatibility & deployment discipline — Tolerant Reader, Expand/Contract, N-1/N+1 window | Accepted |
| [039](adrs/adr-039-mvvm-client.md) | MVVM client architecture + HTML/JS entity view definitions | Accepted |
| [040](adrs/adr-040-ticket-exchange-headerless-clients.md) | URL-embeddable ticket exchange for header-incapable clients (streaming/WebDAV playback) | Accepted |
| [041](adrs/adr-041-explicit-composition-first-party-libraries.md) | Explicit composition and first-party libraries over convention-magic (constructor injection, Pure DI, no AutoMapper/third-party logging, `System.Text.Json`) | Accepted |
| [042](adrs/adr-042-gated-authoritative-publish.md) | Gated authoritative publish — Entity Store only reflects approved data; a separate Live View shows the rest (revises `ADR-035`) | Accepted |
| [043](adrs/adr-043-delegated-temporary-access-grants.md) | Delegated, capped, time-boxed read-access grants ("secondary opinion" access) — reuses UCAN delegation (`ADR-036`) | Accepted |
| [044](adrs/adr-044-application-defined-permissions.md) | Application-defined permission/grant types via per-`AppId` trust roots (resolves what UCAN itself leaves out-of-band) | Accepted |
| [045](adrs/adr-045-read-access-audit-log.md) | Read access audit log — every read logged against the reader's identity and trust basis (resolves the open question `ADR-043` raised) | Accepted |
| [046](adrs/adr-046-role-based-access-control.md) | Role-Based Access Control (ANSI/INCITS 359) — permissions granted to roles, roles assigned to users, resolved at token issuance | Accepted |
| [047](adrs/adr-047-claims-augmentation-federated-idp.md) | Claims augmentation for federated/external identity providers — reuses OAuth Token Exchange (RFC 8693) a third time | Accepted |
| [048](adrs/adr-048-spiffe-spire-service-identity.md) | SPIFFE/SPIRE for internal service-to-service and peer-sync identity (reverses prior SPIFFE/SPIRE rejection) | Accepted |
| [049](adrs/adr-049-api-gateway-yarp.md) | API Gateway (YARP) as the single external entry point (reverses prior YARP rejection) | Accepted |
