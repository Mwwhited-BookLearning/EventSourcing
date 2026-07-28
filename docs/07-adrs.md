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
