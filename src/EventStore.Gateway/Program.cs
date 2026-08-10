using EventStore.Host.Core;
using EventStore.Spiffe;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// ADR-049 -- external TLS termination and ADR-006/017/040 authentication
// happen at this gateway; the Host it forwards to still performs its own
// actual JWT/DPoP validation (the Authorization header rides through
// unchanged, YARP's own default forwarding behavior) -- this gateway
// doesn't re-implement that. Its own distinct value is centralizing WHERE
// external traffic enters (one address, not N) and authenticating itself
// to the Host via ADR-048's SPIFFE/SPIRE identity -- the same mechanism
// peer-sync uses (EventStore.Host.Core.SpiffePeerIdentity), reused as-is
// under its own distinct SPIFFE ID path rather than a second mechanism.
var gatewayIdentity = new SpiffePeerIdentity(
    builder.Configuration.GetSection("Spiffe").Get<SpiffePeerOptions>()
    ?? new SpiffePeerOptions { ServicePath = "/eventstore/gateway" });

builder.Services.AddSingleton(gatewayIdentity);
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .ConfigureHttpClient((_, handler) => handler.SslOptions.ClientCertificates = [gatewayIdentity.SvidCertificate]);

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapReverseProxy();
app.Run();

public partial class Program;
