using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EventStore.DevIdp;

// OpenIddict's own application/token/scope store -- EF Core InMemory, per
// ADR-006: "dev/POC only, no realm-export file, no admin console." This is
// deliberately a separate store from EventStoreContext -- DevIdp is a
// throwaway dev-mode IdP, not part of the durable event log.
//
// "Delegated Grants, RBAC, Federated Claims" (ADR-043/044/046/047) adds
// five identity/trust tables here too -- see AppTrustRoot.cs/Role.cs/
// TrustedFederationIssuer.cs's own comments for why they live in DevIdp
// rather than EventStoreContext despite docs/data/schema-registry.md's
// documentation grouping.
public class DevIdpDbContext(DbContextOptions<DevIdpDbContext> options) : DbContext(options)
{
    public DbSet<AppTrustRoot> AppTrustRoots => Set<AppTrustRoot>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<TrustedFederationIssuer> TrustedFederationIssuers => Set<TrustedFederationIssuer>();
    public DbSet<FederatedIdentityMapping> FederatedIdentityMappings => Set<FederatedIdentityMapping>();
    public DbSet<RevokedDelegation> RevokedDelegations => Set<RevokedDelegation>(); // ADR-104

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppTrustRoot>(e => e.HasKey(x => new { x.AppId, x.IssuerDid }));
        modelBuilder.Entity<Role>(e =>
        {
            e.HasKey(x => new { x.AppId, x.RoleName });
            // Every other List<T> property anywhere else in this codebase
            // (EventStoreContext's own JsonValueConverter) always gets an
            // explicit conversion -- this one was missed initially, and
            // EF Core's InMemory provider throws at model-build time
            // (Database.EnsureCreatedAsync, called once at DevIdp startup)
            // for a bare, unconfigured List<string> property, which broke
            // EVERY DevIdp-backed test, not just this item's own new ones --
            // found only by re-running an already-passing item 22 test file
            // after adding this entity and seeing it newly fail too.
            e.Property(x => x.Permissions)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => v));
        });
        modelBuilder.Entity<RoleAssignment>(e => e.HasKey(x => new { x.ActorId, x.AppId, x.RoleName }));
        modelBuilder.Entity<UserPermission>(e => e.HasKey(x => new { x.ActorId, x.AppId, x.Permission }));
        modelBuilder.Entity<TrustedFederationIssuer>(e => e.HasKey(x => new { x.AppId, x.Issuer }));
        modelBuilder.Entity<FederatedIdentityMapping>(e => e.HasKey(x => new { x.AppId, x.Issuer, x.Sub }));
        modelBuilder.Entity<RevokedDelegation>(e => e.HasKey(x => x.GrantRef));
    }
}
