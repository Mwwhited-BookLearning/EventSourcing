[← Comparisons index](README.md)

# WebDAV Library: NWebDav vs. Dav.AspNetCore.Server vs. IT Hit WebDAV Server Engine vs. Skip

**Decided in:** `ADR-032` (skip WebDAV entirely). Originally named
NWebDav with no specific reason to prefer it over the newer alternative
beyond it existing first — flagged in `docs/10-open-questions.md` as
worth re-evaluating at build time. Re-evaluated here with real, current
facts: no library option is clean, and once the *other* attachment
access paths were confirmed already served by mechanisms this design
had already adopted (plain HTTP, GraphQL — see `ADR-032`'s "Access
paths, matched to standards individually" section), the remaining
WebDAV-specific value (OS-native file-manager mounting) turned out to
be a "nice to have," not a real gap — worth skipping rather than paying
any of the three library trade-offs below for.

**Stated requirement driving this comparison:** a maintained,
ASP.NET-Core-native RFC 4918 WebDAV server library this design's
attachment surface (`ADR-032`) can depend on without inheriting
someone else's abandoned project — and, implicitly until now, a *free*
one, since every other library this design has adopted (EF Core,
OpenIddict, HotChocolate, Jint, Vue, ...) is free/open-source. **Named
explicitly: WebDAV itself is not a hard requirement here** — the core
"fetch an attachment by reference, with random/seekable access" need
is already satisfied with plain `GET`+`ADR-031`'s Range-request support,
no WebDAV involved. This comparison is scoped to "if WebDAV's
browsable-hierarchy value is wanted, which library" — a "maybe," not a
must, per `ADR-032`.

## The options

### Option A — NWebDav (the original pick)

| | |
|---|---|
| **Pros** | Longer history, broad platform-target list (`netstandard1.6` up through modern `net`/`net10.0` computed targets), an abstract `IStore`/locking-provider model that fits this design's virtual-folder-over-content-addressed-storage need well. |
| **Cons** | **The repository was archived by its owner on July 19, 2025 and is now read-only** — confirmed directly against GitHub, not assumed. Last NuGet release (`NWebDav.Server.AspNetCore` 0.1.36) shipped November 23, 2021. No further commits, fixes, or security patches will ever land. |

### Option B — Dav.AspNetCore.Server

| | |
|---|---|
| **Pros** | **Not archived** — the repository still shows active CI. Targets .NET 7.0+ directly, with a clean, idiomatic minimal-API-style integration (`app.Map("/dav", davApp => davApp.UseWebDav())`) matching this design's own ASP.NET Core conventions. Free/open-source, consistent with every other library this design has adopted. |
| **Cons** | Last NuGet release (1.0.1) shipped May 5, 2023 — not itself under heavy active development, just meaningfully more current than NWebDav. Small community footprint (3 stars, 7 forks at time of writing) — genuinely thin battle-testing behind it, a real risk for a security-relevant surface (RFC 4918 request parsing, content serving), not just a "smaller but fine" footnote. |

### Option C — IT Hit WebDAV Server Engine for .NET

| | |
|---|---|
| **Pros** | The one option that's *actually* well-maintained by an organization whose business model depends on it staying that way — real release history through v14, full cross-platform ASP.NET Core support (Windows/macOS/Linux), multiple storage backends (file system, SQL, S3, Azure, DMS/CMS/CRM) already built. The most feature-complete and lowest-risk option on pure engineering merit. |
| **Cons** | **Commercial — paid licensing**, with a trial available but no free tier. Would be the first paid dependency anywhere in this design; every other adopted library so far is free/open-source, and this project's own stated purpose (`README.md`: "a worked example... a teaching example") sits awkwardly next to introducing a cost a reader following along would have to pay to fully build it. |

## Recommendation

**Skip WebDAV entirely — decided, not left open.** NWebDav is
disqualified outright (archived). Dav.AspNetCore.Server is free and
current enough, but thin — a real risk for security-relevant surface
area with this little community behind it. IT Hit is the only option
with real engineering confidence behind it, but commercial, which cuts
against this design's free-throughout posture and its teaching-example
purpose. Hand-rolling RFC 4918 to avoid all three trade-offs was also
considered and declined — a real protocol implementation (locking
semantics, the XML property model, every required method) costs more
than the convenience is worth.

**The deciding fact, not just a tiebreaker**: once the *other*
attachment access paths were checked, all three were already fully
served by mechanisms this design had adopted for unrelated reasons —
upload (plain HTTP `POST`), fetch-with-random-access (plain HTTP `GET`
+ Range Requests, `RFC 7233`), and browse/list (a GraphQL query against
the owning entity). WebDAV's only genuinely unique value here was
OS-native file-manager mounting — a real convenience, but not a
capability anything else in this design depends on. Paying any of the
three library trade-offs above for a convenience nothing requires
isn't worth it; revisit only if a specific, real need for native
mounting shows up later.
