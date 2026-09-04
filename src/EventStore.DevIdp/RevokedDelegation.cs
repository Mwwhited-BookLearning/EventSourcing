namespace EventStore.DevIdp;

// ADR-104 -- DevIdp's own local fold target for EventStore.Rbac's
// UcanDelegationRevoked reserved event, populated by RbacProjectionWorker,
// mirroring TrustRootService/AppTrustRoot's identical "DevIdp keeps its
// own local queryable copy, populated by Follow" pattern (nothing in this
// design gives DevIdp a live dependency on any Host's own EventStoreContext
// database). Consulted by UcanValidator.ValidateAsync's own live
// revocation check at token-exchange time (ExchangeUcanDelegationAsync).
public class RevokedDelegation
{
    public Guid GrantRef { get; set; }
    public DateTimeOffset RevokedAt { get; set; }
}
