[← ADR index](../07-adrs.md)

# ADR-050: Entity-level permission/masking metadata as OpenAPI/AsyncAPI extensions, reused for log redaction

Status: Accepted

Context: Direction received this session, generalizing beyond
`docs/comparisons/masking-strategies.md`'s "which strategy to build
first" question: (1) entities, not just properties, should be markable
with declarative, attribute-like permission/masking metadata; (2) that
metadata should be surfaced in the generated OpenAPI/AsyncAPI documents
as extensions, not just enforced internally; (3) the same metadata
should also drive **log redaction** — preventing PII/PHI/PCI from being
written to logs at all, a sink `ADR-009`'s masking never covered
(`ADR-009` only ever touches query/stream *responses*; internal
application logging was never in scope there).

Decision:
- **`ADR-008`'s `RequiredPublishClaim`/`RequiredReadClaim` generalize
  from one fixed claim per direction to a list**: `RequiredClaims:
  [{ Direction: Publish|Read, Claim: "type:value" }]` — the
  attribute-like shape requested (multiple markers attachable to one
  entity type, the way multiple C# attributes decorate one class),
  superseding `ADR-008`'s "v1 supports exactly one required claim per
  direction, not an AND/OR set" limitation. **Resolved: `OR` by
  default** — a caller needs *any one* of the claims declared for a
  given direction, not all of them. Deliberately the simpler default,
  not a permanent ceiling: richer combinations (`AND`, or a real
  boolean expression over claims) are a plausible future extension,
  particularly if `docs/patterns/multi-axis-authority-assurance.md`'s
  multi-axis split is ever adopted (independent axes would be exactly
  the case where "hold this AND that" genuinely differs from "hold this
  OR that") — not built now, since nothing requires it yet.
- **Both this entity-level metadata and `ADR-009`'s existing
  property-level `x-masking` are guaranteed to survive into the
  generated OpenAPI 3.1 / AsyncAPI 3.0 documents as real Specification
  Extensions** — both specs formally define `x-`-prefixed vendor
  extension fields for exactly this purpose (AsyncAPI's own spec notes
  its extension mechanism is adapted directly from OpenAPI's). Concretely:
  `x-required-claims` at the schema/operation level (new), `x-masking`
  at the property level (already existing, `ADR-009` — this ADR adds
  the *guarantee* that `OpenApiDocumentBuilder`/`AsyncApiDocumentBuilder`
  (`ADR-002`) actually emit it into the rendered spec, not just track it
  internally). A reader of the generated docs (Scalar, AsyncAPI React,
  `ADR-025`) can see directly which claim is required and which fields
  are masked, without registry access.
- **The same classification metadata drives log redaction**, adopting
  [`Microsoft.Extensions.Compliance.Redaction`](https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction)
  + `Microsoft.Extensions.Telemetry` — first-party, consistent with
  `ADR-041`'s preference, and already the same `Microsoft.Extensions.*`
  family `ADR-026`'s logging/OpenTelemetry story is built on. Two
  genuinely different usage shapes, stated honestly rather than
  conflated:
  - **This framework's own statically-typed internal log call sites**
    (tracing, diagnostics — e.g. logging a `ClientId`) use the
    library's documented pattern directly: a custom `DataClassification`
    taxonomy, custom attributes deriving from it, applied to
    `[LoggerMessage]` source-generated method parameters, redacted
    automatically via a registered `Redactor`.
  - **Schema-driven `Payload`-derived logging** (dynamic JSON — there's
    no compile-time property to attach a static attribute to) instead
    resolves a `Redactor` **programmatically**, via
    `IRedactorProvider.GetRedactor(classification)`, using the
    classification `x-masking`'s `regulatoryClassification`/
    `requiredClaim` already carries in the registry, applied at the
    point a payload-derived value is about to be logged. Same
    underlying primitives (`Redactor`, `DataClassification`), a
    different (dynamic, not attribute-based) call shape — the honest
    reason these are two shapes, not one.
- **This extends masking's enforcement *surface*, not its stored-data
  guarantee** — `ADR-009`'s "publish is never affected,
  `StoredEvent.Payload` is never wrapped/mutated" stays exactly true;
  this adds a second sink (logs) that must respect the same
  classification, alongside the existing query/stream response sink,
  never touching what's actually persisted.

Consequences:
- `docs/data/schema-registry.md`'s `RequiredPublishClaim`/
  `RequiredReadClaim` fields need generalizing to the list-shaped
  `RequiredClaims` described above — a real, mechanical propagation
  cost against `ADR-008`'s existing description, not done this pass.
- `03-api-contracts.md`'s OpenAPI/AsyncAPI contract examples need to
  show `x-required-claims`/`x-masking` explicitly in generated output —
  not done this pass, added to the already-tracked GraphQL-contract
  rewrite debt (`CLAUDE.md`).
- **Resolved**: exposing `x-required-claims`/`x-masking` in the
  publicly-readable generated OpenAPI/AsyncAPI documents (`ADR-002`'s
  docs are anonymous, per `features/auth.md`) is judged **not** a
  meaningful leak by default — revealing that a claim is required
  isn't the same as revealing the value, and undiscoverability of the
  API's own shape is weak protection to begin with. For deployments
  wanting a stricter posture regardless, `ADR-002` adds a config toggle
  to disable the spec endpoints entirely.
- **Log redaction only helps where a log call site actually routes
  through it.** An ad hoc `logger.LogInformation($"...{payload}...")`
  that bypasses the structured `[LoggerMessage]`/`Redactor` path entirely
  is not automatically caught by adopting this library — a real
  discipline/code-review concern once built, not solved by the library
  alone. Stated honestly as a residual risk.

**Compliance note** (a proving-ground compliance review, this session):
log redaction is a distinct sink from `ADR-009`'s query/stream masking
(that ADR's own compliance note covers the response-serialization sink,
not duplicated here) — keeping PII/PHI out of application logs
specifically is what HIPAA's Minimum Necessary standard (45 CFR §
164.502(b)) and GDPR Art. 5(1)(f)'s confidentiality principle both also
require of *any* processing surface, logs included, not just an
externally-facing query response.
