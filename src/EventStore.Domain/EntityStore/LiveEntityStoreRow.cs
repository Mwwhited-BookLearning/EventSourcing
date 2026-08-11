namespace EventStore.Domain.EntityStore;

// Shape is the data-model authority: docs/data/entity-store.md, "Live
// View -- the ungated counterpart (ADR-042)". Folded by the exact same
// mechanism as EntityStoreRow, minus the AuthorityStatus gate -- every
// event updates this the moment it's received, unattested/pending_review/
// rejected included. Deliberately has no LastAppliedLogicalTime/Hash/
// ConflictFlag/LateArrivalFlag of its own: the late-arrival ordering
// guarantee and conflict detection are specifically the AUTHORITATIVE
// view's concern (ADR-024/029) -- this is the simpler "best current
// guess" picture, folded in arrival order, no ordering protection.
public class LiveEntityStoreRow
{
    public string EntityId { get; set; } = default!;    // same {appId}:{entityType}:{uniqueId} key as EntityStoreRow, PK
    public string EntityType { get; set; } = default!;
    public string Data { get; set; } = default!;          // folds every event immediately, no AuthorityStatus gate
    public string Extensions { get; set; } = default!;
    public string AuthorityStatus { get; set; } = default!; // the MOST RECENT contributing event's status -- unattested/pending_review/accepted/rejected, never rolled up/hidden
    public long LastAppliedSequenceNumber { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
