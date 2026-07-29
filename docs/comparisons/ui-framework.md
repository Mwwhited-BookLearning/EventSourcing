[← Comparisons index](README.md)

# UI Framework for the Web Client: Vue vs. Blazor (vs. React, Angular)

**Decided in:** the concrete-implementation section of
[the MVVM pattern doc](../patterns/mvvm-client-architecture.md) (Vue 3).
**Written here:** the shootout that choice didn't get its own file for
at the time, per direct request — Vue against Blazor specifically (a
first-party, C#-native alternative this project's `ADR-041` "prefer
first-party" instinct makes a genuinely live question, not a token
option), plus the other two dominant SPA frameworks for completeness.

**Stated requirements driving this comparison** (from `ADR-039`, `ADR-041`,
[the PWA offline-outbox pattern](../patterns/pwa-offline-outbox.md)):

1. **Renders `ADR-039`'s `ViewDefinition`s as data, not compiled code** —
   a content-addressed, versioned HTML+JS entity-view template, fetched
   at *runtime* from the registry and rendered by a generic interpreter
   — never a per-entity-type recompile/redeploy of the client itself.
2. **MVVM with data + command binding**, per
   [the UI architecture comparison](ui-architecture-patterns.md).
3. **Installable, offline-first PWA** with a Service-Worker-backed,
   durable local outbox.
4. Runs identically inside an embedded web engine (WebView2/WKWebView/
   CEF) for the native shell, and in a real browser for the web target
   — one implementation, not two.
5. `ADR-041`'s stated preference for first-party/explicit tooling over
   third-party, where it doesn't cost one of the requirements above.

## The options

### Option A — Vue 3 (chosen)

Already covered in depth in
[the MVVM pattern doc](../patterns/mvvm-client-architecture.md).

| | |
|---|---|
| **Pros** | Runs natively in any browser or embedded web engine with zero bridging — satisfies requirement 4 for free, since an embedded web engine *is* a browser. Rendering `ViewDefinition` templates as runtime data (requirement 1) is squarely inside what the web platform already does natively (parse and render HTML/JS fetched at runtime) — Vue's reactivity layers cleanly on top without fighting that model. Mature PWA/Service-Worker tooling, directly reusable for requirement 3. |
| **Cons** | A second language and toolchain (npm/Vite/JS) alongside this project's otherwise all-C# backend — the one place `ADR-041`'s "prefer first-party" instinct doesn't get satisfied, stated plainly rather than glossed over. |

### Option B — Blazor (WebAssembly)

| | |
|---|---|
| **Pros** | First-party Microsoft framework — directly aligned with `ADR-041`'s stated preference, more than any other option here. Single language (C#) across backend and client, with real model/validation-code sharing. A genuine, Microsoft-supported PWA project template exists with Service Worker-based offline caching and installability — requirement 3 is a real, supported capability, not a gap. |
| **Cons** | **Requirement 1 is the decisive miss.** Blazor's component model is fundamentally ahead-of-time: a component is a compiled C#/Razor type, and even its dynamic-rendering escape hatch (`DynamicComponent`, built in since .NET 6) renders a component *by type*, not by interpreting an arbitrary HTML+JS blob fetched as data at runtime — the exact shape `ADR-039`'s `ViewDefinition` registry needs. Getting there anyway means bridging out to raw JS interop to render fetched markup, which mostly just re-imports Vue/JS's native strength through a side door rather than avoiding it. Blazor Server (the non-WASM hosting model) is disqualified outright by requirement 3 — it requires a live, persistent connection to the server to render at all, incompatible with "offline is the default assumption" (`ADR-039`). |

### Option C — React

| | |
|---|---|
| **Pros** | Largest ecosystem and hiring pool of any option here; same "runs natively in any web engine" advantage as Vue for requirements 1 and 4. |
| **Cons** | No first-party, opinionated answer to state management or an SFC-style structure/presentation split the way Vue+Pinia gives out of the box — a real React app assembles that shape from separate ecosystem choices (Redux/Zustand, a component-file convention), which is more, not fewer, decisions for this project's stated MVVM discipline to land consistently. |

### Option D — Angular

| | |
|---|---|
| **Pros** | Also a first-party-feeling, opinionated, batteries-included framework (DI container, router, forms, RxJS) with structure genuinely close to MVVM's own discipline — the framework most likely to enforce this project's separation rules by default rather than by convention alone. |
| **Cons** | Heaviest tooling/learning-curve footprint of the four for a project whose stated purpose includes being a legible teaching example; TypeScript-first in a way that adds a second typed-language boundary on top of the C#/JS split every option here already has. |

## Recommendation

**Vue, unchanged** — this comparison's purpose was to check that choice
against a real first-party alternative, not to reopen it lightly, and
Blazor is a genuinely closer call than React/Angular ever were: it wins
outright on `ADR-041`'s first-party instinct and matches Vue on the PWA/
offline requirement. It loses specifically on requirement 1 — `ADR-039`'s
`ViewDefinition` mechanism is, by its own design, "the UI is runtime
data interpreted by a generic renderer," which is exactly the web
platform's native mode and exactly what Blazor's ahead-of-time component
model resists. That single requirement is load-bearing enough to decide
this in Vue's favor even though `ADR-041`'s preference points the other
way — worth stating honestly rather than implying first-party wins by
default. **Revisit if `ADR-039`'s view-definition mechanism ever changes
shape** (e.g., a future decision to compile/deploy per-entity-type
components ahead of time instead of fetching templates as runtime data)
— that's the one change that would remove the requirement Blazor
currently loses on.
