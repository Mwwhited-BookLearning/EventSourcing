[← Comparisons index](README.md)

# UI Architecture: MVVM vs. MVP vs. MVC vs. Code-Behind

**Decided in:** `ADR-039` (MVVM, for the entity-view client). **Written
here:** an explicit fallback priority, per direct request, for the
situations `ADR-039` doesn't fully dictate — a specific UI technology
that can't support full MVVM-style data binding, or a screen simple
enough that the full pattern wouldn't earn its keep.

**Stated requirement driving this comparison:** prefer **data and
command binding over inline logic/code-behind wherever the target UI
technology supports it** — a View should describe *what* to bind to, not
*how* to wire up the response to a click by hand, as consistently as
possible across whatever UI surfaces this design ends up with (the
embedded-web-engine entity views and Vue application shell `ADR-039`
already specifies, but also any future native screen that isn't one of
those).

## The options, in preferred order

### 1. MVVM (Model-View-ViewModel) — preferred default

Already covered in full in [the MVVM pattern doc](../patterns/mvvm-client-architecture.md)
and `ADR-039`. **Source:** John Gossman (Microsoft, 2005), introduced for
WPF/XAML, since carried into WinUI/MAUI/Blazor and — via each platform's
own reactivity system rather than XAML binding — Angular/Vue/Knockout.

| | |
|---|---|
| **Pros** | Two-way data *and* command binding is the pattern's whole point — a View binds to bindable properties and `ICommand`s with essentially zero glue code; the ViewModel is fully unit-testable with no UI mounted at all; this design's entity views (`ADR-039`) already need exactly this shape (bindable state pushed in, commands dispatched out through the client outbox). |
| **Cons** | Needs a real reactive/data-binding substrate under it (WPF/XAML binding, Vue's reactivity, a comparable web framework) — on a UI technology with no such substrate, MVVM's ViewModel ends up hand-wiring the same binding machinery a weaker platform would otherwise provide for free, at which point the pattern's main advantage is doing extra work to simulate. |

### 2. MVP (Model-View-Presenter) — first fallback

**Source:** Mike Potel (Taligent, 1996), *"MVP: Model-View-Presenter, The
Taligent Programming Model for C++ and Java"* — explicitly built as a
generalization of Smalltalk's MVC, later adopted into .NET's own UI
guidance (Microsoft began documenting MVP for WinForms-era .NET UI in
2006, precisely because WinForms lacked WPF's binding infrastructure).

| | |
|---|---|
| **Pros** | The Presenter still fully mediates between View and Model — the View has no direct Model access, same separation-of-concerns discipline as MVVM — but the View/Presenter relationship is an explicit interface contract (the Presenter calls `view.ShowX(value)` / the View calls `presenter.OnCommand()`) instead of relying on a binding engine. Works on any UI technology, since it needs no reactive substrate at all — this is exactly why it's the right fallback for a platform that can't do MVVM-style binding. |
| **Cons** | No free two-way binding — every property the View displays and every command it can trigger needs an explicit method on the Presenter/View interface, which is more boilerplate than MVVM's binding for a UI with many bindable fields. **Still prefer command-style methods (`OnSubmit()`) over inline event-handler bodies** wherever the platform's own widget model allows delegating a click to a named handler method rather than embedding logic directly in the handler — this is the stated command-binding-over-inline principle, applied at the one notch below full MVVM binding. |

### 3. MVC (Model-View-Controller) — second fallback

**Source:** Trygve Reenskaug (Xerox PARC, 1979), designed for
Smalltalk-80 — the common ancestor both MVP and MVVM descend from.

| | |
|---|---|
| **Pros** | The simplest of the three real separations — a Controller receives input, updates the Model, and selects a View to render the result; well understood, essentially universal across web frameworks server-side (this is the shape most server-rendered web frameworks already default to), no special client-side binding infrastructure needed at all. |
| **Cons** | Weakest testability of the three for genuinely interactive client UI — the View in classic MVC is often allowed to read the Model directly (unlike MVP/MVVM's strict mediation), which is fine for a server-rendered request/response page but a real step down in discipline for a stateful, long-lived client screen. Reach for this only where the UI is fundamentally request/response-shaped (a server-rendered admin page, a simple form-post screen) rather than a persistent, richly interactive client view — for anything closer to that shape, MVP is the better fallback from MVVM, not MVC. |

### 4. Code-behind (no separation) — last resort only

Not a named academic pattern — the term describes logic living directly
in the View's own class file (the historical, canonical example is
ASP.NET Web Forms' `.aspx.cs` code-behind files), with no Model/View
separation at all: event handlers read and write UI controls directly
and call into data access inline.

| | |
|---|---|
| **Pros** | Zero indirection — for a screen genuinely trivial enough that no logic will ever need testing independent of the UI (a static informational page, a one-off generated/scaffolded admin screen), the ceremony of any of the three patterns above buys nothing real. |
| **Cons** | Untestable without mounting the UI; logic and presentation tangle together immediately as soon as a screen grows past "trivial," and there is no natural seam to split them later without a rewrite. This is the fallback of last resort, not a starting point — reach for it only when a screen is simple enough that none of the three real separations above would do anything but add files. |

## Recommendation

**MVVM → MVP → MVC → code-behind**, in that order, choosing the first
option in the list the target UI technology can actually support well.
This project's own entity views and Vue application shell (`ADR-039`)
sit at the top of the list and stay there — this priority order exists
for situations `ADR-039` doesn't already dictate: a future native screen
on a UI technology without a real binding substrate steps down to MVP,
not straight to code-behind; a genuinely request/response-shaped
server-rendered page (if this design ever grows one) is MVC's honest
home, not a reason to avoid data binding elsewhere. **Command binding
over inline logic/code-behind is the constant across every step of this
list except the last** — even MVP's explicit interface-method style is
still "the View delegates to a named command," not logic embedded
directly in a click handler; code-behind is what's left once that's no
longer worth doing at all, and should be reached for only there.
