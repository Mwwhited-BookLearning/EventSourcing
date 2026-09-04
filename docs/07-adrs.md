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
| [007](adrs/adr-007-derived-event-types.md) | Derived/materialized event types via cross-stream join+projection | Accepted; built (`08-build-plan.md` item 8, Done) |
| [008](adrs/adr-008-event-type-security.md) | Event-type security via per-event-type required claims | Accepted |
| [009](adrs/adr-009-property-level-masking.md) | Property-level masking via a value/masked/erased three-way wrapper (the third branch added by `ADR-057`) | Accepted; built (`08-build-plan.md` item 9, Done) |
| [010](adrs/adr-010-tail-vs-replay-mode.md) | Explicit tail-vs-replay mode on Follow, via a `mode` parameter | Accepted |
| [011](adrs/adr-011-publish-idempotency.md) | Publish idempotency via an optional client-supplied `eventId` + a stored payload hash | Accepted |
| [012](adrs/adr-012-http-query-method.md) | HTTP `QUERY` method (RFC 10008) for OData data-queries, replacing `GET` | Accepted |
| [013](adrs/adr-013-problem-details.md) | Canonical error responses via RFC 9457 Problem Details | Accepted |
| [014](adrs/adr-014-cors-policy.md) | CORS policy — configurable allowlist, deny by default | Accepted |
| [015](adrs/adr-015-cqrs-projections-follow-api.md) | Read-model projections consume the public Follow API, not a private hook | Accepted |
| [016](adrs/adr-016-changekind-centralized-merge.md) | Event-type `ChangeKind` (Full \| Partial) and centralized snapshot merge | Accepted |
| [017](adrs/adr-017-dpop-bound-tokens.md) | DPoP-bound access tokens (RFC 9449) | Accepted; built (`08-build-plan.md`'s "Hardening & Evolution" item, Done) |
| [018](adrs/adr-018-event-upcasting.md) | Event upcasting for schema evolution | Accepted |
| [019](adrs/adr-019-hash-chained-tamper-evidence.md) | Hash-chained events for tamper evidence | Accepted |
| [020](adrs/adr-020-schemaversion-on-publish.md) | Explicit `schemaVersion` on publish, with publish-time upcast validation (the `EventUpcastFailed` dead-letter event type was retired, superseded by `ADR-023`'s persist-everything posture) | Accepted |
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
| [040](adrs/adr-040-ticket-exchange-headerless-clients.md) | URL-embeddable ticket exchange for header-incapable clients (streaming playback / attachment retrieval) | Accepted |
| [041](adrs/adr-041-explicit-composition-first-party-libraries.md) | Explicit composition and first-party libraries over convention-magic (constructor injection, Pure DI, no AutoMapper/third-party logging, `System.Text.Json`) | Accepted |
| [042](adrs/adr-042-gated-authoritative-publish.md) | Gated authoritative publish — Entity Store only reflects approved data; a separate Live View shows the rest (revises `ADR-035`) | Accepted |
| [043](adrs/adr-043-delegated-temporary-access-grants.md) | Delegated, capped, time-boxed read-access grants ("secondary opinion" access) — reuses UCAN delegation (`ADR-036`) | Accepted |
| [044](adrs/adr-044-application-defined-permissions.md) | Application-defined permission/grant types via per-`AppId` trust roots (resolves what UCAN itself leaves out-of-band) | Accepted |
| [045](adrs/adr-045-read-access-audit-log.md) | Read access audit log — every read logged against the reader's identity and trust basis (resolves the open question `ADR-043` raised) | Accepted |
| [046](adrs/adr-046-role-based-access-control.md) | Role-Based Access Control (ANSI/INCITS 359) — permissions granted to roles, roles assigned to users, resolved at token issuance | Accepted |
| [047](adrs/adr-047-claims-augmentation-federated-idp.md) | Claims augmentation for federated/external identity providers — reuses OAuth Token Exchange (RFC 8693) a third time | Accepted |
| [048](adrs/adr-048-spiffe-spire-service-identity.md) | SPIFFE/SPIRE for internal service-to-service and peer-sync identity (reverses prior SPIFFE/SPIRE rejection) | Accepted |
| [049](adrs/adr-049-api-gateway-yarp.md) | API Gateway (YARP) as the single external entry point (reverses prior YARP rejection) | Accepted |
| [050](adrs/adr-050-permission-masking-metadata-spec-extensions-log-redaction.md) | Entity-level permission/masking metadata as OpenAPI/AsyncAPI extensions, reused for log redaction (`Microsoft.Extensions.Compliance.Redaction`) | Accepted |
| [051](adrs/adr-051-peer-discovery-static-seed-list.md) | Peer discovery via explicit static seed-peer configuration (formalizes `docs/comparisons/peer-discovery.md`) | Accepted |
| [052](adrs/adr-052-streaming-redaction-mechanism.md) | Streaming-channel redaction mechanism — configurable strategy, read-time, zero-fill default (formalizes `docs/comparisons/streaming-redaction-mechanism.md`) | Accepted |
| [053](adrs/adr-053-pluggable-upcast-expression-engine.md) | Pluggable declarative upcast expression engine, defaulting to CEL (CEL/JSONata interchangeable behind one interface) | Accepted |
| [054](adrs/adr-054-client-sdk-generation.md) | Client SDK generation — Kiota (OpenAPI, C#+TypeScript), GraphQL Code Generator (TypeScript) + Strawberry Shake (.NET) for GraphQL | Accepted |
| [055](adrs/adr-055-testing-strategy.md) | Testing strategy — MSTest+Moq (unit), Vitest+Vue Test Utils (frontend unit), Testcontainers (integration), Playwright (E2E/UI) | Accepted |
| [056](adrs/adr-056-data-lifecycle-backup-restore.md) | Data lifecycle designed for easy backup/restore — authoritative vs. rebuildable stores, native provider PITR | Accepted |
| [057](adrs/adr-057-gdpr-erasure-crypto-shredding.md) | GDPR/CCPA erasure via crypto-shredding — per-entity data-encryption keys, destroyed on request (revises `ADR-009`) | Accepted |
| [058](adrs/adr-058-rate-limiting-quota.md) | Per-tenant rate limiting via ASP.NET Core's built-in `RateLimiting` middleware | Accepted |
| [059](adrs/adr-059-composition-root-extensibility-model.md) | Extensibility is composition-root registration only — no dynamic/runtime plugin discovery, ever | Accepted |
| [060](adrs/adr-060-outbound-webhooks.md) | Outbound webhook/notification support — reuses the durable outbox primitive, Standard Webhooks-shaped signing | Accepted |
| [061](adrs/adr-061-data-residency-region-pinning.md) | Data residency — per-`AppId` allowed regions, enforced at replication/sharding assignment | Accepted |
| [062](adrs/adr-062-package-distribution-model.md) | Framework distributed as installable packages (NuGet + npm), not forked/cloned per deployment | Accepted |
| [063](adrs/adr-063-staged-distributed-correctness-testing.md) | Staged adoption of distributed-correctness testing — property-based + in-process fault injection now, network-level/Jepsen as a production-readiness escalation | Accepted |
| [064](adrs/adr-064-actor-id-on-every-event.md) | Capture `ActorId` on every `StoredEvent`, not just self-attested ones | Accepted |
| [065](adrs/adr-065-local-active-scope-caching-erasure-invalidation.md) | Local/edge clients cache only the active-scoped subset, and purge it on erasure | Accepted |
| [066](adrs/adr-066-digital-signoff-step-up-auth.md) | Digital sign-off for regulated actions — RFC 9470 step-up authentication + an envelope `Signature` object | Accepted |
| [067](adrs/adr-067-control-plane-actions-as-reserved-events.md) | Control-plane actions (schema registration, RBAC grants, trust-root registration) are reserved event types in the same Event Log | Accepted |
| [068](adrs/adr-068-lineage-export-and-bitemporal-playback.md) | Lineage-scoped event export for dev/support replay, bitemporal system-time playback, and a self-contained offline player for litigation review | Accepted |
| [069](adrs/adr-069-pluggable-outbox-flush-triggers.md) | Pluggable outbox flush triggers — opportunistic, scheduled ("phone home"), and explicit/manual, including a fully offline transfer | Accepted |
| [070](adrs/adr-070-device-input-integration.md) | Device input integration — WebUSB/WebHID/Web Serial/Web Bluetooth, with a native-bridge fallback | Accepted |
| [071](adrs/adr-071-pci-sad-registration-boundary.md) | PCI-DSS Sensitive Authentication Data can never be registered as a schema field — a hard boundary at registration, not publish | Accepted |
| [072](adrs/adr-072-bulk-ingestion-and-interchange-format-adapters.md) | Bulk/batch ingestion, and external interchange-format adapters (HL7v2/FHIR inbound, regulatory formats outbound) | Accepted |
| [073](adrs/adr-073-accessibility-standard.md) | Accessibility standard — WCAG 2.1 AA baseline, WCAG 2.2 AA forward-looking, independent of which UI architecture renders a screen | Accepted |
| [074](adrs/adr-074-sbom-and-soup-list.md) | SBOM generation (`microsoft/sbom-tool`), and the library catalog doubles as a SOUP list (IEC 62304) | Accepted |
| [075](adrs/adr-075-siloed-per-tenant-deployment.md) | Siloed, dedicated-per-tenant deployment — revises `ADR-030`'s pool model; cross-tenant exchange is federation via `ADR-060`/`ADR-072`, never shared infrastructure | Accepted |
| [076](adrs/adr-076-ef-core-migration-bundles-deployment.md) | Database schema deployment via EF Core migration bundles, applied as a single deploy-time step (never `Database.Migrate()` at app startup) | Accepted |
| [077](adrs/adr-077-dynamic-feature-flag-configuration-provider.md) | Instant feature-flag toggles via a chained, reloadable `IConfigurationProvider` — resolves the apparent `ADR-038`/`ADR-041`/`ADR-058` contradiction | Accepted |
| [078](adrs/adr-078-leader-election-database-lease.md) | Single-active-worker leader election via a database-backed lease, per independent worker role (Router — which also runs UpcastMaterializer inline, not as its own leased role — and each outbox pump) | Accepted |
| [079](adrs/adr-079-sanctions-screening-extensibility-seam.md) | Pluggable sanctions/watchlist screening (`ISanctionsScreeningProvider`) — an application-scoped (KYC/Meridian) extension point, not core Duplex | Accepted |
| [080](adrs/adr-080-dependency-scanning-and-build-provenance.md) | Dependency-vulnerability scanning (Dependabot, `dotnet list package --vulnerable`, `npm audit`) and build provenance (NuGet author signing, `npm publish --provenance`, SLSA Level 2) on top of `ADR-074`'s SBOM | Accepted |
| [081](adrs/adr-081-thread-id-and-telemetry-pointer-list.md) | `TelemetryChannel.ThreadId` for multi-channel session grouping, and `TelemetryPointer` generalized to a list — revises `ADR-031` | Accepted |
| [082](adrs/adr-082-tenant-federation-mapping.md) | Tenant-to-tenant federation mapping — ordinary `client_credentials` API calls; shape mapping accepted as bespoke per pair, no new adapter category | Accepted |
| [083](adrs/adr-083-monotonic-timer-clock-lie-detection.md) | Optional `TelemetrySample.MonotonicElapsedMicros`, alongside wall-clock `Timestamp`, for device-telemetry clock-lie detection | Accepted |
| [084](adrs/adr-084-liveness-readiness-probe-semantics.md) | Liveness/readiness probe semantics — readiness stays healthy through degraded peers by default, consistent with `ADR-023`'s never-block posture | Accepted |
| [085](adrs/adr-085-performance-regression-testing-staged.md) | Performance-regression testing, staged like `ADR-063` (BenchmarkDotNet now, NBomber deferred) — no framework-wide numeric targets, those are deployment-specific | Accepted |
| [086](adrs/adr-086-rfc-3161-trusted-timestamping.md) | RFC 3161 trusted timestamping for `ADR-066` signatures and `ADR-068` litigation exports, via a pluggable `ITimestampAuthorityClient` | Accepted |
| [087](adrs/adr-087-i18n-l10n-architectural-scope.md) | i18n/l10n — framework-level architectural requirement (`Accept-Language`, string externalization, culture-aware formatting, CSS logical properties); translated content is domain-owned | Accepted |
| [088](adrs/adr-088-mechanism-level-otel-instrumentation.md) | Mechanism-level OpenTelemetry instrumentation (fold lag, outbox depth/age, webhook delivery lag, hash-chain verification) — extends `ADR-026`; alert thresholds/on-call stay deployment-specific | Accepted |
| [089](adrs/adr-089-event-log-archival-segment-detachment.md) | Event Log/`AccessLog` archival — detach a verified segment to `ADR-032`'s existing pluggable `IAttachmentContentStore`, no new interface; tier/backend is provider-driven | Accepted |
| [090](adrs/adr-090-read-your-writes-via-existing-filters.md) | Read-your-writes stays declined as a built-in guarantee — achievable today via `EventId`/`OriginId`+`SequenceNumber` filtering; no frontier token adopted | Accepted |
| [091](adrs/adr-091-ci-cd-platform-github-actions.md) | CI/CD platform — GitHub Actions, because that's where this repository lives; revisit if that ever changes | Accepted |
| [092](adrs/adr-092-benign-actor-trust-model-perimeter-defense.md) | Core-engine trust model assumes non-malicious actors; hostile-traffic defense (WAF/gateway) is a deployment-perimeter concern, not a framework one | Accepted |
| [093](adrs/adr-093-signing-secret-rotation-dual-signature.md) | `ADR-060`'s webhook signing secret becomes a current+previous pair with dual-signature emission (Standard Webhooks' own mechanism); `ADR-040`'s ticket-exchange rotation was found unbuildable as originally assumed and remains open/unbuilt (see `TODO.md`); rotation cadence stays ops-configurable | Accepted |
| [094](adrs/adr-094-expected-response-tracking.md) | Expected-response tracking — a generic `RespondsToEventId` envelope field (Correlation Identifier) + opt-in `EventTypeDefinition.ExpectedResponse` registry declaration, escalation policy left to the application | Accepted |
| [095](adrs/adr-095-push-notification-wake-signal.md) | A push-notification "wake sooner" layer on top of every background worker's existing poll loop (which stays the sole correctness guarantee) — Postgres `LISTEN`/`NOTIFY`, SQL Server Service Broker, SQLite an in-process signal backed by a durable marker row; proven on `RouterWorker` first, the other five workers a named follow-up | Accepted |
| [096](adrs/adr-096-searchable-blind-index-bucketed-range.md) | Searchable blind-index (equality) and bucketed-range indexes over crypto-shredded fields, with a cardinality-aware registration guardrail (extends `ADR-057`) | Accepted |
| [097](adrs/adr-097-order-revealing-encryption-opt-in.md) | Order-Revealing Encryption (ORE) as an opt-in, loudly-gated real range-comparison mechanism — sibling to `ADR-096`, no-override guardrail on classified fields | Accepted |
| [098](adrs/adr-098-in-database-predicate-evaluator-seam.md) | Pluggable in-database native predicate evaluator seam (`IEncryptedPredicateEvaluator`) for exact-match comparison without app-tier bulk decryption — designed, not yet built | Accepted |
| [099](adrs/adr-099-naive-ui-router-left-nav-shell.md) | `client-web` adopts Naive UI + Vue Router behind a left-hand-nav shell (Azure Portal/DevOps-style), restyling every existing component in the same pass; render-side grid pagination now, a real paged query and a chosen charting library both deferred and tracked | Accepted |
| [100](adrs/adr-100-configurable-presentation-type-charting.md) | Adopt Apache ECharts (via `vue-echarts`) and a narrow declarative `chartable`/`chartType` field config, first wired to Meridian's `MatchConfidence` gauge in the KYC Analyst Queue | Accepted |
| [101](adrs/adr-101-plantuml-native-user-flow-engine.md) | PlantUML-native executable flow engine (real ANTLR4 grammar/Listener) + `PendingTask` cross-domain read model — a read-side `IProjection<T>` consumer, no write-side engine, no durable workflow-instance state | Accepted |
| [102](adrs/adr-102-cross-provider-peer-sync-and-multi-provider-topology.md) | Cross-provider peer sync, proven real (`ADR-033`'s existing mechanism, verified provider-agnostic), plus a configurable multi-provider Aspire topology (`Topology:Enable{Sqlite,SqlServer}Peer`) — orchestration-level, does not reverse `ADR-001` | Accepted |
| [103](adrs/adr-103-schema-registry-cross-peer-replication.md) | Schema-registry state replicates across peers via a targeted Router reactor (widened `SchemaRegistered` notification payload + a real per-site `OriginId`), not a generic `EventTypeDefinition` rearchitecture — resolves `docs/10-open-questions.md` row 1 | Accepted |
| [104](adrs/adr-104-live-revocation-check-for-delegated-grants.md) | Live revocation check for delegated UCAN grants (`ADR-043`), alongside unchanged offline self-verification — a `UcanDelegationRevoked` reserved event, consulted at validation time; CRL/OCSP's own real-world hybrid shape, resolves `docs/10-open-questions.md` row 2 | Accepted |
| [105](adrs/adr-105-rbac-with-application-scoped-permissions.md) | RBAC (not ABAC/ReBAC/Hybrid/DACL/Classification) as Duplex's chosen authorization decision model — generalized roles as JWT claims, application-scoped permission expansion via RFC 8693 Token Exchange or Gateway middleware, chosen per application | Accepted |
| [106](adrs/adr-106-oidc-scope-devidp-vs-production-idp.md) | Duplex's resource-server side already supports any compliant OIDC/OAuth2 IdP with zero code changes; `EventStore.DevIdp` deliberately stays client-credentials-only, not expanded into a real interactive Provider — adopts RFC 7591/7592 (Dynamic Client Registration) and RFC 8414 (AS Metadata) | Accepted |
| [107](adrs/adr-107-delegated-grant-issuance-audit-event.md) | Delegated-grant issuance gets a real `ucanDelegationIssued` audit event, symmetric with `ADR-104`'s revocation event — `UcanDelegation.Create` stays fully offline; recording issuance is a separate, opt-in Publish-API call; resolves `docs/10-open-questions.md`'s last row | Accepted |
