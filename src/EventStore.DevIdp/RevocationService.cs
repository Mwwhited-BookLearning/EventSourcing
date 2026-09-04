using Microsoft.EntityFrameworkCore;

namespace EventStore.DevIdp;

// ADR-104 -- the live revocation-check store UcanValidator.ValidateAsync
// consults, symmetric with TrustRootService's own shape/idempotency
// posture.
public class RevocationService(DevIdpDbContext db)
{
    public async Task RecordRevocationAsync(Guid grantRef, DateTimeOffset revokedAt, CancellationToken ct = default)
    {
        if (await db.RevokedDelegations.AnyAsync(r => r.GrantRef == grantRef, ct))
            return; // idempotent -- same posture as TrustRootService.RegisterAsync
        db.RevokedDelegations.Add(new RevokedDelegation { GrantRef = grantRef, RevokedAt = revokedAt });
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> IsRevokedAsync(Guid grantRef, CancellationToken ct = default) =>
        db.RevokedDelegations.AnyAsync(r => r.GrantRef == grantRef, ct);
}
