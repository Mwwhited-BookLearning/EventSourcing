using Microsoft.EntityFrameworkCore;

namespace EventStore.DevIdp;

// ADR-044.
public class TrustRootService(DevIdpDbContext db)
{
    public async Task RegisterAsync(string appId, string issuerDid, string? description, CancellationToken ct = default)
    {
        if (await db.AppTrustRoots.AnyAsync(t => t.AppId == appId && t.IssuerDid == issuerDid, ct))
            return; // idempotent -- re-registering the same (AppId, IssuerDid) pair is a no-op, not a duplicate-key error
        db.AppTrustRoots.Add(new AppTrustRoot { AppId = appId, IssuerDid = issuerDid, Description = description, RegisteredAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> IsTrustedAsync(string appId, string issuerDid, CancellationToken ct = default) =>
        db.AppTrustRoots.AnyAsync(t => t.AppId == appId && t.IssuerDid == issuerDid, ct);
}
