[← Libraries index](../README.md)

# Dav.AspNetCore.Server (dotnet) — considered, not adopted

> **Not adopted.** `ADR-032` decided to skip WebDAV entirely rather
> than adopt any library — see
> [the WebDAV library comparison](../../comparisons/webdav-library.md).
> This library was free and current enough to use (unlike archived
> NWebDav), but WebDAV's one unique value (OS-native file-manager
> mounting) wasn't worth its thin community (3 stars, 7 forks) once the
> attachment store's other access paths were confirmed already served
> by plain HTTP and GraphQL. Kept here as a record of what was
> evaluated, not as a recommendation.

**What it's for:** a WebDAV (RFC 4918) server library for ASP.NET Core —
built explicitly on NWebDav's own concepts, with a modern, minimal-API-
style integration surface.

**Why it's a candidate:** implementing RFC 4918 correctly from scratch
is a real, fiddly protocol implementation this project's value doesn't
come from writing itself, and unlike NWebDav (`docs/libraries/dotnet/
nwebdav.md`, now archived), this one is still maintained.

## General usage

```csharp
var app = builder.Build();
app.Map("/dav", davApp =>
{
    davApp.UseWebDav();
});
```

## Where this project uses it

`ADR-032` — browsable access to binary attachments, replacing the
originally-named NWebDav (see the comparison above for why).

## Links

- [github.com/ThuCommix/Dav.AspNetCore.Server](https://github.com/ThuCommix/Dav.AspNetCore.Server)
- [nuget.org/packages/Dav.AspNetCore.Server](https://www.nuget.org/packages/Dav.AspNetCore.Server) (latest: 1.0.1, targets .NET 7.0+)
