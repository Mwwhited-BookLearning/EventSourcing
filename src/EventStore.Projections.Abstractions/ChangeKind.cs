namespace EventStore.Projections.Abstractions;

// Relocated here from EventStore.Projections.Host/SnapshotMerger.cs (ADR-101)
// so IProjection<T>.OverrideChangeKind can reference it without Abstractions
// depending on Host (the wrong direction -- Host already depends on
// Abstractions). Still a local, deliberately-not-shared-with-EventStore.
// Domain's-own-ChangeKind enum, for the same reason as before: ProjectionHost's
// only contact with the write side is HTTP JSON (GET /registry/{eventType}/
// change-kind returns this as a plain string), so it parses its own copy
// rather than sharing a CLR type across the write/read boundary the
// project-reference graph otherwise keeps hard (docs/06-solution-structure.md).
public enum ChangeKind { Full, Partial }
