using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Closes the other half of TODO.md's "UI-playbook coverage is capped by
// ADR-039's one-event-type-per-instance model" entry: a dedicated
// client-web-vitals-ionmalert instance (VITE_EVENT_TYPE IonmAlertRaised,
// VITE_ENTITY_TYPE ionmalert) makes Workflow D's IonmAlert entity
// (alert-0091, Samples.Vitals.Seed) Browse-reachable, same reasoning as
// VitalsWorkflowBDeviceOnboardingPlaybookTests's own header comment.
[TestClass]
public class VitalsWorkflowDIntraoperativeMonitoringPlaybookTests
{
    private static DistributedApplication _app = null!;
    private static string _clientWebVitalsIonmAlertBaseUrl = null!;
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
            .WaitForResourceAsync("client-web-vitals-ionmalert", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var httpClient = _app.CreateHttpClient("client-web-vitals-ionmalert", "http");
        _clientWebVitalsIonmAlertBaseUrl = httpClient.BaseAddress!.ToString().TrimEnd('/');

        using var pollClient = new HttpClient();
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await pollClient.GetAsync(_clientWebVitalsIonmAlertBaseUrl);
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
    public async Task RecordIntraoperativeMonitoringAndAlertResponsePlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "neurotechnologist", "monitor-and-respond-to-alert.md"));

        await _page.GotoAsync(_clientWebVitalsIonmAlertBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Vitals-IonmAlert instance of the Duplex Client -- a dedicated client-web instance launch-configured (ADR-039) to subscribe to the trial1 IonmAlertRaised event type, since neither the PatientScreened- nor DeviceOnboarded-subscribed instances can Browse an IonmAlert entity.");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Browse" }).ClickAsync();
        // ADR-099 -- EntityBrowser now paginates (page size 10); the filter box
        // is the way to reach a specific row once the simulator has pushed
        // more than a page of entities in front of it (found by actually
        // running this playbook, not assumed).
        await _page.GetByTestId("entity-browser-filter").FillAsync("alert-0091");
        var alertRow = _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "alert-0091" });
        await Assertions.Expect(alertRow).ToBeVisibleAsync(new() { Timeout = 30_000 }); // REPLAY-mode subscription needs a moment to catch up on first load
        await recorder.RecordStepAsync(_page, "Switching to the Browse tab. The seed continuity alert alert-0091 (Samples.Vitals.Seed), raised for patient S-0091's IONM stream, is already present via REPLAY-mode catch-up.");

        await alertRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        var fallbackView = _page.GetByLabel("Entity (generic fallback view)");
        await Assertions.Expect(templatedView.Or(fallbackView)).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Selecting alert-0091 opens its Detail view -- rendered generically via GenericFallbackView (ADR-039), since no ViewDefinition is registered for the \"ionmalert\" EntityType. This subscription's own payload type is IonmAlertRaised's, so the finding (SSEP amplitude decrease) and severity (High) render here -- IonmAlertAcknowledged's own AckedBy field isn't part of this payload shape, even though both events fold onto the same entity in the Entity Store itself (ADR-094's expected-response tracking).");

        const string sequenceDiagram = """
            @startuml VitalsIonmAlert_Playbook_Sequence
            autonumber
            actor "Neurotechnologist" as tech
            actor "Attending neurologist" as neurologist
            participant "PublishEndpoint\n(Inbox)" as inbox
            participant "ExpectedResponseWatcher" as watcher
            participant "Duplex Client\n(client-web-vitals-ionmalert)" as client
            database "Entity Store\n(trial1:IonmAlert:alert-0091)" as entityStore

            tech -> inbox: POST /publish/IonmAlertRaised\n{ AlertId: "alert-0091", SubjectId: "S-0091",\n  Finding: "SSEP amplitude decrease", Severity: "High" }\nExpectedResponse: IonmAlertAcknowledged within 2 minutes (ADR-094)
            inbox -> entityStore: fold (Partial), AuthorityStatus: "accepted"
            watcher -> watcher: start a 2-minute deadline timer\nfor this event's own RespondsToEventId chain

            alt acknowledged within 2 minutes (this playbook's own seed data)
              neurologist -> inbox: POST /publish/IonmAlertAcknowledged\n{ AlertId: "alert-0091", AckedBy: "neurologist-1" }\nRespondsToEventId: <the IonmAlertRaised event's own EventId>
              inbox -> entityStore: fold (Partial) onto the SAME IonmAlert entity
              watcher -> watcher: deadline satisfied -- no further action
            else no IonmAlertAcknowledged within the 2-minute deadline
              watcher -> inbox: publish reserved "ExpectedResponseMissing" event\n(system-owned, no RequiredClaims)
              inbox -> entityStore: fold as an ordinary Follow-able fact
              note right: the domain's own escalation process (paging a\nbackup, sounding a different alarm) reacts to this\nthe same way it already reacts to ChannelLagDetected
            end

            == Later: staff browse the result via client-web ==
            client -> entityStore: (via GraphQL Subscription on_trial1_IonmAlertRaised, REPLAY)
            client -> tech: Detail view shows Finding/Severity (this subscription's\nown IonmAlertRaised payload) -- AckedBy isn't part of this\npayload shape, even though it's folded into the same entity
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Vitals -- Workflow D: Intraoperative Monitoring and Alert Response", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "neurotechnologist", "monitor-and-respond-to-alert.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
