using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// TODO.md, direct request: "style-guide.md describing how client-web's
// UI/UX should work... either as PlantUML+Salt mockups or as real pages
// captured via a Playwright script that keeps the file updated (this
// project's own established PlaybookRecorder mechanism, reused)." Real
// pages, not mockups: ADR-099 already built the actual target UI, so a
// hand-drawn Salt mockup would just be a less accurate second copy of
// something that already exists and runs. Reuses PlaybookRecorder exactly
// (see its own PlaybookRecorder.AddSection addition) rather than a
// bespoke writer -- the only new thing this file needed was prose-only
// sections with no screenshot of their own, which that one small addition
// covers without forking a second markdown assembler.
//
// Deliberately one client-web instance (client-web-vitals): its own
// left-nav already exposes Detail/Browse/Compose/Queue/Lineage (every
// Naive UI component family this app uses except the Meridian-only
// Relying-Party panel, which is the same n-card/n-form/n-button
// primitives already captured elsewhere, not a new pattern) -- no need to
// juggle a second instance's own base URL for one non-distinct screen.
[TestClass]
public class StyleGuideTests
{
    private static DistributedApplication _app = null!;
    private static string _clientWebVitalsBaseUrl = null!;
    private static IPlaywright _playwright = null!;
    private static IBrowser _browser = null!;
    private IPage _page = null!;

    [ClassInitialize]
    public static async Task ClassInitAsync(TestContext _)
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EventStore_AppHost>();
        _app = await appHost.BuildAsync();

        var resourceNotificationService = _app.Services.GetRequiredService<ResourceNotificationService>();
        await _app.StartAsync();

        await resourceNotificationService
            .WaitForResourceAsync("client-web-vitals", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var httpClient = _app.CreateHttpClient("client-web-vitals", "http");
        _clientWebVitalsBaseUrl = httpClient.BaseAddress!.ToString().TrimEnd('/');

        using var pollClient = new HttpClient();
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await pollClient.GetAsync(_clientWebVitalsBaseUrl);
                if (response.IsSuccessStatusCode) break;
            }
            catch (HttpRequestException) { }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanupAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
        await _app.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitAsync()
    {
        _page = await _browser.NewPageAsync();
    }

    [TestCleanup]
    public async Task TestCleanupAsync()
    {
        await _page.CloseAsync();
    }

    [TestMethod]
    public async Task RecordStyleGuide()
    {
        var recorder = new PlaybookRecorder(Path.Combine(RepoRootDirectory(), "docs", "style-guide.md"));

        await _page.GotoAsync(_clientWebVitalsBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Left-hand navigation shell (ADR-099): n-config-provider + n-layout/n-layout-sider/n-menu replaces the old top tab-button row. n-menu's own items are render-function RouterLinks -- real <a href> elements, not a click handler -- so deep-linking and screen-reader link semantics both work. Sidebar collapse state is a per-viewer localStorage convenience, not app state.");

        recorder.AddSection("Design tokens and theming", """
            One source of truth, `client-web/packages/mvvm-client/src/theme/tokens.ts`
            (`docs/patterns/mvvm-client-architecture.md`'s "Styling (shared theme)"
            rule): `--duplex-border`/`--duplex-bg`/`--duplex-fg`/`--duplex-flag-active`
            as plain CSS custom properties, applied once in `App.vue`, plus a derived
            `themeOverrides` object (`common.borderColor`/`bodyColor`/`textColorBase`)
            fed straight into Naive UI's own `n-config-provider` at the app root. No
            component overrides its own colors, spacing, or radii locally -- a value
            that needs to change, changes in `tokens.ts` and propagates everywhere,
            both to Naive UI's own components and to the handful of plain-HTML
            elements (see "Property tables," below) that predate the Naive UI
            adoption and still read the same CSS custom properties directly.
            """);

        await _page.GetByRole(AriaRole.Link, new() { Name = "Browse" }).ClickAsync();
        await Assertions.Expect(_page.GetByTestId("entity-browser-filter")).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Data tables with pagination (n-data-table, ADR-099): EntityBrowser and both Queue components use Naive UI's own n-data-table with its built-in pagination prop (page size 10) over the client's already-loaded entity cache. This bounds render/DOM cost per page but does NOT reduce what crosses the wire -- REPLAY-mode subscriptions already streamed every matching event before any grid renders a row (TODO.md tracks the real, separate fix: a genuine paged/cursor GraphQL query). The filter box (added once pagination made a specific seed row like S-0091 unreachable past page 1 -- a real regression, not a hypothetical) is the way to reach a specific row directly instead of paging to it.");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Compose" }).ClickAsync();
        // Wait for the real, loaded form (the "select" only renders once
        // EventComposer's own `loaded` ref flips true) -- the heading alone
        // is present even during "Loading registered event types...", which
        // the first capture attempt caught on camera by only waiting for that
        // heading (found by actually reviewing the screenshot, not assumed).
        await Assertions.Expect(_page.Locator("select")).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Forms (n-form/n-input/n-checkbox, ADR-099): the Event Composer uses Naive UI's own form components throughout, with one deliberate exception -- the event-type picker stays a native <select> rather than n-select, since n-select's teleported dropdown would have needed a disproportionate rewrite of its own test's find('select').setValue() interaction for no real UX gain on a short, keyboard-native list. n-form-item's own label has no functional `for` association in this installed Naive UI version (found via a11y.spec.ts, run for real) -- every input carries an explicit aria-label alongside its visible label rather than relying on implicit association.");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Lineage & Playback" }).ClickAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading).First).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Cards and panels (n-card, ADR-099): RelyingPartyAccessPanel, LineageExportAndPlaybackPanel, BitemporalPlaybackControl, and OfflineBundleViewer all use n-card/n-form-item/n-button/n-alert as their shared chrome. Every card uses a real <h2>/<h3> for its heading, never n-card's own `title` prop -- that prop renders content wrapped in `role=\"heading\"` with no `aria-level` in this installed version, a genuine axe-core \"aria-required-attr\" critical violation found by actually running a11y.spec.ts against a real rendered n-card, not assumed from the library's own docs.");

        recorder.AddSection("Property tables (deliberately still plain HTML)", """
            `GenericFallbackView`'s own property list (any entity with no registered
            `ViewDefinition`, ADR-039) is wrapped in an `n-card` for chrome, but the
            table itself is a plain, hand-styled `<table>` with `<th scope="row">` for
            each property name -- deliberately NOT swapped for `n-descriptions` during
            the ADR-099 restyle. Reasoning worth keeping, not just the choice itself:
            `<th scope="row">` is what makes a screen reader announce a value together
            with its own label ("carrier: UPS," not two anonymous cells), a real,
            already-hard-won accessibility property confirmed by reasoning about actual
            screen-reader behavior, not something axe-core's automated ruleset can
            verify on its own (it doesn't flag a headerless two-column table, since
            nothing automated can tell whether a column is semantically a label). Swap
            this for a Naive UI component only after verifying its exact DOM output
            preserves the same row/label pairing -- don't assume equivalence.
            """);

        recorder.AddSection("Accessibility baseline", """
            `ADR-073`'s own exit criterion (automated `axe-core` conformance PLUS a
            real screen-reader pass, never automated checks alone) is what actually
            caught every Naive UI gotcha this restyle hit -- all found by running
            `a11y.spec.ts` for real against real rendered markup, none of them
            predictable from Naive UI's own documentation:
            - `n-card`'s `title`/`header` prop → critical `aria-required-attr`
              violation (`role="heading"` with no `aria-level`). Use a real
              `<h2>`/`<h3>` inside the card instead.
            - `aria-label` on `n-card`'s own roleless root `<div>` → `aria-prohibited-
              attr`. Put the label on a real landmark (a `<section>`/`<article>`
              wrapping the card) instead.
            - `n-form-item`'s `label` prop has no functional `for` association in this
              installed version -- pair every input with an explicit `aria-label` of
              its own rather than relying on implicit label association.

            New components should run through `a11y.spec.ts` (or an equivalent real
            render + `axe-core` check) before being considered done, not spot-checked
            visually -- every one of the three gotchas above looked completely correct
            on screen.
            """);

        await recorder.WriteMarkdownAsync("Duplex Client — UI/UX Style Guide");

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "style-guide.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
