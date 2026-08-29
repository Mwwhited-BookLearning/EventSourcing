using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Closes half of TODO.md's "UI-playbook coverage is capped by ADR-039's
// one-event-type-per-instance model" entry: EventStore.AppHost/AppHost.cs
// now runs a dedicated client-web-vitals-device instance (VITE_EVENT_TYPE
// DeviceOnboarded, VITE_ENTITY_TYPE device) alongside the existing
// PatientScreened-subscribed client-web-vitals, so Workflow B's Device
// entity (dev-0091, Samples.Vitals.Seed) is finally Browse-reachable --
// it never was from client-web-vitals itself, confirmed by reading
// subscriptionBuilder.ts (a GraphQL Subscription field is built per
// (AppId, EventType), never per EntityType). This covers Workflow B's
// upstream half only (Device Onboarding and Continuous Monitoring) --
// its downstream half (Adverse Event Capture and Review) has no seed
// data published for the AdverseEvent entity type at all yet, a
// separate, still-open gap (TODO.md).
[TestClass]
public class VitalsWorkflowBDeviceOnboardingPlaybookTests
{
    private static DistributedApplication _app = null!;
    private static string _clientWebVitalsDeviceBaseUrl = null!;
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
            .WaitForResourceAsync("client-web-vitals-device", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var httpClient = _app.CreateHttpClient("client-web-vitals-device", "http");
        _clientWebVitalsDeviceBaseUrl = httpClient.BaseAddress!.ToString().TrimEnd('/');

        using var pollClient = new HttpClient();
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await pollClient.GetAsync(_clientWebVitalsDeviceBaseUrl);
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
    public async Task RecordDeviceOnboardingAndContinuousMonitoringPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "site-coordinator", "onboard-monitoring-device.md"));

        await _page.GotoAsync(_clientWebVitalsDeviceBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Vitals-Device instance of the Duplex Client -- a second, dedicated client-web instance launch-configured (ADR-039) to subscribe to the trial1 DeviceOnboarded event type, since the original client-web-vitals instance is locked to PatientScreened and can never Browse a Device entity.");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Browse" }).ClickAsync();
        // ADR-099 -- EntityBrowser now paginates (page size 10); the filter box
        // is the way to reach a specific row once the simulator has pushed
        // more than a page of entities in front of it (found by actually
        // running this playbook, not assumed).
        await _page.GetByTestId("entity-browser-filter").FillAsync("dev-0091");
        var deviceRow = _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "dev-0091" });
        await Assertions.Expect(deviceRow).ToBeVisibleAsync(new() { Timeout = 30_000 }); // REPLAY-mode subscription needs a moment to catch up on first load
        await recorder.RecordStepAsync(_page, "Switching to the Browse tab. The seed continuity device dev-0091 (Samples.Vitals.Seed), paired to patient S-0091, is already present via REPLAY-mode catch-up.");

        await deviceRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        var fallbackView = _page.GetByLabel("Entity (generic fallback view)");
        await Assertions.Expect(templatedView.Or(fallbackView)).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Selecting dev-0091 opens its Detail view -- rendered generically via GenericFallbackView (ADR-039), since no ViewDefinition is registered for the \"device\" EntityType. Shows the device model (NIM-Eclipse), interface kind (IONM), the patient it's paired to (S-0091), and its site.");

        const string sequenceDiagram = """
            @startuml VitalsDeviceOnboarding_Playbook_Sequence
            autonumber
            actor "Site staff" as user
            participant "PublishEndpoint\n(Inbox)" as inbox
            participant "Duplex Client\n(client-web-vitals-device)" as client
            database "Entity Store\n(trial1:Device:dev-0091)" as entityStore

            user -> inbox: POST /publish/DeviceOnboarded\n{ DeviceId: "dev-0091", DeviceModel: "NIM-Eclipse",\n  InterfaceKind: "IONM", PairedToSubjectId: "S-0091", SiteId }
            inbox -> entityStore: fold (Full), AuthorityStatus: "accepted"\n(no RequiredClaims -- ordinary Bearer token, events:publish)

            note over user, entityStore
              Continuous monitoring itself (the device's live telemetry
              stream, ADR-031) has no client-web UI -- this playbook
              covers device pairing/metadata only. The paired continuous
              stream feeds Workflow D's own IONM alert detection instead
              (see that playbook's own diagram).
            end note

            == Later: staff browse the result via client-web ==
            client -> entityStore: (via GraphQL Subscription on_trial1_DeviceOnboarded, REPLAY)
            client -> user: Detail view -- rendered generically\n(GenericFallbackView, no ViewDefinition registered for "device")
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Vitals -- Workflow B: Device Onboarding and Continuous Monitoring", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "site-coordinator", "onboard-monitoring-device.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
