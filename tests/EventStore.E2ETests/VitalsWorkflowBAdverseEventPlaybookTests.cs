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
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "site-coordinator", "capture-and-review-adverse-event.md"));

        await _page.GotoAsync(_clientWebVitalsAdverseEventBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Vitals-AdverseEvent instance of the Duplex Client -- a dedicated client-web instance launch-configured (ADR-039) to subscribe to the trial1 AdverseEventReported event type.");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Browse" }).ClickAsync();
        // ADR-099 -- EntityBrowser now paginates (page size 10); the filter box
        // is the way to reach a specific row once the simulator has pushed
        // more than a page of entities in front of it (found by actually
        // running this playbook, not assumed).
        await _page.GetByTestId("entity-browser-filter").FillAsync("ae-1042");
        var aeRow = _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "ae-1042" });
        // See VitalsWorkflowAPlaybookTests.cs's identical comment -- waits
        // for the debounced server-side "contains" filter to actually take
        // effect (table settles to 1 row), not just for the target row's
        // own visibility, which can be trivially true before that happens.
        await Assertions.Expect(_page.Locator("tbody tr")).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await Assertions.Expect(aeRow).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Switching to the Browse tab. The seed continuity adverse event ae-1042 (Samples.Vitals.Seed) -- the same AeId the feature doc's own worked example uses -- is already present via REPLAY-mode catch-up.");

        await aeRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        var fallbackView = _page.GetByLabel("Entity (generic fallback view)");
        await Assertions.Expect(templatedView.Or(fallbackView)).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Selecting ae-1042 opens its Detail view -- rendered generically via GenericFallbackView (ADR-039), since no ViewDefinition is registered for the \"adverseevent\" EntityType. Shows the reported severity (Severe) and SeriousAdverseEvent flag (true) -- this playbook stops at capture; the delegated secondary-opinion review and investigator sign-off this workflow's own feature doc describes (ADR-043/ADR-066) have no dedicated client-web screen yet, only the same generic property view every entity gets.");

        const string sequenceDiagram = """
            @startuml VitalsAdverseEvent_Playbook_Sequence
            autonumber
            actor "Site coordinator" as coordinator
            actor "Colleague\n(delegated 'secondary opinion')" as colleague
            actor "Principal Investigator" as pi
            participant "PublishEndpoint\n(Inbox)" as inbox
            database "Entity Store\n(trial1:AdverseEvent:ae-1042)" as entityStore

            coordinator -> inbox: POST /publish/AdverseEventReported\n{ AeId: "ae-1042", SubjectId: "S-0091",\n  Severity: "Severe", SeriousAdverseEvent: true }

            alt ordinary publish (this playbook's own seed data)
              inbox -> entityStore: fold (Full) immediately, AuthorityStatus: "accepted"
            else non-authoritative capture, pending clinical judgment (ADR-035/042)
              inbox -> inbox: AuthorityStatus: "pending_review"\n(ReviewPending: true, reason: "clinical-judgment-required")
              note right: not exercised by this playbook's own seed data
              pi -> inbox: POST /publish/accessGrant\n{ GranteeDid, DelegatedClaim: "review:secondary-opinion",\n  EntityScope: "trial1:AdverseEvent:ae-1042" } (ADR-043)
              colleague -> inbox: reviews the pending finding via a delegated,\nentity-scoped read (ADR-043) -- never blanket access
              pi -> inbox: POST /publish/authorityDecision\n{ targetEventId, decision: "accepted"|"rejected",\n  decidingActorId }\nRequiredClaims: "review:ae"; step-up gated (ADR-066)
              inbox -> entityStore: fold now (catch-up) if accepted;\nEntity Store left untouched if rejected (ADR-042)
            end
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Vitals -- Workflow B: Adverse Event Capture and Review", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "site-coordinator", "capture-and-review-adverse-event.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
