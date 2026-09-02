# TODO

A live tracker for **concrete, already-decided work that just hasn't
been done yet** — distinct from both other live trackers in this repo:

- [`docs/10-open-questions.md`](docs/10-open-questions.md) is for a
  design fork **not yet decided** — the question itself is still open.
- **This file** is for a task where the decision is already made (a doc
  needs rewriting, a diagram needs drawing, a terminology collision
  needs resolving) and only the doing is left.
- [`docs/changes/{date}.md`](docs/changes) is the narrative history of
  work **already completed** — where an item here goes once it's done.

**Full workflow (adding/completing items, batching large ones) is in
[`.claude/protocols/todo-tracking.md`](.claude/protocols/todo-tracking.md)
— read it before touching this file.** Short version: add an item the
same pass you find one; when it's done, delete the item here and add a
line to today's `docs/changes/{date}.md` instead.

**This is the authoritative list of active work** — per the same
reasoning `docs/10-open-questions.md` already applies to itself, do not
restate this list's contents elsewhere in the repo (including in
`CLAUDE.md`); a duplicated copy just drifts stale. `CLAUDE.md` points
here instead of inlining.

Every item previously tracked here (Naive UI/Vue Router shell,
`style-guide.md`, playbook diagrams/restructure/new playbooks/READMEs,
paged entity-list data grids, configurable-presentation-type charting,
JSON Schema field/dependent-field validation, calculated fields, the
PlantUML `.puml`/Docker-render migration) is done, per the workflow
above: deleted from this file, full narrative in
[`docs/changes/2026-08-28.md`](docs/changes/2026-08-28.md) and
[`docs/changes/2026-08-29.md`](docs/changes/2026-08-29.md).

(The "DSL for user flows/validations/approvals" ask was moved to
[`docs/10-open-questions.md`](docs/10-open-questions.md) row 1, not kept
here — a genuinely undecided fork, not decided work with only the doing
left.)

- [ ] **A generic demo identity still can't publish a real Vitals/Meridian
  business event over HTTP** — the narrower, still-genuinely-open half of
  the "Dispatch a command" demo-panel gap (`docs/changes/2026-09-02.md`
  closed the OTHER half, the field-casing gap, for real). No DevIdp-seeded
  HTTP client anywhere holds the specific `RequiredClaims` any real
  business event type demands (e.g. `PatientScreened`'s `patient:enroll`,
  `VitalsWorkflowA.cs`) — those events have only ever been created
  in-process by `Samples.Vitals.Seed`/`Simulator` calling `PublishService`
  directly, bypassing the HTTP auth layer's claim check entirely. This is
  a real security-policy decision, not a technical gap: either (a) grant a
  narrow, explicitly-labeled "demo:dispatch"-style claim per domain to a
  shared demo identity (weakens this project's own "one identity per real
  capability need" convention, `DevIdpSeeder.cs`), or (b) retire the
  generic cross-domain panel in favor of a per-domain demo action that
  already speaks the right claim (matching how Vitals/Meridian's own Queue
  screens already work) — deliberately left undecided here rather than
  picked unilaterally. In the meantime the *symptom* is fixed: a rejection
  now marks the outbox entry `Failed` (terminal, visible in the UI) instead
  of retrying forever silently, with no signal anything is wrong
  (`useOutboxStore.flush`'s new `permanentFailure` handling,
  `docs/changes/2026-09-02.md`).

- [ ] **Decide the authorization model**, now that the comparison doc is
  written: [`docs/comparisons/authorization-model.md`](docs/comparisons/authorization-model.md)
  surveys RBAC-extended, ABAC/policy-based (XACML/NGAC), ReBAC/
  relationship-tuple (Zanzibar-style), a named Hybrid, DACL, and
  Classification-based (Mandatory Access Control), each worked through
  one shared scenario (a Vitals PI resolving one assigned
  `AdverseEventReported` task) with a concrete schema+pseudocode sketch,
  per direct request, so a later real spike can start from this analysis
  instead of redoing it. Its Recommendation leans Hybrid (RBAC for
  coarse entity-type × access-level grants, a Zanzibar-shaped tuple table
  for per-task grants) but leaves the actual pick to the user. Once
  decided: write the deciding ADR (cites the comparison, doesn't
  re-derive it), add a row to `docs/comparisons/README.md`'s catalog, and
  only then close this item.

- [ ] **Decide scope for full OIDC/OAuth2 identity-provider support —
  now including an application-owned local authorization STS layer.**
  Today `EventStore.DevIdp` only implements `client_credentials` +
  RFC 8693 Token Exchange (`src/EventStore.DevIdp/Program.cs:133,143,
  279-280`) — no `authorization_code`/PKCE, no ID tokens ever issued
  (`Program.cs:412-414`), no interactive login, no userinfo endpoint. It
  exposes `/.well-known/openid-configuration` for discovery only
  (`ADR-006`). Per direct request, also design the split named and
  worked through in `docs/comparisons/authorization-model.md`'s
  "Application-owned local authorization STS" section: a central identity
  layer issues only generalized, cross-application roles; each deployed
  application runs its own local RFC 8693 token-exchange step mapping
  that generalized role into its own entity-type/access-level/per-task
  claims (real precedents verified: Okta's per-API custom authorization
  servers, Kubernetes `ClusterRole`+per-namespace `RoleBinding`). A
  completeness pass also found real standards for two adjacent gaps this
  same design direction depends on, both cited in the comparison doc's
  STS section now: RFC 7591/7592 (OAuth Dynamic Client Registration) for
  letting an application self-register instead of `DevIdpSeeder.cs`'s
  current hardcoded C# client list, and RFC 8414 (OAuth Authorization
  Server Metadata) as the OAuth-only counterpart to the OIDC Discovery
  doc already used. Decide what "full OIDC+OAuth2" and this STS split
  mean for this framework and record the decision in `docs/references.md`
  plus a queued ADR.

- [ ] **Evaluate adopting OpenID Federation as the multi-IdP trust
  pattern.** OpenID Federation 1.0 is now a Final spec (OpenID
  Foundation, approved 2026-02-17), split into a 1.1 core+profiles set
  approved 2026-05-06 — signed JWT Entity Statements chained to a Trust
  Anchor. This repo's existing "federation" mechanisms are all single-
  hop/pairwise, not trust-chain/hierarchy: `ADR-047` (one
  `TrustedFederationIssuer` per `AppId`, JWKS-verified), `ADR-082`
  (tenant-to-tenant data mapping), `ADR-044` (UCAN `AppTrustRoot`).
  Neither OpenID Federation itself nor a rejection of it has ever been
  evaluated here — decide and record adopted-or-rejected in
  `docs/references.md` either way, per this repo's own convention that a
  rejection needs to be as explicit as an adoption.

- [ ] **Decide where crontab-format and RFC 5545 (iCalendar) scheduling
  each apply, and record it.** No scheduler of any kind exists in this
  repo today — `ADR-069` explicitly declines to build one (`docs/adrs/
  adr-069-pluggable-outbox-flush-triggers.md:40`, "this framework doesn't
  build a scheduler, it only needs `Flush` to be safely callable by
  one"). Crontab format is POSIX.1 base + de facto Vixie-cron extensions
  (no RFC — don't cite one). RFC 5545 defines `VEVENT`/`VTODO`/
  `VJOURNAL`/`VFREEBUSY`/`VTIMEZONE`/`VALARM` plus the `RRULE` recurrence
  type. The two are genuinely complementary, not redundant: crontab
  fits a bare periodic engine tick with no calendar semantics (e.g. a
  re-screening interval); RFC 5545 `VEVENT`/`VTODO`+`RRULE` fits a
  person/appointment-facing domain object needing time, timezone,
  duration, and attendees as first-class data. **RFC 5545 alone is only
  the data format, not the scheduling protocol** — a completeness pass
  found the layer actually missing: RFC 5546 (iTIP) defines the
  REQUEST/REPLY/CANCEL/COUNTER methods for one party to propose/accept/
  decline a calendar object to another; RFC 4791 (CalDAV) is the HTTP
  transport for it; RFC 6638 (CalDAV Scheduling Extensions) is what
  actually binds iTIP's methods to CalDAV's HTTP transport specifically
  (vs. RFC 6047/iMIP's email transport, confirmed low relevance here) —
  without RFC 6638, adopting iTIP+CalDAV alone doesn't actually
  interoperate. RFC 7265 (jCal) gives a JSON form of the RFC 5545 data,
  likely a better fit than raw ICS text for this JSON-first framework.
  Decide which capability needs which (crontab vs. the RFC 5545/5546/
  4791/6638/7265 stack, or both for different purposes) and record in
  `docs/references.md`, likely with a new pattern doc/ADR.

- [ ] **Design a Contact/Profile entity before building vCard (RFC 6350)
  import/export.** No `Contact`/`Profile`-shaped entity exists anywhere
  in `src/` today (searched digital-identity-kyc domain and broader
  `src/` for `FirstName`/`GivenName`/`EmailAddress`/`PhoneNumber`/
  `Contact`/`Profile` — none found). vCard export needs something to
  export *from*; decide that entity's shape (likely in the digital-
  identity-kyc domain) before wiring RFC 6350 import/export on top of it.
  Same data-vs-protocol gap as the scheduling item above: RFC 6350 is
  data-only — RFC 6352 (CardDAV) is the HTTP access protocol (address-book
  collections, content negotiation between vCard 3.0/4.0), and RFC 7095
  (jCard) gives a JSON form, again likely a better fit here than raw
  vCard text.

- [ ] **Add "vehicle/equipment maintenance & fuel logs" as a candidate
  domain.** Real standards to ground it in: VMRS (ATA/TMC's hierarchical
  maintenance/repair coding system — industry-standard, no formal SDO)
  and ISO 15143-3/AEMP 2.0 (JSON/XML telematics data exchange covering
  position/hours/fuel/machine status — check whether this can reuse the
  existing `ADR-031` streaming/telemetry channel mechanism rather than
  needing a new one). ISO 14224's reliability/maintenance data categories
  are oil-and-gas-specific but may transfer conceptually. SAE J1939
  (verified, real) is the underlying heavy-vehicle CAN-bus wire protocol
  telematics data ultimately traces back to (SPNs like Fuel Level 1,
  Engine Speed) — the wrong layer for a fuel-log *event schema* itself,
  worth at most a one-line "traces back to" mention, not a driving
  citation; VMRS/ISO 15143-3 remain the right fit for the schema layer.
  Add at minimum
  a "considered" domain doc (`docs/domains/vehicle-equipment-maintenance/
  README.md`, one feature doc, per the existing 13-considered/2-chosen
  structure) and a row in `docs/domains/README.md`'s catalog and
  `docs/comparisons/proving-ground-domain.md`'s coverage matrix.
