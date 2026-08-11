using Microsoft.EntityFrameworkCore;

namespace EventStore.DevIdp;

// ADR-046 -- roles bundle permissions, users are assigned roles, and a
// direct per-user permission grant is additive alongside whatever role(s)
// a user holds. Role-to-permission expansion happens once, at token
// issuance (GetFlattenedClaimsAsync below) -- no existing claim check
// anywhere in this design changes; it's unaware whether a claim arrived
// via a role or a direct grant.
public class RoleService(DevIdpDbContext db)
{
    public async Task DefineRoleAsync(string appId, string roleName, List<string> permissions, CancellationToken ct = default)
    {
        var existing = await db.Roles.SingleOrDefaultAsync(r => r.AppId == appId && r.RoleName == roleName, ct);
        if (existing is null)
            db.Roles.Add(new Role { AppId = appId, RoleName = roleName, Permissions = permissions });
        else
            existing.Permissions = permissions;
        await db.SaveChangesAsync(ct);
    }

    public async Task AssignRoleAsync(string actorId, string appId, string roleName, CancellationToken ct = default)
    {
        if (await db.RoleAssignments.AnyAsync(r => r.ActorId == actorId && r.AppId == appId && r.RoleName == roleName, ct))
            return;
        db.RoleAssignments.Add(new RoleAssignment { ActorId = actorId, AppId = appId, RoleName = roleName, AssignedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeRoleAsync(string actorId, string appId, string roleName, CancellationToken ct = default)
    {
        var assignment = await db.RoleAssignments.SingleOrDefaultAsync(r => r.ActorId == actorId && r.AppId == appId && r.RoleName == roleName, ct);
        if (assignment is not null)
        {
            db.RoleAssignments.Remove(assignment);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task GrantDirectPermissionAsync(string actorId, string appId, string permission, CancellationToken ct = default)
    {
        if (await db.UserPermissions.AnyAsync(p => p.ActorId == actorId && p.AppId == appId && p.Permission == permission, ct))
            return;
        db.UserPermissions.Add(new UserPermission { ActorId = actorId, AppId = appId, Permission = permission });
        await db.SaveChangesAsync(ct);
    }

    // The union of every role-bundled permission plus every direct grant --
    // additive-only, no explicit-deny concept, no precedence to resolve.
    public async Task<IReadOnlyList<string>> GetFlattenedPermissionsAsync(string actorId, string appId, CancellationToken ct = default)
    {
        var roleNames = await db.RoleAssignments.Where(r => r.ActorId == actorId && r.AppId == appId).Select(r => r.RoleName).ToListAsync(ct);
        // Permissions has an explicit HasConversion (DevIdpDbContext) --
        // EF can't translate SelectMany over a converted property, so the
        // roles are materialized first and flattened client-side.
        var roles = await db.Roles.Where(r => r.AppId == appId && roleNames.Contains(r.RoleName)).ToListAsync(ct);
        var rolePermissions = roles.SelectMany(r => r.Permissions);
        var directPermissions = await db.UserPermissions.Where(p => p.ActorId == actorId && p.AppId == appId).Select(p => p.Permission).ToListAsync(ct);
        return rolePermissions.Concat(directPermissions).Distinct().ToList();
    }
}
