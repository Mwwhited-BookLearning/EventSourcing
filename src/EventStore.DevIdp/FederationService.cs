using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EventStore.DevIdp;

// ADR-047.
public class FederationService(DevIdpDbContext db, IHttpClientFactory httpClientFactory)
{
    public async Task RegisterIssuerAsync(string appId, string issuer, string jwksUri, string? description, CancellationToken ct = default)
    {
        var existing = await db.TrustedFederationIssuers.SingleOrDefaultAsync(t => t.AppId == appId && t.Issuer == issuer, ct);
        if (existing is null)
            db.TrustedFederationIssuers.Add(new TrustedFederationIssuer { AppId = appId, Issuer = issuer, JwksUri = jwksUri, Description = description });
        else
            existing.JwksUri = jwksUri;
        await db.SaveChangesAsync(ct);
    }

    public Task<TrustedFederationIssuer?> FindAsync(string appId, string issuer, CancellationToken ct = default) =>
        db.TrustedFederationIssuers.SingleOrDefaultAsync(t => t.AppId == appId && t.Issuer == issuer, ct);

    // The same operational shape ADR-006's own discovery-document fetch
    // already has (auth.md), just pointed at a third party's JWKS instead
    // of EventStore.DevIdp's own.
    public async Task<IReadOnlyList<SecurityKey>> FetchSigningKeysAsync(string jwksUri, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient();
        var json = await client.GetStringAsync(jwksUri, ct);
        var jwks = new JsonWebKeySet(json);
        return jwks.Keys.Cast<SecurityKey>().ToList();
    }

    // ADR-047's own resolved open question: (Issuer, Sub) together, JIT-
    // provisioned -- first-seen pair mints a new ActorId, a repeat lookup
    // reuses it, never bare Sub alone (which could collide two different
    // people's identifiers from two different registered issuers).
    public async Task<string> GetOrCreateActorIdAsync(string appId, string issuer, string sub, CancellationToken ct = default)
    {
        var existing = await db.FederatedIdentityMappings.SingleOrDefaultAsync(m => m.AppId == appId && m.Issuer == issuer && m.Sub == sub, ct);
        if (existing is not null)
            return existing.ActorId;

        var actorId = $"federated:{Guid.NewGuid():N}";
        db.FederatedIdentityMappings.Add(new FederatedIdentityMapping { AppId = appId, Issuer = issuer, Sub = sub, ActorId = actorId, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        return actorId;
    }
}
