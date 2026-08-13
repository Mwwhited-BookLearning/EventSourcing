// ADR-039's client outbox/entity cache are IndexedDB-backed; jsdom (the
// Vitest test environment below) doesn't implement IndexedDB at all --
// fake-indexeddb is the standard, real-enough-for-tests shim, imported once
// here so every spec file gets a working `indexedDB` global with no
// per-file boilerplate. Same one-line setup packages/reference-app's own
// vitest.setup.ts uses -- this package has its own copy since it's this
// package's own spec files (db/, stores/) that actually need it, not the
// reference app's.
import 'fake-indexeddb/auto'
