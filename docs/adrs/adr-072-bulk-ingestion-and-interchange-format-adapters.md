[← ADR index](../07-adrs.md)

# ADR-072: Bulk/batch ingestion, and external interchange-format adapters (HL7v2/FHIR inbound, regulatory formats outbound)

Status: Accepted

Context: A proving-ground-domain review (this session) surfaced the
same underlying gap from three different real examples: pharmacovigilance
needs outbound ICH E2B(R3) XML to EudraVigilance/FAERS, DSCSA needs
outbound GS1/EPCIS-formatted trading-partner exchange, and — confirmed
directly from the clinical-trials proving-ground domain — **inbound**
integration with hospital EMR systems via **HL7v2 and/or FHIR**. Every
publish path decided so far (`ADR-023` onward) is one event at a time,
and nothing decided transforms between this framework's own JSON Schema
shape and an externally-mandated interchange format in either direction.

**Verified the real transport reality before designing anything
bespoke**: HL7v2 is not carried over HTTP in practice — nearly every
production HL7v2 interface uses **MLLP** (Minimal Lower Layer Protocol,
TCP-based, no inherent security of its own — relies on TLS/network
controls), the default transport for integration engines like Mirth
Connect, Rhapsody, and Cloverleaf. [Google Cloud's own MLLP
adapter](https://github.com/GoogleCloudPlatform/mllp/) — a small
component that receives HL7v2 over MLLP/TCP and forwards it to a REST
API — is the real, concrete precedent this ADR's inbound adapter shape
follows, not a bespoke invention. FHIR, by contrast, is RESTful/HTTP-
native and needs no such bridge.

Decision:

**1. Bulk/batch ingestion:**
- **A new batch publish endpoint, `POST /publish/batch`**, accepting an
  NDJSON or JSON-array body of multiple event submissions in one
  request. **Not a new persistence model** — each event inside the
  batch still goes through `ADR-023`'s exact same persist-everything
  path (its own `SequenceNumber`, `ChainHash`, idempotency check via
  `ADR-011`); batching is a transport/efficiency optimization (one HTTP
  round trip, one DB transaction for N inserts) over what already
  happens per-event, not a different guarantee.
- **Response is an array of the same per-event status envelope
  `ADR-023` already defines**, one per submitted event, in submission
  order — a batch never fails or succeeds as a unit; each event inside
  it is exactly as independently persist-everything as it would be sent
  alone.

**2. External interchange-format adapters:**
- **A new extensibility seam, `IInterchangeFormatAdapter`**, added to
  `docs/extensibility-points.md` — the same keyed-DI shape every other
  seam in this design uses, one implementation per external standard
  (`Hl7V2Adapter`, `FhirAdapter`, `IchE2bR3Adapter`, `Gs1EpcisAdapter`,
  ...), chosen per integration need, several active simultaneously.
- **Inbound**: an adapter receives a message in the external format,
  transforms it into this framework's registered `JsonSchema` shape,
  and publishes it through the *ordinary* publish path (`ADR-023`,
  including `ADR-072`'s new batch endpoint where the source system
  itself batches) — inheriting persist-everything, non-authoritative
  capture (`ADR-035`, a reasonable default for EMR-sourced data arriving
  through an interface engine), and everything else automatically. For
  **HL7v2 specifically**, this means a small, dedicated MLLP-listener
  component (matching Google Cloud's own adapter shape) — HL7v2's real
  transport is TCP/MLLP, not HTTP, and pretending otherwise would
  misrepresent how every real hospital interface actually works. FHIR's
  own inbound adapter is an ordinary HTTP resource consumer, no bridge
  needed.
- **Outbound**: an adapter transforms an outbound event into the
  external format *before* delivery — composing with `ADR-060`'s
  webhook delivery as an extra transform step ahead of the HTTP POST,
  not a replacement for it.
- **This relies on the source/target format being reasonably
  representable as this framework's own JSON Schema shape** — an
  adapter's transform logic is ordinary application code against
  `IInterchangeFormatAdapter`'s interface, not a new schema-registry
  mechanism; no change to `ADR-020`'s schema versioning or `ADR-018`'s
  upcast chain is needed for this to work.

Consequences:
- `docs/extensibility-points.md` gains the `IInterchangeFormatAdapter`
  row.
- ~~`06-solution-structure.md` gains a new project concept for the
  MLLP-listener component specifically (a background TCP listener,
  distinct from every other component in this design, which are all
  HTTP/GraphQL) — not yet detailed, flagged as remaining propagation
  work.~~ **Done — found stale by a design-compliance audit this
  session**: `docs/06-solution-structure.md` (lines 108-117) documents
  `EventStore.Interchange`/`EventStore.Interchange.Abstractions` and the
  MLLP listener in detail; `src/EventStore.Interchange/
  Hl7V2MllpListener.cs` is real.
- **MLLP's lack of inherent security is a real, named operational
  requirement, not glossed over**: an `Hl7V2Adapter` deployment must
  provide its own transport security (TLS termination, or network-level
  isolation) — this framework doesn't add security MLLP itself doesn't
  have.
- No change to `ADR-023`'s content-level persist-everything posture, and
  no change to `ADR-060`'s webhook delivery/signing mechanics — this ADR
  adds a transform step around both, not a new posture.

**Addendum, 2026-09-03 — a fifth concrete adapter, `VCardAdapter`,
designed against this same seam (no new ADR needed, per this ADR's own
"..." in the adapter list above):** digital-identity-kyc's own
Contact/Profile entity (RFC 6350's `FN`/`N`/`EMAIL`/`TEL`/`ADR`/`ORG`
subset, wire format RFC 7095 jCard) needed vCard import/export — see
[`../domains/digital-identity-kyc/features/contact-profile-and-vcard-
interchange.md`](../domains/digital-identity-kyc/features/contact-profile-and-vcard-interchange.md).
Notable: this is the **first genuinely bidirectional** adapter
(`Hl7V2Adapter`/`FhirAdapter` are inbound-only, `IchE2bR3Adapter`/
`Gs1EpcisAdapter` outbound-only) — confirms the interface's own
per-direction `NotSupportedException` design was already general enough
to support one without a shape change. CardDAV (RFC 6352) was
considered as a transport surface and declined for the same reason
`ADR-032` declined WebDAV entirely (plain HTTP already serves both
directions this adapter needs) — not a new decision, the same one
applied to a narrower, WebDAV-extension protocol.
