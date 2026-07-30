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
    public string? UpcastFromPrevious { get; set; }     // originally an OData compute() expression list, this version <- previous (ADR-018); materialized on success (ADR-027); evaluated via a pluggable IUpcastExpressionEvaluator, CEL by default (ADR-053), since ADR-037 moved this off OData entirely
    public string? DowncastToPrevious { get; set; }     // previous <- this version (ADR-028); read-time only, never materialized; same pluggable-evaluator move as UpcastFromPrevious above (ADR-037/ADR-053)
    public RejectionBehavior RejectionBehavior { get; set; } = RejectionBehavior.Annotate; // Annotate | Compensate — how an authorityDecision:rejected is handled for this type (ADR-035, comparisons/authority-rejection-behavior.md)
    public RequiredSignature? RequiredSignature { get; set; } // null = no sign-off required; set = publish must satisfy an RFC 9470 step-up challenge first (ADR-066)

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

public class RequiredSignature
{
    public List<string> AcrValues { get; set; } = new(); // RFC 9470 acr_values -- which authentication context the caller's token must carry
    public int? MaxAge { get; set; }                      // RFC 9470 max_age (seconds) -- how recently that authentication must have occurred
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
resolved in `ADR-044`'s Consequences — a narrow `registry:trust-admin`
scope, separate from `registry:admin` — see
`../comparisons/trust-root-registration-gate.md` for the full comparison.

## Roles (`ADR-046`)

```csharp
public class Role
{
    public string AppId { get; set; } = default!;         // part of the composite key (ADR-030)
    public string RoleName { get; set; } = default!;
    public List<string> Permissions { get; set; } = new(); // opaque claim/scope strings -- the framework never validates what one means
}
```

A named, `AppId`-scoped bundle of the same opaque permission strings
used everywhere else in this design (`RequiredPublishClaim`/
`RequiredReadClaim` values, `ADR-008`; `ADR-044`'s application-defined
permission types). **Role assignment (which user has which role) and
direct per-user permission grants (`UserPermission`, additive-only —
`ADR-046`) are both identity-provider state, not core-engine data** —
the same scoping `ADR-006` already drew around dev-mode seeded clients.
The IdP expands a user's roles plus any direct grants into one
flattened claim set at token issuance; every claim check in this design
(`ADR-008`, `ADR-043`, `ADR-044`) is unchanged and unaware whether a
claim arrived via a role, a direct grant, or neither.

## Trusted federation issuers (`ADR-047`)

```csharp
public class TrustedFederationIssuer
{
    public string AppId { get; set; } = default!;   // part of the composite key (ADR-030)
    public string Issuer { get; set; } = default!;   // the external IdP's `iss` value
    public string JwksUri { get; set; } = default!;  // where to fetch that issuer's signing keys
    public string? Description { get; set; }
}
```

Names which external, already-authoritative OIDC IdP(s) this framework
will accept a Token Exchange `subject_token` from, for a given `AppId`
— a different question from `ADR-044`'s `AppTrustRoot` (which DID is a
root of trust for UCAN capability delegation), so its own entity rather
than a reused shape.

## Data residency (`ADR-061`)

```csharp
public class AppDataResidencyPolicy
{
    public string AppId { get; set; } = default!;         // part of the composite key (ADR-030)
    public List<string> AllowedRegions { get; set; } = new(); // e.g. ["eu-west", "eu-central"] -- matches a peer's Region tag (ADR-051's SeedPeers config)
}
```

Absent for a given `AppId`, that tenant is unconstrained (today's
behavior, unchanged) — this table is purely additive. Enforced at
`ADR-033`'s peer-sync outbox, which filters candidate destination peers
to those tagged with one of the listed regions before including an
`AppId`'s events in an outbound sync batch — not enforced here at the
registry layer, which only holds the *declared* constraint.

## Webhook subscriptions (`ADR-060`)

```csharp
public class WebhookSubscription
{
    public Guid SubscriptionId { get; set; }
    public string AppId { get; set; } = default!;          // part of the composite key (ADR-030)
    public string TargetUrl { get; set; } = default!;
    public string SigningSecret { get; set; } = default!;    // HMAC-SHA256 key, Standard Webhooks-shaped (ADR-060)
    public List<string> EventTypes { get; set; } = new();    // which event/entity types this subscription wants notified about
    public string FixedClaimsSnapshot { get; set; } = default!; // JSON -- the claim set computed once at registration time (ADR-060), never re-evaluated per delivery
    public bool Active { get; set; } = true;
    public DateTimeOffset RegisteredAt { get; set; }
}
```

`FixedClaimsSnapshot` is what `ADR-060` means by "a fixed claim set
computed once at registration time" — every payload this subscription
is ever sent is masked (`ADR-009`) against this snapshot, never a
live re-check, the same "claims fixed for a connection's lifetime" rule
`ADR-009` already applies to a Follow connection. `WebhookOutbox`/
`WebhookDeliveryCursor` (the delivery-side durable queue and per-
subscription cursor) are a separate concern from this registration
record — not yet placed in this file, still flagged as remaining
propagation work per `ADR-060`'s own Consequences.

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
`{"value": ...}` / `{"masked": "***"}` / `{"erased": true}` envelope
(a three-way `oneOf`, the third branch added by `ADR-057`) for callers
who lack a field-specific claim, or for a field whose crypto-shredding
key has been destroyed — is a related, finer-grained feature (`ADR-009`,
v1 scope). No new column on `EventTypeDefinition` for it: masking rules
live inside the registered `JsonSchema` text itself, as an `x-masking`
extension on a property (`{ "requiredClaim": "type:value", "strategy":
"FixedValue", "maskedValue": "***" }`), an array's `items` (when scalar
— wraps each element), or a property nested inside a complex-object
`items` schema (wraps just that property per element). Unlike an
earlier, since-replaced `null`-out design, this works on **any**
scalar-typed field — including required, non-nullable ones — because
the wrapper is a new type at that position, not a mutation of the
original type's slot. It applies only to query/stream responses, never
to the stored or published `Payload`; see `ADR-009` and
`../06-solution-structure.md` for the schema-plus-data transform that
computes it.

`x-masking` also carries three **optional, schema-only** descriptive
fields — `regulatoryClassification`, `governanceBody`,
`regulationReference` (e.g. `"PHI"` / `"HHS/OCR"` /
`"HIPAA 45 CFR §164.514(b)"`) — plus two behavioral ones added since:
`erasureScope` (`ADR-057` — a JSON Pointer to the property naming the
`EntityId` whose crypto-shredding key actually protects this field,
for PII that belongs to a different entity than the event's own; a
field with no `erasureScope` defaults to the event's own `EntityId`)
and `revealOnDemand` (`ADR-009`'s amendment — a `showFirst`/`showLast`/
`maskChar`/`preserveSeparators` object computing a display-safe
`displayMask` sibling on the `value` branch, for shoulder-surfing
mitigation independent of claims-based access). The three purely
descriptive fields are documentation, not behavior: the masking
transform never reads them, and they never appear in the runtime
wrapper.

**One `regulatoryClassification` value is not merely descriptive:
`"PCI-SAD"` (`ADR-071`) makes registration itself reject the event
type (`400`).** PCI-DSS Sensitive Authentication Data (CVV2/CVC2/CID,
full track data, PIN blocks) may never be persisted after
authorization, encrypted or not — masking and `ADR-057`'s crypto-
shredding both still write the real value to `Payload` first, which
this rule already prohibits regardless of what happens afterward. A
schema author self-declares `"PCI-SAD"` the same way they'd declare
`"PHI"`/`"PCI"`; registration checks for exactly this one value and
refuses the event type outright, the one place this design still
enforces reject-on-invalid after `ADR-023`. A full card number (PAN)
is *not* SAD and is unaffected — ordinary masking/crypto-shredding
already covers it like any other classified field.

~~**There is no deletion/erasure mechanism for regulated data, by
design.** `Payload` is never mutated or removed once stored, for a
masked field the same as any other — masking is a read-time
presentation transform, not a storage-layer redaction. `ADR-009`
records this as explicitly settled, not merely unaddressed.~~
**Superseded by `ADR-057`**: erasure is now a real requirement, solved
via crypto-shredding — a classified field's *value* is encrypted before
it's first stored, so "erasure" destroys the key that makes existing
ciphertext readable rather than ever touching `Payload` itself. See
`ADR-057` and `ADR-009`'s own updated closing note.
