[← Data model index](../02-data-model.md)

# Schema Registry

```csharp
public class EventTypeDefinition
{
    public string AppId { get; set; } = default!;      // tenant/application scoping key — part of the composite key (ADR-030)
    public string Name { get; set; } = default!;      // e.g. "OrderPlaced" — canonical casing, stored lowercase for lookup; unique only within AppId
    public int Version { get; set; }
    public string JsonSchema { get; set; } = default!; // raw JSON Schema document, stored as text
    public DateTimeOffset RegisteredAt { get; set; }
    public bool IsActive { get; set; }                 // latest version flag
    public ParentValidationMode ParentValidationMode { get; set; } = ParentValidationMode.Strict;
    public string? RequiredPublishClaim { get; set; }  // "type:value", e.g. "clearance:secret" — null = no extra restriction
    public string? RequiredReadClaim { get; set; }     // gates Follow + Lineage; null = no extra restriction
    public ChangeKind ChangeKind { get; set; }          // Full | Partial — required, no default (ADR-016); Partial payloads are Optional<T>-wrapped per-property (ADR-022)
    public string EntityIdField { get; set; } = default!; // JSON path into Payload that yields this type's uniqueId (ADR-021) — required, no default
    public string? UpcastFromPrevious { get; set; }     // OData compute() expression list, this version <- previous (ADR-018); materialized on success (ADR-027)
    public string? DowncastToPrevious { get; set; }     // OData compute() expression list, previous <- this version (ADR-028); read-time only, never materialized
    public RejectionBehavior RejectionBehavior { get; set; } = RejectionBehavior.Annotate; // Annotate | Compensate — how an authorityDecision:rejected is handled for this type (ADR-035, comparisons/authority-rejection-behavior.md)

    public List<FilterableField> FilterableFields { get; set; } = new();
}

public enum ChangeKind
{
    Full,    // this event type's payload replaces everything known about its key
    Partial  // this event type's payload merges onto (never overlays a missing/masked field over) existing state
}

public enum ParentValidationMode
{
    Strict,     // publish is rejected (400) if any parentEventId does not resolve to a stored event
    Permissive  // dangling/forward parentEventId references are accepted and stored as unresolved
}

public enum RejectionBehavior
{
    Annotate,   // default — a rejected event stays as originally published, flagged via AuthorityStatus only (ADR-035)
    Compensate  // a rejected event triggers a compensating patch, per-type opt-in where the domain needs it
}

public class FilterableField
{
    public int Id { get; set; }
    public string EventTypeName { get; set; } = default!;
    public int EventTypeVersion { get; set; }
    public string JsonPath { get; set; } = default!;    // e.g. "$.Amount"
    public FilterableFieldType DataType { get; set; }   // String, Number, Boolean, DateTimeOffset
    public bool IsIndexed { get; set; }                 // whether a DB index/computed column exists
}

public enum FilterableFieldType { String, Number, Boolean, DateTimeOffset }
```

## Application-defined permission trust roots (`ADR-044`)

```csharp
public class AppTrustRoot
{
    public string AppId { get; set; } = default!;       // part of the composite key (ADR-030)
    public string IssuerDid { get; set; } = default!;   // the DID this AppId trusts as a root of trust for its own custom permission/capability namespace
    public string? Description { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
}
```

Registering a DID here is what makes it authoritative for
minting/delegating `AppId`-scoped custom permissions via UCAN (`ADR-036`)
— resolving the one thing the UCAN spec itself leaves out-of-band (which
DID counts as a root of trust for a given namespace). The core engine
never validates *what* a permission string means, only that a presented
UCAN's delegation chain roots in a DID registered here for the `AppId`
the request is scoped to. Who may register/deregister a trust root is
not designed further here — see `10-open-questions.md`.

## Per-provider index strategy for filterable fields

When a `FilterableField` is marked `IsIndexed = true`, the registry service
issues a provider-specific migration to add a computed/expression index:

| Provider | Mechanism |
|---|---|
| SQLite | Expression index: `CREATE INDEX ... ON Events(json_extract(Payload, '$.Amount'))` (SQLite 3.9+) |
| PostgreSQL | Expression index: `CREATE INDEX ... ON "Events" ((("Payload"::jsonb) ->> 'Amount'))` |
| SQL Server | Computed column + index: `ALTER TABLE Events ADD Amount AS JSON_VALUE(Payload, '$.Amount'); CREATE INDEX ... ON Events(Amount)` |

This is generated/applied by the Schema Registry Service at field-registration
time, not part of the baseline EF model — see
`../05-schema-registry-and-spec-generation.md`.

## Event-type security (required claims)

`RequiredPublishClaim` and `RequiredReadClaim` are a second, orthogonal
authorization dimension from the operation-level scopes in `ADR-006`:
scopes (`events:publish`, `events:follow`, `events:lineage:read`,
`registry:admin`) gate *whether you can call the operation at all*;
these two fields gate *whether you may touch a specific event type*, per
`ADR-008`. Both are optional, single `"type:value"` claim strings (e.g.
`"clearance:secret"`), checked with `ClaimsPrincipal.HasClaim(type, value)`
— v1 supports exactly one required claim per direction, not an AND/OR set.

- `RequiredPublishClaim` gates `POST /publish/{event-type}` for this type.
- `RequiredReadClaim` gates `QUERY /follow/{event-type}` (`ADR-012`; checked at connect
  time) **and** the Lineage API — see `../03-api-contracts.md`, "Lineage API",
  for why a restricted node anywhere in an ancestors/descendants traversal
  fails the whole request rather than being stubbed out.
- **Claims can optionally carry an entity-scope restriction (`ADR-043`)**:
  a delegated/granted claim (e.g. from a "secondary opinion" access
  grant) may be scoped to one specific `EntityId` rather than applying
  blanket, in which case the check becomes "does the caller have this
  claim, *and* does it apply to this `EntityId`" — an ordinary, unscoped
  claim (the default) is unaffected and behaves exactly as above.
- Both are `null` by default — registering an event type with neither set
  behaves exactly as before this feature existed.
- Enforcement needs the caller's claims to already be populated by JWT
  bearer auth (`ADR-006`), so this can only be enforced once that auth
  middleware exists — see `../08-build-plan.md`, Phase 6.

Property-level **masking** — wrapping individual field *values* in a
`{"value": ...}` / `{"masked": "***"}` envelope for callers who lack a
field-specific claim — is a related, finer-grained feature (`ADR-009`, v1
scope). No new column on `EventTypeDefinition` for it: masking rules live
inside the registered `JsonSchema` text itself, as an `x-masking` extension
on a property (`{ "requiredClaim": "type:value", "strategy": "FixedValue",
"maskedValue": "***" }`), an array's `items` (when scalar — wraps each
element), or a property nested inside a complex-object `items` schema
(wraps just that property per element). Unlike an earlier, since-replaced
`null`-out design, this works on **any** scalar-typed field — including
required, non-nullable ones — because the wrapper is a new type at that
position, not a mutation of the original type's slot. It applies only to
query/stream responses, never to the stored or published `Payload`; see
`ADR-009` and `../06-solution-structure.md` for the schema-plus-data
transform that computes it.

`x-masking` also carries three **optional, schema-only** descriptive
fields — `regulatoryClassification`, `governanceBody`,
`regulationReference` (e.g. `"PHI"` / `"HHS/OCR"` /
`"HIPAA 45 CFR §164.514(b)"`). These are documentation, not behavior: the
masking transform never reads them, and they never appear in the runtime
wrapper — they exist so a schema self-documents *why* a field is masked,
discoverable via the registry and generated specs, per `ADR-009`.

**There is no deletion/erasure mechanism for regulated data, by design.**
`Payload` is never mutated or removed once stored, for a masked field the
same as any other — masking is a read-time presentation transform, not a
storage-layer redaction. `ADR-009` records this as explicitly settled, not
merely unaddressed.
