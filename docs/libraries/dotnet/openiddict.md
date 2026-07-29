[← Libraries index](../README.md)

# OpenIddict (dotnet)

**What it's for:** a complete, spec-compliant OAuth 2.0/OpenID Connect
server — token issuance, introspection, discovery — as a library you
host inside your own ASP.NET Core app, rather than a separate product to
stand up and operate.

**Why bought, not built:** implementing OAuth2/OIDC correctly (grant
types, token lifetime/revocation, discovery metadata, JWKS rotation) is
exactly the kind of complex, security-critical, already-solved problem
"buy over build" exists for — getting any of it subtly wrong is a real
vulnerability, not just a bug.

## General usage

```csharp
builder.Services.AddOpenIddict()
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/connect/token");
        options.AllowClientCredentialsFlow();
        options.AddEncryptionKey(...).AddSigningKey(...);
        options.UseAspNetCore().EnableTokenEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });
```

A client then requests a token the standard way:

```
POST /connect/token
grant_type=client_credentials&client_id=...&client_secret=...
```

## Where this project uses it

`ADR-006` — `EventStore.DevIdp`, an in-process OpenIddict host seeded
with the clients in `features/auth.md`'s table, orchestrated alongside
the rest of the system via [.NET Aspire](aspire.md). Also performs OAuth
2.0 Token Exchange (RFC 8693) for `ADR-036`'s UCAN exchange and
`ADR-040`'s ticket issuance/introspection — same host, three grant
shapes.

## Links

- [openiddict.com](https://openiddict.com/)
- [github.com/openiddict/openiddict-core](https://github.com/openiddict/openiddict-core)
