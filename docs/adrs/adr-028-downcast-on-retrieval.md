[← ADR index](../07-adrs.md)

# ADR-028: Downcast on retrieval for an explicitly requested older schema version

Status: Accepted

Context: `ADR-018` solves one direction of schema evolution — old data,
read in the *current* shape. There's a genuinely different, opposite
need: a consumer that hasn't upgraded yet (a legacy integration pinned to
`v1`, a slow-moving client) wants *current* data, but in the *old* shape
it still understands. `docs/design-docs/07 §7.3` names this precisely:
**forward map (upcast)**, old→current, for replay; **backward map
(downcast)**, current→old, "a query-time content-negotiation concern,"
not a replay concern. EventSouring only had the forward direction until
now.

Decision:
- Each registered schema version gains an optional `downcastToPrevious`
  mapping — symmetric to `ADR-018`'s `upcastFromPrevious`, same
  registration mechanism, same declarative expression mechanism
  (currently OData `compute()` per `ADR-018`; if `ADR-018` itself moves
  onto JS/CEL + GraphQL directives once the OData-to-GraphQL swap lands,
  `downcastToPrevious` moves with it — **confirmed, this move actually
  happened and the predicted symmetry held**: `src/EventStore.Upcasting/
  DowncastChain.cs` constructs with the same `IUpcastExpressionEvaluator`
  `UpcastChain` uses, CEL by default per `ADR-053` — found by a design-
  compliance audit this session, which checked the prediction against
  the real code rather than assuming it — the two stay symmetric by
  construction, not by separately remembering to update both).
- **`DowncastChain` walks backward, hop by hop**, from an entity's actual
  current shape down to whatever older version a consumer explicitly
  requests — the same one-hop-at-a-time design `UpcastChain` (`ADR-018`)
  already uses, just applying each version's `downcastToPrevious` instead
  of `upcastFromPrevious`, in the opposite direction.
- **Trigger: an explicit request for an older version, never a default.**
  A consumer states the version it wants (e.g. `asOfSchemaVersion` on a
  Follow connection, or a query argument once `ADR-037`'s GraphQL layer
  exists) — omitting it means "current version," the existing behavior,
  completely unchanged. Downcasting never happens speculatively or
  automatically.
- **Deliberately never materialized or persisted, unlike `ADR-027`'s
  upcasts.** An upcast has exactly one legitimate target — "the" current
  version — so materializing it once is a bounded, worthwhile
  investment. A downcast has as many potential targets as there are
  historical versions of a type, requested by however many different
  legacy consumers happen to still be around — persisting every
  version-pair combination anyone might ask for is unbounded, likely
  wasted work for versions nobody actually requests. Computed fresh,
  read-time, every time, is the right trade here specifically because the
  target isn't fixed the way `ADR-027`'s is.

Consequences:
- This is genuinely just `ADR-018`'s mechanism, mirrored — no new
  transform engine, no new expression language, no new registration
  surface beyond one more optional field per version. The cost of adding
  it is small precisely because `ADR-018` already did the hard design
  work (why hop-by-hop, why a narrow expression language is enough, why
  it's declarative data and not code).
- A version with no `downcastToPrevious` registered simply can't be
  downcast to from the version above it — the chain stops and the
  request fails (a `400`, the specific shape not designed further here)
  rather than guessing. Symmetric with `ADR-018`'s "no upcaster
  registered — passed through unchanged" accepted risk, except downcast
  has no safe "pass through unchanged" fallback (an old consumer fed a
  field it can't parse is exactly the failure mode this feature exists
  to prevent) — so the failure mode here is a hard stop, not a silent
  pass-through.
- Unlike `ADR-020`'s live upcast validation (which doubles as a
  compatibility check using real data), nothing about serving a
  downcast validates anything at publish time — `downcastToPrevious`
  mappings are only ever exercised on read, so a broken one is only
  discovered when an old-version consumer actually asks, later than
  `ADR-020`'s upcast validation would catch an equivalent problem. Worth
  being aware of, not solved further here.
