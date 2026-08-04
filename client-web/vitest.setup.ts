// ADR-039's client outbox/entity cache are IndexedDB-backed; jsdom (the
// Vitest test environment below) doesn't implement IndexedDB at all --
// fake-indexeddb is the standard, real-enough-for-tests shim, imported once
// here so every spec file gets a working `indexedDB` global with no
// per-file boilerplate.
import 'fake-indexeddb/auto'
