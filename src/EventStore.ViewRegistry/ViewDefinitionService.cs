using System.Security.Cryptography;
using System.Text;
using EventStore.Domain.Views;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.ViewRegistry;

// ADR-039 -- the same content-addressed, versioned registry pattern
// EventStore.SchemaRegistry.SchemaRegistryService already established for
// schemas, applied to view templates. Deliberately much smaller: no
// upcast/downcast chains, no claims, no FilterableFields -- ViewDefinition
// carries none of that (docs/data/schema-registry.md's own shape). A new
// registration for the same (EntityType, ViewKind) pair increments Version
// and marks the prior one DeprecatedAt (never deleted -- ADR-038's "N-1/N+1,
// deprecated but still served" discipline, reused here for the exact same
// reason: an already-cached client shouldn't lose a template it fetched
// before a newer one was registered).
public class ViewDefinitionService(EventStoreContext db)
{
    public async Task<RegisterViewDefinitionResult> RegisterAsync(RegisterViewDefinitionRequest request, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (!Enum.TryParse<ViewKind>(request.ViewKind, ignoreCase: true, out var viewKind))
            errors.Add($"viewKind must be one of List, Detail, Edit, Custom (got: {request.ViewKind})");

        if (string.IsNullOrWhiteSpace(request.TemplateContent))
            errors.Add("templateContent is required");

        if (request.CompatibleSchemaVersions is null || request.CompatibleSchemaVersions.Count == 0)
            errors.Add("compatibleSchemaVersions must name at least one schema version");

        if (string.IsNullOrWhiteSpace(request.EntityType))
            errors.Add("entityType is required");

        if (errors.Count > 0)
            return new RegisterViewDefinitionResult.ValidationFailed(errors);

        var normalizedEntityType = request.EntityType.ToLowerInvariant();

        var priorVersion = await db.ViewDefinitions
            .Where(v => v.EntityType == normalizedEntityType && v.ViewKind == viewKind)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);

        var newVersion = (priorVersion?.Version ?? 0) + 1;

        if (priorVersion is not null && priorVersion.DeprecatedAt is null)
        {
            priorVersion.DeprecatedAt = DateTimeOffset.UtcNow;
            db.ViewDefinitions.Update(priorVersion);
        }

        var hash = ComputeHash(request.TemplateContent);
        var definition = new ViewDefinition
        {
            EntityType = normalizedEntityType,
            Version = newVersion,
            ViewKind = viewKind,
            CompatibleSchemaVersions = request.CompatibleSchemaVersions,
            TemplateContent = request.TemplateContent,
            Hash = hash,
            EffectiveFrom = DateTimeOffset.UtcNow,
        };
        db.ViewDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);

        return new RegisterViewDefinitionResult.Success(newVersion, hash);
    }

    // The client's own lookup: the currently-active (not-yet-deprecated)
    // template for this EntityType/ViewKind, unless the caller names a
    // specific SchemaVersion the active one doesn't declare compatibility
    // with -- in which case the most recent DEPRECATED-but-still-served
    // version that IS compatible is used instead, same discipline as
    // ADR-038's schema N-1/N+1 window. Returns null when nothing at all is
    // registered for this EntityType/ViewKind -- the caller's own signal to
    // fall back to the generic property-list view.
    public async Task<ViewDefinition?> GetActiveAsync(string entityType, string viewKind = "Detail", int? schemaVersion = null, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ViewKind>(viewKind, ignoreCase: true, out var kind))
            return null;

        var normalizedEntityType = entityType.ToLowerInvariant();
        var candidates = await db.ViewDefinitions
            .AsNoTracking()
            .Where(v => v.EntityType == normalizedEntityType && v.ViewKind == kind)
            .OrderByDescending(v => v.Version)
            .ToListAsync(ct);

        if (schemaVersion is { } sv)
            return candidates.FirstOrDefault(v => v.DeprecatedAt is null && v.CompatibleSchemaVersions.Contains(sv))
                ?? candidates.FirstOrDefault(v => v.CompatibleSchemaVersions.Contains(sv));

        return candidates.FirstOrDefault(v => v.DeprecatedAt is null);
    }

    private static string ComputeHash(string templateContent) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(templateContent))).ToLowerInvariant();
}
