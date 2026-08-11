namespace EventStore.DevIdp;

// ADR-067 -- a configured, known AppId list, not dynamic discovery, matching
// this project's existing "deployment configuration, not auto-discovered"
// posture (e.g. RFC 9470's own acr_values taxonomy). RbacProjectionWorker is
// opt-in: it never starts unless at least one AppId is configured, so every
// pre-existing DevIdp-only test (no Host counterpart running) is completely
// unaffected.
public class RbacProjectionOptions
{
    public List<string> AppIds { get; set; } = [];
}
