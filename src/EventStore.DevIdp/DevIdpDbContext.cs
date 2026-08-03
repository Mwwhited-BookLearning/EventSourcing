using Microsoft.EntityFrameworkCore;

namespace EventStore.DevIdp;

// OpenIddict's own application/token/scope store -- EF Core InMemory, per
// ADR-006: "dev/POC only, no realm-export file, no admin console." This is
// deliberately a separate store from EventStoreContext -- DevIdp is a
// throwaway dev-mode IdP, not part of the durable event log.
public class DevIdpDbContext(DbContextOptions<DevIdpDbContext> options) : DbContext(options);
