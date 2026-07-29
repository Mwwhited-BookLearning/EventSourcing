# Conversation Context — For Continuation in Claude Code

This file exists so work on this design can continue in another tool (Claude Code)
without losing the reasoning trail that produced the numbered documents in this
folder. It is a narrative summary, not a spec — the numbered documents (`01`–`14`) are
the actual design; this file explains *how they got that way* and what's still loosely
decided vs. firmly decided.

## Author / Environment Preferences

The person working on this design generally works in: **C#, SQLite, SQL Server, WCF,
.NET Core, .NET Framework 4.8, Markdown, PlantUML diagrams, C4 architecture patterns,
BDD, and UI diagrams expressed in PlantUML+Salt.** Any code samples, tooling
suggestions, or diagram formats generated for this project should default to this
stack unless the person says otherwise. Diagrams throughout this document set are
PlantUML (C4, sequence, state, class) and Salt (UI wireframes) specifically because of
this preference.

## How the Design Evolved (Chronological Narrative)

1. **Starting point** — a question about which HTTP status code fits "message accepted
   but not yet validated." Landed on `202 Accepted` with a status envelope, rather than
   `200`/`400`/`422`, because those imply an outcome is already known.

2. **Reliable capture requirement emerged** — the person wants *all* posted events
   persisted even if not validated as correctly formatted, with a separate response
   channel for feedback. This became the seed of the whole "persist first, validate/
   route/authorize as separate non-blocking steps" philosophy that now runs through
   every document (stated explicitly in `01` §1.2).

3. **ID design** — debated client- vs. server-generated IDs; landed on client-generated
   **correlation IDs** (for idempotency/tracking before a domain identity exists) plus
   server-assigned **entity IDs** (`{appId}:{entityType}:{uniqueId}`) once routing
   resolves an entity's type. This split appears throughout `01`, `04`, `05`.

4. **Status vocabulary** — introduced `accepted|invalid|processing` style status in the
   response envelope; later refined into `received → applied/rejected` once CQRS +
   event sourcing was introduced, because "accepted" was ambiguous between "into the
   inbox" and "into the domain." See `04`.

5. **CQRS + Event Sourcing + Event Streams** — the person clarified the inbound flow is
   really just a **transport handoff** from client outbox to server inbox — no routing
   or domain logic happens at that point. This produced the inbox/outbox pattern
   description now central to `04`.

6. **Bidirectional pipeline** — added an outbound direction: server → client events for
   (a) responses to submitted messages and (b) subscription/watch updates on entities
   the client cares about. Two different routing keys (`correlationId` vs. `entityId`)
   — now `04` §4.2.

7. **Partial updates + null vs. unspecified** — the person wanted patches that only
   include changed properties, and needed to distinguish "property not sent" from
   "property explicitly set to null." Landed on an `Optional<T>` wrapper type over
   alternatives (field-mask, JSON Patch) because the system is strongly-typed C#
   throughout. See `06`.

8. **Soft schema registry + patch-chain event store** — the person wants a schema
   registry of known entity types/versions, with the event store being nothing but a
   chain of patches and the entity store being the latest full materialized state from
   replaying that chain. This produced the versioned/hashed Schema Registry and the
   "entity store is just a rebuildable projection" framing — `05`, `07`.

9. **Ordering/concurrency realization** — the person correctly identified that two
   patches based on the same prior version, from different clients, touching the same
   field, have **no true causal order** — any resolution is a policy choice. Landed on
   stream-arrival-order LWW as the default, with non-blocking conflict *detection* and
   flagging layered on top (not full CRDTs, reserved for specific contentious fields
   only). See `08`.

10. **Readability/size digressions** — brief side discussions on XML vs. JSON vs. YAML
    readability, and when XML-with-attributes can rival JSON for size (mainly:
    many short repeated attributes, or after gzip compression). Concluded JSON remains
    the right choice for this platform's event/patch payloads given strong typing
    needs and .NET tooling — not written into the numbered docs as its own section,
    but informs why JSON is assumed as the default serialization throughout.

11. **MVVM client architecture** — the person wants a WPF-like MVVM client with command
    binding, clean separation of View/Style/ViewModel/Model, where ViewModels dispatch
    through a mediator into the outbox rather than mutating local state directly. Also
    surfaced sharding needs. See `03` and `09`.

12. **Sharding → actually replication** — the person described shards as replicas that
    may originate from different locations and become eventually consistent — this is
    **replication**, not sharding, and the two were explicitly distinguished. Both are
    still wanted (shard for partition, replicate for geo-distribution/availability).
    See `09`.

13. **GraphQL vs. OData for the query layer** — evaluated both; recommended GraphQL as
    primary because Query/Mutation/Subscription unify with the platform's existing
    three pipelines, and because GraphQL is the clearly better fit for **hierarchical**
    queries (explicitly re-confirmed later in the conversation — see point 20). OData
    retained as a secondary option for enterprise tooling consumers only. See `10`.

14. **Change history query** — the person proposed letting clients query all events for
    a given entity as one way to surface/inspect ordering conflicts. This became a
    first-class capability distinct from both the entity store (current state) and the
    subscription pipeline (push) — see `08` §8.4.

15. **Entity view definitions** — the person wants entities to carry their own
    HTML+JS view definitions, rendered via an embedded web engine (WebView2/WKWebView/
    CEF) even from a native app, to keep rendering simple and unified across platforms.
    This produced the View Definition Registry and the native/JS bridge design. See
    `03` §3.2, `05` §5.5.

16. **Backward/forward schema maps** — the person wants explicit upcast (old→current,
    for replay) and downcast (current→old, for serving legacy consumers) mappings.
    Discussed GraphQL directives as a way to keep schema and migration metadata from
    drifting apart; noted OData is weaker here. See `07` §7.3–7.4.

17. **Content-negotiation question, and "is there a standard for this?"** — concluded
    content-type-based dispatch between GraphQL/OData isn't real content negotiation
    and isn't recommended (distinct paths instead); concluded **no RFC/W3C standard**
    covers upcast/downcast schema mapping specifically — closest prior art is Avro
    schema resolution, Confluent compatibility modes, Protobuf field evolution, and
    JSON-LD `@context` (for renames only), none of which fully cover the need. See `07`
    §7.3.4, `10` §10.4.

18. **Decision to just use raw JS functions** for schema map transforms, rather than a
    bespoke declarative DSL — reasoned that the platform already needs a JS runtime for
    view definitions (point 15), so reusing one sandboxed engine for both is simpler
    than inventing a separate mapping language. Introduced hard determinism and
    sandboxing constraints given the replay requirement (Jint recommended over
    ClearScript for manageability). See `07` §7.3.2.

19. **Method whitelisting question** — the person wanted a way to allow specific JS
    capabilities (e.g. a deterministic `now()`) while denying others (e.g.
    `Math.random()`). Landed on two options: **CEL** (Common Expression Language) as a
    standards-based, whitelist-by-construction expression language for the common
    case, or manual global-object stripping + curated function injection if staying
    with raw Jint. Recommended a two-tier split: CEL for common/declarative transforms,
    sandboxed Jint reserved for rare complex cases. See `07` §7.3.2–7.3.3.

20. **API compatibility requirements stated explicitly** — the person wants
    best-effort matching so extra/missing properties never cause client errors, and the
    system should always accept changes (with upcasting) even under mixed client/server
    versions. This produced the **Tolerant Reader** framing tying together schema
    advisory-ness, the `extensions` field, and additive-only evolution as one named
    principle. See `11`.

21. **No forced client upgrade mid-capture; rollback without DB restore/redeploy** —
    the person wants rolling deploys and rollbacks to never disrupt an in-progress
    client capture and never require a database restore. Produced the
    Expand/Contract migration pattern, N-1/N+1 compatibility window, and the
    "persist-before-route" reframing as a rollback-safety mechanism (not just an
    ingestion-reliability one). See `11` §11.5–11.9.

22. **Even schema validation should be a warning, not a gate** — the person sharpened
    the stance further: a node being behind on schema knowledge (or a client emitting
    events before its schema is even published to the registry) must never punish other
    clients/servers. This reframed the Schema Registry itself as **discovered, not
    authorized** — eventually-consistent, replicated data like anything else, not
    special synchronous infrastructure. Raised an open question about whether
    unresolved-schema history should be reconciled retroactively once new schema info
    arrives (leaning yes, via a background reconciler, but not finalized). See `07`
    §7.2, `14`.

23. **Server-to-server sync** — the person wants servers themselves to sync as
    replicas, with no guarantee of matching state at any given time. Recognized this
    reuses the exact same outbox/inbox primitive already designed for client↔server,
    just peer-to-peer. Discussed topology options (gossip recommended), Merkle-tree
    catch-up for reconnecting after a gap, and confirmed that cross-server divergence
    is resolved by the *same* conflict-flag mechanism as same-server concurrent writes
    (no new resolution logic needed). See `09` §9.4.

24. **Non-authoritative capture** — the person wants the system to accept events even
    from users whose permissions can't be proven at capture time (self-attestation),
    with later accept/reject review, and without ever deleting the original data. This
    introduced `AttestedActorId`/`AttestedClaims`/`AuthorityStatus` as a trust axis
    distinct from schema status, with accept/reject modeled as new events (never
    mutations), and an open per-entity-type decision between annotation-only vs.
    compensating-patch rejection behavior. See `12`.

25. **DID + UCAN + OAuth token exchange** — the person specifically wants to use W3C
    DID and UCAN for self-attested credentials, exchanged via OAuth/OIDC (RFC 8693
    token exchange) for ordinary bearer JWTs, so that no downstream endpoint needs to
    understand anything beyond "bearer token." This mapped UCAN's offline-verifiable
    delegation chains directly onto the platform's "capture now, adjudicate later"
    need, and clarified the exchange should happen **server-side at ingestion** (not
    client-side pre-exchange) given the offline-capture requirement. See `12` §12.5.

26. **This step: expansion into a multi-document set** — the single monolithic draft
    was split into the numbered documents in this folder, organized for independent
    readability with cross-references, plus this context file, packaged as a zip.

## What Is Still Genuinely Undecided

See `14-open-questions.md` for the full list — do not treat anything there as decided
just because it's discussed at length in a numbered document. In particular: final
GraphQL vs. OData call, sharding strategy (hash vs. entity-type), peer sync topology,
and per-entity-type rejection behavior are all still open.

## Suggested Next Steps (if continuing in Claude Code)

- Stand up a minimal .NET solution skeleton reflecting `05`'s table shapes (SQLite for
  local/dev, SQL Server for a shared environment, per the person's usual stack).
  BDD test project structure, given the person's stated preference, to exercise the
  scenarios in `13`.
- Prototype the `Optional<T>` JSON converter (`06`) as an early, low-risk starting
  point — it's foundational to everything else and easy to unit test in isolation.
- Prototype a minimal Jint-sandboxed schema-map transform runner (`07` §7.3.2) with the
  determinism/timeout constraints enforced, before building the full projector.
- Treat each numbered document as a candidate for its own set of implementation tasks/
  ADRs as the project moves from design into build.
