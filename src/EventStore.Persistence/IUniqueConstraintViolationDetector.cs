namespace EventStore.Persistence;

// The concurrent-retry idempotency race (ADR-011, docs/06-solution-structure.md
// "Publish idempotency"): two concurrent requests carrying the same never-yet-seen
// EventId can both pass a preceding "not found" lookup before either commits.
// EventAppender must catch the resulting unique-constraint violation at the
// database level -- which exception type/error code that is is provider-specific,
// so this is a real per-provider seam like IJsonPathTranslator, resolved via DI
// per EventStore.Host.<Provider>, not a switch on a provider name string. The
// three implementations live in their respective
// EventStore.Persistence.Migrations.<Provider> projects (the ones that already
// reference each provider's own ADO.NET exception types), not centrally here.
public interface IUniqueConstraintViolationDetector
{
    bool IsUniqueConstraintViolation(Exception exception, string columnName);
}
