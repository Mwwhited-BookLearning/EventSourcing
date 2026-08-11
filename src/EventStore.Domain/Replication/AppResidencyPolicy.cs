namespace EventStore.Domain.Replication;

// ADR-061 -- a per-AppId residency constraint, folded from the reserved
// AllowedRegionsSet event (ADR-067's control-plane-actions-as-reserved-
// events pattern, the same synchronous-fold posture FeatureFlagState
// already established: this table is read by the SAME process that
// publishes the event, no cross-process Follow fold needed). Absent for
// an AppId means unconstrained -- purely additive, ADR-061's own text.
public class AppResidencyPolicy
{
    public string AppId { get; set; } = default!;
    public List<string> AllowedRegions { get; set; } = new();
    public long LastAppliedSequenceNumber { get; set; }
}
