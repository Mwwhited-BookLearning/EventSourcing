using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Closes the first of TODO.md's three remaining UI-playbook gaps:
// Samples.Vitals.Seed now publishes an AdverseEventReported event (it
// never did before this pass), and EventStore.AppHost runs a dedicated
// client-web-vitals-adverseevent instance (VITE_EVENT_TYPE
// AdverseEventReported, VITE_ENTITY_TYPE adverseevent) so the resulting
// AdverseEvent entity (ae-1042) is Browse-reachable -- same reasoning as
// VitalsWorkflowBDeviceOnboardingPlaybookTests's own header comment for
// why a dedicated instance is required at all (ADR-039's
// one-event-type-per-instance model).
[TestClass]
public class VitalsWorkflowBAdverseEventPlaybookTests
{
    private static DistributedApplication _app = null!;
    private static string _clientWebVitalsAdverseEventBaseUrl = null!;
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
            .WaitForResourceAsync("client-web-vitals-adverseevent", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var httpClient = _app.CreateHttpClient("client-web-vitals-adverseevent", "http");
        _clientWebVitalsAdverseEventBaseUrl = httpClient.BaseAddress!.ToString().TrimEnd('/');

        using var pollClient = new HttpClient();
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await pollClient.GetAsync(_clientWebVitalsAdverseEventBaseUrl);
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
    public async Task RecordAdverseEventCaptureAndReviewPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "workflow-b-adverse-event-capture-and-review.md"));

        await _page.GotoAsync(_clientWebVitalsAdverseEventBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Vitals-AdverseEvent instance of the Duplex Client -- a dedicated client-web instance launch-configured (ADR-039) to subscribe to the trial1 AdverseEventReported event type.");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Browse" }).ClickAsync();
        var aeRow = _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "ae-1042" });
        await Assertions.Expect(aeRow).ToBeVisibleAsync(new() { Timeout = 30_000 }); // REPLAY-mode subscription needs a moment to catch up on first load
        await recorder.RecordStepAsync(_page, "Switching to the Browse tab. The seed continuity adverse event ae-1042 (Samples.Vitals.Seed) -- the same AeId the feature doc's own worked example uses -- is already present via REPLAY-mode catch-up.");

        await aeRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        var fallbackView = _page.GetByLabel("Entity (generic fallback view)");
        await Assertions.Expect(templatedView.Or(fallbackView)).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Selecting ae-1042 opens its Detail view -- rendered generically via GenericFallbackView (ADR-039), since no ViewDefinition is registered for the \"adverseevent\" EntityType. Shows the reported severity (Severe) and SeriousAdverseEvent flag (true) -- this playbook stops at capture; the delegated secondary-opinion review and investigator sign-off this workflow's own feature doc describes (ADR-043/ADR-066) have no dedicated client-web screen yet, only the same generic property view every entity gets.");

        await recorder.WriteMarkdownAsync("Vitals -- Workflow B: Adverse Event Capture and Review");

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "workflow-b-adverse-event-capture-and-review.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
