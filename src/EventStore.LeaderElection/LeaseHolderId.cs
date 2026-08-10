namespace EventStore.LeaderElection;

// ADR-078 -- "this instance's own identity (host name + process id, or
// similar)." One value per process, computed once and reused for every
// worker role this process attempts to lead -- if this process holds
// TWO roles' leases at once (an ordinary, expected outcome, not a
// conflict; ADR-078's roles are independent of each other), both rows
// legitimately carry the same LeaseHolderId.
public static class LeaseHolderId
{
    public static readonly string Current = $"{Environment.MachineName}:{Environment.ProcessId}";
}
