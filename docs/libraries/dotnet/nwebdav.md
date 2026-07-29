[← Libraries index](../README.md)

# NWebDav (dotnet)

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

`ADR-032` — browsable access to binary attachments, and the noted
starting point for `ADR-039`'s deferred OS-level virtual filesystem
(OneDrive/Google Drive/iCloud-style mounted attachments), which is a
client-side WebDAV *consumer* built on top of this server surface, not a
second implementation of the protocol.

## Links

- [github.com/ramondeklein/nwebdav](https://github.com/ramondeklein/nwebdav)
- Alternative considered: [Dav.AspNetCore.Server](https://github.com/ThuCommix/Dav.AspNetCore.Server)
  (newer, explicitly built on NWebDav's concepts) — not chosen over
  NWebDav here for lack of a specific reason to prefer the newer,
  less-established option; worth re-evaluating at build time.
