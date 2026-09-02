[← Comparisons index](README.md)

# How should Duplex model fine-grained authorization: RBAC, ABAC, ReBAC, DACL, or classification-based?

**Raised by:** direct request, building on `TODO.md`'s open "generic demo
identity can't publish a real business event" gap. The ask: map app roles
to domain permissions; an **access level** (`Read`/`ReadMasked`/`Write`)
crossed with **entity type**; a grant scoped to one specific **user
task**, not a whole entity type; a **config-driven** backend (no redeploy
to add/change a grant); and **application scope** as a first-class
concept so multiple applications can share one platform deployment,
including deliberate cross-application data sharing. Six named patterns
were requested for a side-by-side survey before spiking any of them for
real: RBAC-extended, ABAC/policy-based, ReBAC/relationship-tuple, a named
Hybrid, DACL, and Classification-based (Mandatory Access Control).

**This is a doc-only spike, per direct request** — no throwaway code.
Each option below is worked through the same shared scenario in enough
concrete detail (schema shape + decision pseudocode) that promoting any
one of them to a real working spike later can start from this analysis
rather than redoing it.

## Shared scenario, used identically in every option below

A Vitals clinical trial (`docs/domains/clinical-trials-device-telemetry/`)
running Workflow B (adverse-event reporting):

- **Entity types**: `AdverseEventReported` (the clinical record — some
  fields PHI) and `authorityDecision` (a PI's sign-off, `ADR-094`'s
  reserved response type).
- **Actors**: Dr. Alvarez, a Principal Investigator assigned to Trial
  `T1`/Site `S1` only; a cross-site Safety Monitor who needs visibility
  into every site's adverse events but never the patient-identifying
  fields; a Sponsor auditor who needs the same masked view, org-wide,
  read-only, forever.
- **The three asks made concrete**:
  1. **Access level × entity type**: Dr. Alvarez needs `Write` on
     `authorityDecision` and `Read` (full) on `AdverseEventReported`; the
     Safety Monitor and Sponsor auditor need `ReadMasked` on
     `AdverseEventReported` and no access to `authorityDecision` at all.
  2. **Per-user-task grant**: Dr. Alvarez may resolve *the specific AE
     task assigned to her* — not every `AdverseEventReported` in Trial
     `T1`, and never another PI's task, even at her own site.
  3. **Config-driven, no redeploy**: adding Dr. Alvarez, reassigning her
     to a new site, or onboarding a new Safety Monitor must be an API
     call against a running deployment, not a code change.

## What's already built and config-driven today — reuse before inventing

Every option below should be judged against what already exists, not
against a blank slate:

- **`ADR-046`/`047`/`067`** — `Role`/`RoleAssignment`/`UserPermission`
  are `EventStore.DevIdp`-owned EF tables; grants/revokes go through
  `EventStore.Rbac`'s scope-gated Minimal API (`src/EventStore.Rbac/
  RbacEndpoints.cs`), which publishes real, hash-chained
  `RoleGranted`/`RoleRevoked`/`PermissionGranted` events through
  `PublishService` — DevIdp's own `RbacProjectionWorker` follows that
  stream and folds it back into its local tables. **This is already the
  config-driven, no-redeploy, auditable mechanism the new design needs a
  precedent for** — whichever option is chosen should extend this
  pipeline, not build a second one alongside it.
- **`ADR-008`/`050`** — `RequiredClaims` on `EventTypeDefinition`,
  OR-matched, entity-**type**-wide. No per-instance concept exists here.
- **`ADR-009`/`057`** — property-level `x-masking`, a real three-state
  mechanism (full value / masked value / erased), but authored per
  *field* at schema-registration time, gated by the same `"type:value"`
  claim primitive as `RequiredClaims`. There is no existing "this whole
  entity type is `ReadMasked` to caller X" toggle — only per-field opt-in.
- **`ADR-043`'s entity-scoped delegation** —
  `RequiredClaimEvaluator.HasClaimForEntity` (`src/EventStore.Domain/
  SchemaRegistry/RequiredClaimEvaluator.cs:38-49`) checks a base claim
  plus an optional companion `"{claim}:entityScope"` claim restricting it
  to one `EntityId`. This is the closest thing today to a per-task grant,
  but it's granted by **UCAN delegation from another authorized user**
  (`ADR-036`/`043`), not stored as a list attached to the resource, and
  there is no way to enumerate "everyone with access to this entity" by
  reading the entity itself.
- **The `clearance:phi` claim** (`src/EventStore.DevIdp/
  DevIdpSeeder.cs:101`, enforced for real via `PayloadMasker.cs:74-75` →
  `RequiredClaimEvaluator.HasClaim`) — despite the name, this is a flat
  boolean claim check, not a leveled clearance-vs-classification
  dominance rule. Worth naming explicitly so "classification-based"
  below isn't mistaken for something already built.
- **`ADR-050`'s `DataClassification` taxonomy**
  (`Microsoft.Extensions.Compliance.Redaction`, `docs/patterns/
  README.md:77`) — a tag-to-`Redactor` **routing** mechanism (which
  redaction strategy applies to a value), not an access-control decision
  model. Also worth distinguishing from true classification-based access
  control, below.
- **`EntityType`** (`EventTypeDefinition.EntityType`,
  `src/EventStore.Domain/SchemaRegistry/EventTypeDefinition.cs:27`) is a
  free string, not a controlled vocabulary — any option keying a matrix
  on it inherits that gap (nothing stops two event types under the same
  `AppId` disagreeing on casing/spelling) unless addressed.
- **`AppId`** (`ADR-030`, narrowed by `ADR-075`'s silo model) already
  scopes multiple applications sharing *one tenant's* deployment — that
  part of "application scope as first-class" already exists. What
  doesn't exist: any record *of* an application (no `Application.cs`,
  just a bare string), and deliberate **cross-`AppId` data sharing**,
  which neither ADR addresses — `ADR-030`'s whole point was zero-collision
  isolation between applications, not sharing.

## The fork

### Option A — RBAC-extended

Bolt the two new dimensions directly onto the existing flat role model:
give `Role.Permissions` structured entries instead of bare strings, and
add a `TaskGrant` table for the per-instance case `ADR-046` never
modeled.

```
Role            { AppId, RoleName, Permissions: [{ EntityType, AccessLevel }] }
RoleAssignment  { ActorId, AppId, RoleName }                          // unchanged
TaskGrant       { ActorId, AppId, EntityType, EntityId, AccessLevel } // new — the per-task escape hatch
```

Decision check: `HasAny(role permissions matching EntityType+AccessLevel)
OR HasAny(TaskGrant matching EntityId+AccessLevel)`. Assigning Dr.
Alvarez her specific AE task means writing one `TaskGrant` row — a
`PermissionGranted`-shaped reserved event, following `ADR-067`'s existing
pattern exactly.

| | |
|---|---|
| **Pros** | Directly extends a mechanism already built, already config-driven, already audited as reserved events; smallest conceptual jump for anyone who already knows `ADR-046`; `TaskGrant` reuses the exact same publish/project pipeline as `RoleGranted`. |
| **Cons** | `TaskGrant` is a bolt-on, not a natural consequence of RBAC's own model — NIST's RBAC standard (ANSI/INCITS 359-2004) has no per-instance concept at all, so this option is really "RBAC plus a hand-rolled ReBAC-shaped table," not RBAC on its own; every new per-task need adds another ad hoc table unless generalized early. |

### Option B — ABAC / policy-based (XACML, NGAC)

Decide access by evaluating attributes of subject, resource, and
environment against declarative policy — [NIST SP 800-162](https://csrc.nist.gov/pubs/sp/800/162/upd2/final)'s
own definition. Two real standards implement this concretely, verified
via [NIST SP 800-178](https://csrc.nist.gov/pubs/sp/800/178/final)'s own
head-to-head comparison of them:

- **XACML** (OASIS) — policies as logical formulas over attributes,
  decided by a Policy Decision Point, enforced by a Policy Enforcement
  Point (the PDP/PEP/PAP/PIP reference architecture).
- **NGAC** (NIST SP 800-178, ANSI/INCITS 499) — the same goal via
  enumerated graph relations among users, objects, and attributes rather
  than logical formulas — structurally closer to a per-task grant, since
  a task is just another graph node.

```
attribute(actor: Dr. Alvarez)   = { role: "PI", assignedTrial: "T1", assignedSite: "S1" }
attribute(resource: AE-task-42) = { entityType: "authorityDecision", trial: "T1", site: "S1", assignedTo: "Dr. Alvarez" }
policy: permit(Write, authorityDecision) IF subject.assignedTo == resource.assignedTo
policy: permit(ReadMasked, AdverseEventReported) IF subject.role IN ["SafetyMonitor","SponsorAuditor"]
```

| | |
|---|---|
| **Pros** | Access level × entity type falls straight out as resource attributes — no special-casing needed; per-task grants are just another attribute comparison (`resource.assignedTo == subject.id`), not a bolt-on table; two real, mature standards to draw the policy shape from rather than inventing one. |
| **Cons** | XACML's own reference architecture (PDP/PEP/PAP/PIP as separate components) is heavier than anything else in this framework's auth stack; policy-authoring becomes its own skill/surface, a new admin concern beyond "grant this role to this actor"; NGAC's graph-relation model is less mainstream/tooled than XACML or ReBAC. |

### Option C — ReBAC / relationship-tuple (Zanzibar-style)

Google's [Zanzibar](https://authzed.com/learn/google-zanzibar) model,
implemented in open systems like OpenFGA, SpiceDB, and Ory Keto (not a
standards-body spec, but a real, widely-implemented de facto one):
every permission fact is a tuple `(object, relation, subject)`.

```
(authorityDecision:AE-task-42, resolver,      user:dr-alvarez)
(AdverseEventReported:*,        masked_viewer, role:safety-monitor)
(AdverseEventReported:*,        masked_viewer, role:sponsor-auditor)
```

Decision check: `Check(object, relation, subject)` walks the tuple graph
(directly, or through a role-as-object indirection for the two masked-
viewer rows above). Access level and entity type are just which relation
name is being checked; a per-task grant is exactly one tuple naming the
specific object.

| | |
|---|---|
| **Pros** | **The single best structural fit for "grant per user task"** of any option here — a task-scoped grant is the tuple system's native unit, not a special case; also cleanly expresses "everyone with access to this entity" as a reverse lookup, which `ADR-043`'s delegation-based approach can't do today. |
| **Cons** | Newest, least precedented pattern in this framework's own stack (no existing ADR leans this way); introduces a genuinely new storage shape (a tuple/relation store) rather than extending `ADR-046`'s existing tables; the "role-as-object" indirection needed for coarse role-style grants (the Safety Monitor rows above) means still needing *some* RBAC-shaped concept underneath for the coarse case — ReBAC alone doesn't replace roles, it complements them. |

### Option D — DACL (discretionary access control list)

A list of `(subject, permission-set)` entries attached directly to one
resource instance, controlled by that resource's **owner** — the
defining, discretionary trait, per [NISTIR 7316](https://csrc.nist.gov/pubs/ir/7316/final)'s
own classification of DAC as a fundamental model distinct from RBAC/MAC.
Real-world shapes verified: [RFC 3744](https://www.rfc-editor.org/rfc/rfc3744)
(WebDAV ACLs on HTTP resources — explicitly excludes property-level
control, role-based security, and global ACLs), [RFC 7530](https://www.rfc-editor.org/rfc/rfc7530)/[RFC 8881](https://www.rfc-editor.org/rfc/rfc8881)
(NFSv4 ACLs, modeled on Windows' ACL shape), and Windows' own
security-descriptor DACL/SDDL. (POSIX.1e draft ACLs are the fourth
obvious reference point but were never actually ratified — IEEE/PASC
withdrew sponsorship in 1998 — so cite the *implementations* Linux/
Solaris/FreeBSD ship, not a standard that doesn't formally exist.)

```
AdverseEventReported:AE-task-42.acl = [
  { subject: dr-alvarez,       permissions: [Read, Write] },
  { subject: role:safety-monitor, permissions: [ReadMasked] },
]
```

| | |
|---|---|
| **Pros** | Maximally simple mental model — "who can touch this one thing" is a list on the thing itself, trivially enumerable, no policy engine or role graph to reason about. |
| **Cons** | **Discretionary is the wrong trust model here** — every real DACL standard checked puts the *resource owner* in control of the list; nothing in this framework has an owner-grants-access concept, every existing grant (`ADR-046` roles, `ADR-043` delegation) is centrally/administratively issued. Using DACL's mechanics without its ownership premise is really just Option A or C wearing a different name. Doesn't generalize to "every PI, org-wide" style rules without one ACL per entity — no `AccessLevel × EntityType`-wide statement is expressible at all. |

### Option E — Classification-based (Mandatory Access Control)

Labels on both subjects (clearance) and objects (classification), with a
centrally fixed **dominance** rule deciding access — never discretionary,
never role-derived. Root citations verified directly: Bell &amp; LaPadula,
*Secure Computer Systems* (MITRE, MTR-2547 and successors, 1973) for
confidentiality ("no read up, no write down"); Biba, *Integrity
Considerations for Secure Computer Systems* (MITRE ESD-TR-76-372/
MTR-3153, 1977) for the integrity dual. [FIPS 188](https://csrc.nist.gov/pubs/fips/188/final)
("Standard Security Label for Information Transfer") is the obvious
citation for a *machine-readable* label format, but it was **withdrawn
by NIST in 2015** — cite it as historical only. SELinux's Type
Enforcement (with optional MLS) is the one real, current, verifiable MAC
implementation worth pointing to.

```
subject.clearance    = { level: "Confidential", compartments: ["TrialT1"] }
object.classification = { level: "Confidential", compartments: ["TrialT1"] }
permit(Read) IF subject.clearance.level >= object.classification.level
              AND object.classification.compartments ⊆ subject.clearance.compartments
```

| | |
|---|---|
| **Pros** | Exactly right shape for a genuinely leveled-sensitivity domain (public/internal/confidential/restricted tiers spanning many entity types uniformly); dominance comparison is simple and provably analyzable, the reason it's the one model with real formal security proofs behind it. |
| **Cons** | **Doesn't fit the actual scenario at all** — Dr. Alvarez vs. the Safety Monitor isn't a clearance-level difference, it's a *role and task-assignment* difference at the *same* sensitivity tier; forcing this scenario into dominance labels would need a compartment per trial per site per task, which is really ReBAC's tuple model wearing MAC's clothes. The repo's own `clearance:phi` claim already shows this trap — it *sounds* like MAC but is actually a flat boolean claim, not a dominance rule, because nothing here has needed real leveled clearance yet. |

### Option F — Hybrid (RBAC + ReBAC)

Named directly rather than assumed after the fact, per the request: keep
`ADR-046`'s role/permission model for coarse, entity-type × access-level
grants (the Safety Monitor's "`ReadMasked` on `AdverseEventReported`,
every site" rule — a role-shaped statement, not a per-instance one), and
add a small, genuinely tuple-shaped `TaskGrant` relation *only* for the
per-instance case, modeled explicitly as a `(object, relation, subject)`
tuple rather than Option A's ad hoc table — so it can grow into a real
relation store later without a rewrite if more per-instance cases show up.

```
Role      { AppId, RoleName, Permissions: [{ EntityType, AccessLevel }] }   // coarse, unchanged shape from A
TaskGrant { EntityType, EntityId, Relation: "resolver", ActorId }           // per-instance, tuple-shaped like C
```

| | |
|---|---|
| **Pros** | Takes the two-line answer from the running scenario directly: coarse role rules cover the Safety Monitor/Sponsor Auditor case cleanly, the tuple-shaped grant covers Dr. Alvarez's one task cleanly — neither is forced to do the other's job. Reuses `ADR-046`'s config-driven pipeline for the coarse half, and only introduces the new tuple shape where a table genuinely doesn't fit. |
| **Cons** | Two mechanisms to reason about instead of one; needs a clear, written rule for which one a new grant belongs to (this doc's own scenario answers it — "does the grant name a specific entity instance, or a whole entity type?" — but that rule needs to actually get written down wherever this is decided, not left implicit). |

## Recommendation

**Option F (Hybrid), if this needs to be picked now — but this is a
comparison for you to choose from, not a decision.** Argued from the
scenario itself: every real requirement in this ask splits cleanly along
one line — "does the grant name a whole entity type, or one specific
instance?" — and that line maps exactly onto RBAC (already built,
already config-driven, already audited) for the first half and a small,
Zanzibar-shaped tuple table for the second. DACL is a poor fit because
nothing in this framework has an owner-grants-access trust model to
begin with; Classification-based is a poor fit because the scenario's
real distinction (role/task assignment) isn't a sensitivity-level
distinction — both are included above because they're real, well-defined
patterns worth knowing, not because they solve this particular ask.
Pure ABAC/XACML-NGAC is the strongest *single-mechanism* alternative to
the Hybrid, since attributes subsume both the coarse and per-task cases
without needing two tables — the trade is accepting a genuinely new
policy-authoring surface this framework doesn't have any precedent for
yet, where the Hybrid's RBAC half needs none.

If you want to see any of these as a real working spike before deciding,
this doc's schema sketches are the starting point for that — Option C
(pure ReBAC) and Option F (Hybrid) are the two most worth spiking for
real, since they're the two genuinely new mechanisms; Options A/D/E are
different enough from the scenario's actual shape that a spike would
mostly just confirm this doc's own analysis.
