[← ADR index](../07-adrs.md)

# ADR-082: Tenant-to-tenant federation mapping — ordinary service-credential API calls, mapping accepted as bespoke per pair

Status: Accepted

Context: `ADR-075` named a real, un-glossed-over residual: `ADR-072`'s
`IInterchangeFormatAdapter` was built for *externally standardized*
formats (HL7v2/FHIR/ICH E2B(R3)/GS1-EPCIS), each anchored to a real
published spec, but two federating tenants each have their own
independently-versioned native schema registry (`ADR-030`) with no
shared external spec to anchor a mapping to — and a bespoke adapter per
tenant pair doesn't scale past a handful of partners. Direct design
conversation resolved this session: the transport half needs no new
mechanism at all — **it's just an ordinary authenticated API call**,
reusing this design's own existing service-credential pattern.

Decision:
- **Transport/authentication: `ADR-006`'s existing `client_credentials`
  flow, unchanged.** A sibling tenant's dedicated deployment authenticates
  to another tenant's GraphQL API (or receives its `ADR-060` webhook
  deliveries) using the exact same `client_credentials`-issued service
  credential this design already uses for every other service-to-service
  actor. No new auth mechanism, no new credential type — federation is
  simply one more `client_credentials` client, scoped and revocable the
  same way any other one is.
- **Shape mapping stays accepted as bespoke, per-pair integration code —
  not promoted to a new "native adapter category," and not a shared
  interchange schema tenants opt into.** Both alternatives the open
  question posed were real options; neither wins convincingly enough to
  justify new framework machinery for what `ADR-075` itself already
  called a low-volume, "a handful of partners" case.
- **The bespoke mapping code doesn't need a *new* interface, though —
  it can be written as an ordinary custom `IInterchangeFormatAdapter`
  implementation, registered per tenant pair in that tenant's own
  composition root (`ADR-059`'s pattern, the same shape `ADR-079`
  already established for a domain-scoped, non-core extension).**
  `ADR-072`'s interface contract itself never required its input/output
  formats to be externally standardized — that was true of every
  *built-in* adapter (`Hl7V2Adapter`, `FhirAdapter`, `IchE2bR3Adapter`),
  not a constraint baked into the interface shape. A tenant pair writing
  their own native-to-native adapter, registered the same way any custom
  extension is, is a legitimate use of an interface this design already
  has — not a new category, not a gap.
- **No shared interchange schema is adopted.** Real prior art exists for
  this shape (a common canonical model many parties map to/from, the way
  HL7v2/FHIR themselves function for healthcare) but standing one up for
  a framework whose whole multi-tenant premise is *independently-owned,
  independently-versioned* schemas per tenant (`ADR-030`) would be a much
  larger undertaking than the actual, low-volume problem justifies.
  Revisit only if a real deployment's federation partner count grows
  past the point where per-pair bespoke mapping is genuinely the binding
  cost — not decided preemptively.

Consequences:
- No new interface, no new auth mechanism, no new adapter category — this
  ADR is a scope clarification and a confirming reuse, not new framework
  surface.
- A tenant pair's own bespoke `IInterchangeFormatAdapter` implementation
  is that pair's own maintenance burden, explicitly accepted — consistent
  with `ADR-075`'s own "servers are cheaper than lawsuits," bespoke-
  integration-cost-over-shared-infrastructure posture.
- Resolves the design fork logged in `docs/changes/2026-07-31.md`
  (formerly `docs/10-open-questions.md` row 22).
