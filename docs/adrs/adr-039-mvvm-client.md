[← ADR index](../07-adrs.md)

# ADR-039: MVVM client architecture + HTML/JS entity view definitions

Status: Accepted — the least load-bearing piece of this integration for
everything else; sequenced last deliberately.

Context: Every ADR up to this point is server/framework-side. This
design's stated purpose also includes a client story
(`docs/design-docs/01`, `03`) — a client that submits patches/actions
through a durable local outbox and renders entities without needing a
bespoke native view per entity type.

Decision:
- **MVVM, with commands dispatched through the outbox, never mutating
  local state directly.** View (structure/style only) binds to
  ViewModel (bindable properties + `ICommand`s). A `Mediator`/
  `ICommandDispatcher` routes commands into a **client-local outbox** —
  the same durable, fault/abend/restart-tolerant pattern `CLAUDE.md`'s
  standing requirement already demands of the peer-sync outbox
  (`ADR-033`); this is the third and final relationship reusing that one
  outbox/inbox transport primitive (client↔server, server↔server,
  and now the client's own local durability layer feeding it).
  The "real" state change happens only once the round-trip through
  `ADR-021`'s Entity Store and back through the client inbox completes —
  a ViewModel never assumes its own optimistic write is truth.
- **Entity view definitions are HTML+JS, rendered in an embedded web
  engine** (WebView2/WKWebView/CEF, or a real browser for web clients) —
  one rendering implementation across desktop, mobile, and web, instead
  of N native implementations per entity type. A `ViewDefinition`
  registry entry follows the exact same content-addressed, versioned,
  hashed shape `docs/data/schema-registry.md` already established for
  schemas (`EntityType`, `Version`, `ViewKind` — `list`/`detail`/`edit`/
  custom, `CompatibleSchemaVersions`, `TemplateContent`, `Hash`,
  `EffectiveFrom`/`DeprecatedAt`) — a second application of `docs/data/
  entity-store.md`'s "content-addressed, versioned definitions" pattern,
  not a bespoke third shape.
- **Native/JS bridge, two directions**: data in — the ViewModel
  serializes current entity state (already JSON-shaped, `docs/data/
  entity-store.md`) and pushes it into the web view; because the
  platform already emits `Optional<T>`-aware partial updates (`ADR-022`),
  the web view is just another subscriber, receiving incremental updates
  the same way the Entity Store does. Commands out — the HTML+JS view
  posts a message back through the bridge, mapping directly onto the
  same `ICommand` dispatch a native control would produce; a button
  click in an HTML view and a native button produce identical
  downstream events.
- **Fallback and tolerance, consistent with `ADR-038`**: no view
  definition for an entity type/version renders a generic property-list
  view (native, not web-based) rather than failing to display the entity
  at all. Properties present that no field in the view template accounts
  for (landed in `Extensions`, `ADR-022`) are shown generically or
  ignored, never a rendering failure. `ConflictFlag`/`LateArrivalFlag`
  (`ADR-024`/`ADR-029`) and `AuthorityStatus` (`ADR-035`) reuse one
  generic "flag" rendering convention, not a bespoke indicator per
  concern.
- **View definitions are treated as untrusted-ish content** — versioned
  data, potentially updated independently of app deployment: CSP
  headers, no arbitrary native API surface exposed beyond the bridge,
  sanitize/validate before rendering if authorship is ever opened beyond
  the core team.
- **Offline is the default assumption, not an edge case** — the client
  outbox/inbox pair already assumes disconnected operation is normal
  (matching `ADR-036`'s offline-capture design); view definitions and
  entity data are cached client-side so opening an entity never requires
  a network round trip, rendering from last-known-good cache and applying
  queued updates once connectivity resumes.
- **The web client is a real Progressive Web App, not just a page that
  happens to run in a browser**: a Web App Manifest makes it installable
  to a device's home screen/app list, and a Service Worker serves the
  app shell plus cached `ViewDefinition`/entity data with no network
  present at all — the same "offline is default" principle above,
  concretely implemented for the web target. The outbox itself persists
  in IndexedDB (survives a closed tab, a crashed browser, a restarted
  device), with the Background Sync API used to flush it once
  connectivity returns where the browser engine supports it, and
  "flush on next open/focus" as the same-outcome fallback where it
  doesn't (notably Safari/WebKit, as of this writing) — see
  [the PWA offline-outbox pattern](../patterns/pwa-offline-outbox.md)
  for the full mechanism and citations.
- **Multiple independent client instances can run concurrently, each
  scoped to a different entity stream.** Which `EntityType`/`AppId`/
  subscription target an instance follows is per-instance launch
  configuration, not a global singleton — a user (or an operator running
  a monitoring wall) can open several windows of the same installed app,
  each watching something different. Instances share the same origin's
  Service Worker/cache but never share outbox state: each instance's
  queued commands are namespaced to its own configuration, so no
  instance's backlog or connectivity state can block or corrupt
  another's.

Consequences:
- ~~This is the natural home for `ADR-032`'s noted-but-deferred OS-level
  virtual file system (OneDrive/Google Drive/iCloud-style mounted
  attachments) — built on top of that ADR's WebDAV surface, a client
  shell-integration concern, not a server one.~~ **Superseded**:
  `ADR-032` decided against building any WebDAV surface at all (no
  clean library, and the capability wasn't needed once its other
  access paths were confirmed already served via plain HTTP and
  GraphQL) — this client-side virtual-filesystem extension point no
  longer has a server-side surface to build on, and is dropped, not
  just deferred, unless `ADR-032` is revisited first.
- No new server-side mechanism — every piece this ADR needs
  (content-addressed registries, `Optional<T>` incremental updates, a
  durable outbox/inbox transport) already exists from earlier ADRs. This
  is client-side composition of already-designed primitives, which is
  exactly why it was safe to sequence last.
- **Resolved: raw HTML+JS with a small injected binding runtime** —
  not a compiled/templating-syntax alternative. The deciding reason:
  this ADR's own `ViewDefinition` model already commits to "the UI is
  runtime data, interpreted by a generic renderer" (content-addressed,
  versioned, fetched at runtime, never precompiled) — exactly the web
  platform's native mode, needing zero extra machinery. A custom
  templating syntax would need its own client-side compiler bundled
  into the runtime, solving a problem raw HTML+JS-plus-binding-runtime
  already solves for free — the identical reasoning [the UI framework
  comparison](../comparisons/ui-framework.md) already used to prefer
  Vue over Blazor for the same underlying property, applied a second
  time at the template-syntax layer specifically.
- **Accessibility is deliberately not decided here** — a UI-technology-
  agnostic requirement that would apply identically if `docs/
  comparisons/ui-architecture-patterns.md`'s stated fallback chain
  (MVVM → MVP → MVC → code-behind) ever fell back to a different
  pattern for some screen this ADR doesn't fully dictate. See `ADR-073`
  for the actual standard adopted.
