[← ADR index](../07-adrs.md)

# ADR-013: Canonical error responses via RFC 9457 Problem Details

Status: Accepted

Context: Error responses were described inconsistently across the design
— different feature docs implied different response bodies for `400`/
`401`/`403`/`404`/`409` without ever settling on one shape. Left alone,
every endpoint would plausibly grow its own ad hoc error format.

Decision: Every error response across every endpoint uses **RFC 9457
Problem Details** (`application/problem+json`), via ASP.NET Core's
built-in support (`builder.Services.AddProblemDetails()`,
`Results.Problem(...)`/`Results.ValidationProblem(...)` in minimal APIs) —
no custom error DTO, no library beyond what's already in the framework.
Standard members: `type` (a URI identifying the problem category — see
below), `title` (short, stable summary), `status`, `detail`
(occurrence-specific human-readable text), `instance` (the request path).
Anything beyond that is carried in Problem Details' standard
`Extensions` dictionary, not by inventing new top-level fields:

| Situation | `status` | `type` slug | Extensions |
|---|---|---|---|
| Payload fails schema validation | `400` | `validation-failed` | Uses `ValidationProblemDetails`'s `errors: { "<path>": ["<message>"] }`, not a custom shape — this is the one case with an existing framework type built for exactly this |
| Strict-mode parent event(s) not found | `400` | `parent-not-found` | `missingParentEventIds: [...]` |
| `$filter` references an undeclared field | `400` | `filter-field-not-filterable` | `field: "InternalNotes"` |
| `fromSequenceNumber` supplied with `mode=tail` | `400` | `invalid-replay-parameters` | — |
| Missing/invalid Bearer token | `401` | `unauthenticated` | — |
| Missing/invalid DPoP proof, or proof doesn't match the token's `cnf.jkt` (`ADR-017`) | `401` | `dpop-proof-invalid` | `reason: "..."` |
| `schemaVersion` on publish names a version that doesn't exist (`ADR-020`) | `400` | `unknown-schema-version` | — |
| Missing scope, or missing `RequiredPublishClaim`/`RequiredReadClaim` | `403` | `forbidden` | `reason: "missing_scope"` \| `"missing_required_claim"` — this is exactly the "response detail, not the status code" distinction `ADR-008` already promised |
| Unknown event-type / unknown `eventId` | `404` | `not-found` | — |
| `eventId` reused with different content | `409` | `event-id-conflict` | `eventId: "..."` |
| `x-masking` malformed at registration (`ADR-009`) | `400` | `masking-invalid` | `path: "<property path>"`, `reason: "..."` |
| `changeKind` missing or not `Full`/`Partial` at registration (`ADR-016`) | `400` | `change-kind-required` | — |

`type` values are placeholder slugs here (`https://eventstore.example/problems/<slug>`
in the examples below) — RFC 9457 wants `type` to ideally resolve to human
documentation, but doesn't require it; picking a real base URL (or
defaulting every `type` to `about:blank`, ASP.NET Core's built-in fallback)
is an implementation-time decision this design doesn't need to make.

```json
{
  "type": "https://eventstore.example/problems/parent-not-found",
  "title": "One or more parent events do not exist",
  "status": 400,
  "detail": "parentEventIds referenced an event that has not been published.",
  "instance": "/publish/OrderShipped",
  "missingParentEventIds": ["00000000-0000-0000-0000-000000000000"]
}
```

Consequences:
- Every response consumers need to parse has one shape, not N ad hoc ones
  — a caller can always check `status` + `type` first and fall back to
  `detail` for a human, without needing per-endpoint response schemas.
- `403`'s `reason` extension is the only place the scope-vs-claim
  distinction from `ADR-008` actually surfaces; the status code alone
  still can't be used to tell them apart, by design.
- OpenAPI/AsyncAPI generation documents every non-`2xx` response as
  `$ref: '#/components/schemas/ProblemDetails'` (a single shared schema)
  plus a `type`-specific `example`, rather than a bespoke schema per
  status code per endpoint.
- This doesn't apply to Lineage's `restricted: true` stubs (`ADR-008`) —
  those are `200` responses with a marked node, not an error at all; there
  is no HTTP error status for "some of what you asked for is hidden."
