namespace EventStore.DevIdp;

// ADR-046 -- a named, AppId-scoped bundle of the same opaque permission/
// claim strings used everywhere else in this design. Built here as a
// plain CRUD-backed table (ADR-046's own original decision) rather than
// folded from reserved control-plane events (ADR-067's later revision,
// deferred to that build-plan item -- 08-build-plan.md's own explicit
// "build the simple way first" note for this item).
public class Role
{
    public string AppId { get; set; } = default!;
    public string RoleName { get; set; } = default!;
    public List<string> Permissions { get; set; } = [];
}

// The missing piece ADR-046's own text never explicitly modeled (it jumps
// straight from "Role bundles permissions" to "the IdP expands a user's
// ROLES... into one flattened claim set" without ever showing which table
// records that a given ActorId actually holds a given Role) -- a real,
// necessary addition, not a design change.
public class RoleAssignment
{
    public string ActorId { get; set; } = default!;
    public string AppId { get; set; } = default!;
    public string RoleName { get; set; } = default!;
    public DateTimeOffset AssignedAt { get; set; }
}

// ADR-046 -- additive-only, direct per-user permission grants, unioned
// with role-derived permissions at token issuance. No explicit-deny
// concept exists anywhere in this model, by design.
public class UserPermission
{
    public string ActorId { get; set; } = default!;
    public string AppId { get; set; } = default!;
    public string Permission { get; set; } = default!;
}
