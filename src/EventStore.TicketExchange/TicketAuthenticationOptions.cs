using Microsoft.AspNetCore.Authentication;

namespace EventStore.TicketExchange;

public class TicketAuthenticationOptions : AuthenticationSchemeOptions
{
    // EventStore.DevIdp's own introspection endpoint (ADR-040's RFC
    // 7662-shaped extension) -- resolved from the same "Authentication:
    // Authority" configuration value JwtBearer's own options already use
    // (HostCoreExtensions), never a second config key for the same IdP.
    public string IntrospectionEndpoint { get; set; } = default!;
}
