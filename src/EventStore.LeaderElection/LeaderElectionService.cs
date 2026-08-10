using EventStore.Domain.LeaderElection;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventStore.LeaderElection;

// ADR-078 -- one lease row per WorkerRole; whichever instance currently
// holds a role's row is that role's sole active leader. Azure's own
// Leader Election pattern adapted from a Blob Storage lease to a plain
// database row -- the one piece of shared infrastructure every deployment
// already has (ADR-004's provider portability), not a new quorum/
// consensus dependency.
public class LeaderElectionService(EventStoreContext db)
{
    // Renew (already the holder) is one atomic, portable conditional
    // UPDATE -- an equality-only WHERE clause, which EF Core's Sqlite
    // provider translates for ExecuteUpdate without issue. Stealing an
    // expired lease deliberately does NOT use an inequality
    // (LeaseExpiresAt <= now) inside ExecuteUpdate's own WHERE clause --
    // that shape failed to translate at all under Sqlite specifically
    // ("could not be translated," found only by running this; a real,
    // narrower-than-documented limitation of that provider's ExecuteUpdate
    // support, not a bug in this query). Instead: read the current row
    // (an ordinary query, no such limitation), decide expiry in memory,
    // then compare-and-swap on the EXACT LeaseExpiresAt value just
    // observed -- equality-only again. Two instances racing this exact
    // statement can never both see success: ExecuteUpdateAsync's own
    // affected-row-count reflects how many rows matched the WHERE clause
    // AT THE MOMENT the database executed it, not a read-then-write window
    // an unlucky interleaving could exploit -- whichever instance's UPDATE
    // runs first changes LeaseExpiresAt, so the second's own WHERE clause
    // (matching the now-stale value it read) matches zero rows.
    public async Task<bool> TryAcquireOrRenewAsync(string workerRole, string holderId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var newExpiry = now + leaseDuration;

        var renewed = await db.LeaderLeases
            .Where(l => l.WorkerRole == workerRole && l.LeaseHolderId == holderId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.LeaseExpiresAt, newExpiry), ct);
        if (renewed > 0)
            return true;

        var existing = await db.LeaderLeases.AsNoTracking().SingleOrDefaultAsync(l => l.WorkerRole == workerRole, ct);
        if (existing is null)
        {
            // Never had a lease row before -- first-ever acquire for this
            // role. A unique-PK violation means a concurrent instance's own
            // first-ever acquire won the race, an ordinary "I didn't win
            // this time" outcome, not a real error.
            try
            {
                db.LeaderLeases.Add(new LeaderLease { WorkerRole = workerRole, LeaseHolderId = holderId, LeaseExpiresAt = newExpiry });
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        if (existing.LeaseExpiresAt > now)
            return false; // still held by another instance, and still valid

        var stolen = await db.LeaderLeases
            .Where(l => l.WorkerRole == workerRole && l.LeaseExpiresAt == existing.LeaseExpiresAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.LeaseHolderId, holderId)
                .SetProperty(l => l.LeaseExpiresAt, newExpiry), ct);
        return stolen > 0;
    }
}
