[← ADR index](../07-adrs.md)

# ADR-030: Multi-tenant framework — `appId`-scoped schemas, domain-agnostic core

Status: Accepted — ~~one running deployment shared across
customers~~ narrowed by `ADR-075` to one running deployment per
customer; `AppId` remains exactly as decided below for scoping multiple
*applications within one tenant's own dedicated deployment*.

Context: `EntityId`'s `{appId}:{entityType}:{uniqueId}` shape (`ADR-021`)
already implied something this design never stated explicitly: this is
meant to be a **framework** — a reusable engine that any number of
independent applications register their own event types/entities into —
not a single-purpose store for one domain (`Orders`, the worked example
running through `09-cqrs-read-models.md`, `features/cqrs-projections.md`,
and elsewhere). Left implicit, "the worked example" and "the framework"
risk blurring together — a reader could reasonably come away thinking
`Orders`-specific concepts are baked into the engine rather than being
just the first thing registered into it.

~~`AppId` is the boundary between different customers sharing one
deployment.~~ **Superseded by `ADR-075`**: different *customers* now get
different, fully separate deployments (the silo model) — `AppId`'s real,
unaffected job is scoping different *applications within one customer's
own deployment*, per the Decision below, which otherwise still holds
exactly as written.

Decision:
- **`appId` becomes a real, first-class scoping key, not just a prefix
  convention inside `EntityId`.** `EventTypeDefinition`'s key becomes
  `(AppId, Name, Version)`, not just `(Name, Version)` — two independent
  applications can register a type named `OrderPlaced` with completely
  different shapes, claims, `ChangeKind`, and masking rules, with zero
  collision, because they're different rows entirely. Every existing
  per-type field (`ParentValidationMode`, `RequiredPublishClaim`/
  `RequiredReadClaim`, `ChangeKind`, `EntityIdField`,
  `UpcastFromPrevious`/`DowncastToPrevious`) is unaffected in *meaning* —
  each is still exactly one thing per registered type — only the key
  they're looked up by gains `AppId`.
- **The core engine contains zero domain-specific knowledge, as a hard
  rule, not a loose aspiration.** No event type name, field name, or
  business rule is ever hardcoded in an `EventStore.*` project. Every
  domain-specific thing — schemas, claims, masking rules, upcast/downcast
  maps, view definitions (`ADR-039`) — is *data*, registered at
  runtime through the same registry mechanisms every other ADR in this
  design already treats that way (`ADR-002`'s "schemas registered live"
  reasoning generalizes directly to "applications registered live," not
  just versions).
- **The `Orders` walkthrough is a sample application, not part of the
  framework, and the solution structure says so.** `Samples.Orders.*`
  projects (`06-solution-structure.md`) consume the framework exactly the
  way any other application would — through the ordinary
  `PUT /registry/{event-type}` and publish/follow/query surface — with no
  special access the framework doesn't also grant a genuinely separate
  application. If a project ever needs a second worked example, it's
  another `Samples.<Name>.*` tree, not a special case in `EventStore.*`.
- **Resolved: `registry:admin` and the other operation-level scopes
  (`ADR-006`) gain optional `AppId`-scoped variants — `registry:admin:
  {appId}` alongside the existing unscoped `registry:admin`.** The
  unscoped form remains valid and means what it always has —
  framework-operator/super-admin, works across every `appId` — so
  nothing already issued or seeded breaks. A caller holding only the
  scoped form can register/administer schemas for that one `AppId`
  only; App A's operator genuinely cannot touch App B's schemas with
  it. This is the same "global admin vs. tenant admin" shape almost
  every real multi-tenant SaaS admin model already uses (Azure AD's
  Global Administrator vs. per-tenant roles, GitHub's site admin vs.
  org admin) — not a bespoke scheme, a well-precedented default. Scope
  checks (`ADR-006`'s policy-based authorization) check for *either* the
  unscoped or the correctly-`AppId`-scoped form, never requiring both.

Consequences:
- `GraphQL` (the queued OData-replacing query layer) **must compose its
  schema per `appId`**, not serve one fixed, global SDL — each
  application effectively gets its own GraphQL type graph, generated from
  that application's own registered types, the same way `ADR-002`'s
  OpenAPI/AsyncAPI generation already works per the full registry today
  (which itself now needs to filter by `appId` once this lands). This is
  a real requirement on that ADR's design, not an afterthought to retrofit
  later.
- Every place `EventTypeDefinition` is looked up by `(Name, Version)`
  alone across the existing design (`05-schema-registry-and-spec-
  generation.md`, `06-solution-structure.md`'s DI wiring,
  `ADR-018`/`ADR-020`/`ADR-027`/`ADR-028`'s upcast/downcast/materialization
  logic) needs `AppId` added to that lookup — a real, mechanical
  propagation cost across several already-written pieces, not a
  conceptual one.
- Multi-tenancy at the **event log** level was already free: `StoredEvent`
  has always carried `EventType` as a plain string and now `EntityId`
  with `appId` baked directly into it (`ADR-021`) — no schema change
  needed there. The cost of this ADR is entirely in the **registry** and
  **generation** layers, not the write path.
- This doesn't change anything about `ADR-023`'s persist-everything
  posture, `ADR-024`'s conflict handling, or `ADR-029`'s logical-order
  fold — all of those already operate per-`EntityId`, and `EntityId`
  already disambiguates applications. Multi-tenancy was closer to already
  built than not; this ADR is what makes it a stated, intentional property
  instead of an accidental side effect of `ADR-021`'s ID format.

**Compliance note** (a proving-ground compliance review, this session):
`docs/comparisons/proving-ground-domain.md` already names SOC 2's Trust
Services Criteria and ISO/IEC 27001 as baseline expectations for any
multi-tenant SaaS deployment of this framework, not unique to one
domain — `AppId`-scoped tenant isolation (registry keys, scopes, and
`ADR-061`'s residency enforcement built on top of it) is the concrete
mechanism satisfying both frameworks' logical-segregation-of-customer-
data control, not an incidental side effect of it.
