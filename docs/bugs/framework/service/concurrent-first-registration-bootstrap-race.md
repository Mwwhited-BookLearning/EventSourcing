# Concurrent first registrations in a new AppId race on the SchemaRegistered bootstrap type

- **Found:** 2026-10-09, by the provider e2e suite (`ConcurrentRegistrationsOfDistinctEventTypesAllSucceed`), Postgres.
- **What was wrong:** the first registration in an AppId also auto-registers the built-in `SchemaRegistered` type (`SchemaRegisteredEventType.EnsureRegisteredAsync`). Check-then-insert was unsynchronised, so concurrent first registrations collided on `PK_EventTypeDefinitions` (23505) and the loser returned 500.
- **Root cause:** `SchemaRegistryService.RegisterAsync` treated the bootstrap insert's unique violation as fatal.
- **Resolution:** the `DbUpdateException` is caught, the change tracker cleared, and the error swallowed only if the bootstrap type now exists; otherwise rethrown.
