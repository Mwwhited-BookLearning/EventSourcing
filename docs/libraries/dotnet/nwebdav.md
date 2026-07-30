[← Libraries index](../README.md)

# NWebDav (dotnet) — superseded, archived

> **Archived, do not adopt.** The upstream repository was archived by
> its owner on July 19, 2025 and is now read-only — no further fixes or
> security patches will ever land. **`ADR-032` decided to skip WebDAV
> entirely rather than replace this with another library** — see
> [the WebDAV library comparison](../../comparisons/webdav-library.md).
> Kept here, not deleted, as a record of why the original pick changed.

**What it's for:** a WebDAV (RFC 4918) server library for ASP.NET Core —
exposes a virtual folder/file hierarchy over arbitrary backing data via
an abstract store interface, plus abstract locking providers
(in-memory or Redis-backed).

**Why bought, not built:** `ADR-032` committed this design to WebDAV
specifically to avoid a bespoke browse API, but never named a concrete
library — the second real gap this pass closed. Implementing RFC 4918
correctly (`PROPFIND`/`PROPPATCH`, locking semantics, the XML property
model) from scratch is a real, fiddly protocol implementation with
existing, working options — not something this project's own value comes
from writing itself.

## General usage

```csharp
builder.Services.AddSingleton<IStore>(new AttachmentWebDavStore(attachmentRepository));
app.UseWebDav(); // NWebDav.Server.AspNetCore middleware, routes PROPFIND/GET/PUT/etc.
```

`IStore`/`IStoreCollection`/`IStoreItem` are the seams this project
implements against — a virtual folder per entity, a virtual file per
`AttachmentRef` (`ADR-032`), backed by the content-addressed blob store
rather than a real filesystem.

## Where this project uses it

Nowhere, currently. `ADR-032` decided to skip WebDAV **entirely**, not
just this library — once every other real attachment access path
(upload, fetch+range, browse via GraphQL) was confirmed already served
by mechanisms already adopted for unrelated reasons, WebDAV's one unique
value (OS-native mounting) wasn't worth building at all, archived
library or not. This was originally slated for `ADR-032`'s browsable
attachment access, and as the starting point for `ADR-039`'s
then-deferred OS-level virtual filesystem (OneDrive/Google Drive/
iCloud-style mounted attachments) — both now moot: `ADR-039` was revised
to drop that client-side extension point once the server-side surface it
would have built on top of was declined. Kept here purely as a record of
what was evaluated, per [the WebDAV library comparison](../../comparisons/webdav-library.md).

## Links

- [github.com/ramondeklein/nwebdav](https://github.com/ramondeklein/nwebdav) — archived July 19, 2025
- Superseded by: [Dav.AspNetCore.Server](dav-aspnetcore-server.md)
