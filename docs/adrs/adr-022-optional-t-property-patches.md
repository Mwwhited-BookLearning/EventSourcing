[← ADR index](../07-adrs.md)

# ADR-022: `Optional<T>` property-level patches (refines `ADR-016`)

Status: Accepted — refines `ADR-016`, does not discard it.

Context: `ADR-016`'s `Partial` merge treats an entire event payload as one
merge-patch unit: a field present overwrites, a field **absent** is left
untouched — and, deliberately, `ADR-016` chose *not* to support an
explicit `null` clearing a property ("this design does not want" a
`Partial` event's field to ever erase a previously-known fact). Reviewing
the second design package (`docs/design-docs/06`) surfaces a concrete,
narrow disagreement with that choice: real partial-update needs
genuinely include "clear this field" as a distinct, wanted operation
(e.g. clearing a `MiddleName` that was mistakenly set), not just "add or
overwrite." Plain JSON can't express this without a wrapper — `{"x":
null}` and key-omitted both need to mean different things, and a bare
`Optional<T>`-shaped payload solves exactly that, more precisely than
whole-payload JSON Merge Patch semantics do.

Decision:
- **`Optional<T>` becomes the real per-property patch representation**,
  superseding `ADR-016`'s whole-payload-merge description of `Partial`.
  Three states per property, not two: **unspecified** (omitted from the
  payload — leave current value), **specified as `null`** (explicit
  clear), **specified with a value** (overwrite). `System.Text.Json`
  never invokes a property's converter for a key that's missing, so
  "unspecified" is captured by omission automatically — the converter's
  only real job is telling explicit-`null` apart from absent.
- `ChangeKind` (`Full` | `Partial`) stays exactly as `ADR-016` defined it
  at the **event-type** registration level — this ADR doesn't change
  what `ChangeKind` means, only how a `Partial` event's fields are
  interpreted once one arrives: every property is now wrapped
  `Optional<T>`, and the fold rule is:

  | Patch value | Effect on Entity Store (`ADR-021`) |
  |---|---|
  | `Unspecified` | Leave current value untouched |
  | `Specified(null)` | **Clear** the property (overwrites prior value with `null`) |
  | `Specified(value)` | Overwrite with value |

  This is `ADR-016`'s "same overlay rule masking's consumer guidance
  states" claim, **narrowed**: masking's own guidance (`ADR-009`) is
  about a *masked* field never being written over good data — that
  guidance is untouched (a masked/redacted field is still treated as
  absent, not as an explicit clear, since masking never produces a real
  `null`). What changes is only genuinely explicit, sender-intended
  `null`.
- A `Full` event is **not** wrapped in `Optional<T>` — every property is
  simply present with its value (matching `ADR-016`'s existing "replace
  the whole snapshot" behavior for `Full`, unchanged). `Optional<T>`
  wrapping only applies to `Partial` payloads, where the
  absent/null/value distinction actually matters.
- Unknown properties (a field the receiving schema doesn't recognize) are
  folded exactly like any other `Specified(value)`, just routed to the
  entity's `Extensions` bag (`ADR-021`'s Entity Store row) instead of a
  typed slot — this is unchanged from how `ADR-023`'s persist-everything
  posture already treats unrecognized data generally: still applied, not
  dropped.
- Wire-format alternatives considered and not chosen, for the record
  (same three options `docs/design-docs/06 §6.5` weighs): field-mask +
  full nullable payload (protobuf-style — redundant on the wire); JSON
  Patch (RFC 6902 — unambiguous but verbose for this system's actual
  need, and structural-edit-shaped rather than property-overlay-shaped).
  `Optional<T>` is chosen for the same reason design-docs chose it: this
  design is already strongly-typed C# throughout (`StoredEvent`, entity
  projections, `IProjection<TReadModel>`), and it avoids maintaining a
  parallel changed-properties list in sync with the payload.

Consequences:
- `SnapshotMerger` (`09-cqrs-read-models.md`) changes from a bare
  JSON-Merge-Patch (RFC 7396) call to an `Optional<T>`-aware fold — RFC
  7396 is no longer cited as this design's actual merge semantics
  (`ADR-016`'s closing note is superseded on this specific point); the
  fold rule above is now the canonical one, documented here instead.
- Every `IProjection<TReadModel>` implementation is unaffected — the
  centralization principle `ADR-016` established (individual projections
  never see raw events, `ChangeKind`, or merge logic) still holds; only
  what `ProjectionHost`'s internal merge step actually does changes.
- Existing masking guidance (`ADR-009`) and `ADR-016`'s "producer
  discipline, no ordering enforcement" consequence are otherwise
  unaffected — a `Partial` event for a key with no existing snapshot
  still simply starts one from whatever fields it specifies.
- A publisher must now be deliberate about the difference between
  omitting a field and sending `{"field": null}` — a real, new footgun
  (accidentally clearing a property that was meant to be left alone)
  that the old whole-payload-merge design didn't have, traded for
  correctly supporting the "explicit clear" case at all.
