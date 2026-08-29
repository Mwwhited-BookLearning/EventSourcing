using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// ADR-055's own named-but-never-built EventStore.E2ETests project, plus
// the playbook-generation extension (TODO.md, direct request). Boots the
// REAL EventStore.AppHost (Postgres, migrator, DevIdp, eventstore, and
// the three client-web Vite instances) via Aspire.Hosting.Testing rather
// than assuming a human already has `aspire run` going -- `dotnet test`
// alone regenerates this playbook end to end.
//
// Playwright's own browser/page lifecycle is managed by hand here (not
// via Microsoft.Playwright.MSTest's PageTest base class -- see this
// project's own .csproj comment for why that package's pinned MSTest
// 2.2.7/old VSTest Test SDK genuinely conflicts with this repo's MSTest
// 4.3.3 Microsoft.Testing.Platform engine everywhere else, confirmed by
// actually running it and finding zero tests discovered).
//
// Walks Vitals' real Workflow A (docs/domains/clinical-trials-device-
// telemetry/README.md) against the continuity subject vitals-seed always
// publishes (S-0091, Samples.Vitals.Seed/Program.cs) -- confirmed present
// after every AppHost run, so this test never needs to publish its own
// setup data first.
[TestClass]
public class VitalsWorkflowAPlaybookTests
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

        // client-web-vitals depends (via WaitFor) on eventstore, which
        // itself depends on migrator/vitals-seed/meridian-seed completing
        // first -- waiting on client-web-vitals alone is enough to know
        // the whole chain beneath it already finished.
        await resourceNotificationService
            .WaitForResourceAsync("client-web-vitals", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var httpClient = _app.CreateHttpClient("client-web-vitals", "http");
        _clientWebVitalsBaseUrl = httpClient.BaseAddress!.ToString().TrimEnd('/');

        // "Running" means the process started, not that Vite's own dev
        // server socket is necessarily accepting connections yet -- poll
        // until it actually responds rather than assuming the state
        // transition alone is sufficient (found necessary by actually
        // running this against a cold AppHost start, not assumed).
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
    public async Task RecordPatientEnrollmentAndInformedConsentPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "site-coordinator", "enroll-and-review-patient.md"));

        await _page.GotoAsync(_clientWebVitalsBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Vitals instance of the Duplex Client. It's already subscribed to the trial1 PatientScreened event type (ADR-039's own per-instance launch configuration).");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Browse" }).ClickAsync();
        // ADR-099 -- EntityBrowser now paginates (page size 10); the filter box
        // is the way to reach a specific row once the simulator has pushed
        // more than a page of entities in front of it (found by actually
        // running this playbook, not assumed).
        await _page.GetByTestId("entity-browser-filter").FillAsync("S-0091");
        var patientRow = _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "S-0091" });
        await Assertions.Expect(patientRow).ToBeVisibleAsync(new() { Timeout = 30_000 }); // REPLAY-mode subscription needs a moment to catch up on first load
        await recorder.RecordStepAsync(_page, "Switching to the Browse tab. The continuity subject S-0091 (Samples.Vitals.Seed) is already present -- REPLAY mode (not TAIL) means the full historical PatientScreened stream is caught up on first subscribe, not just new arrivals.");

        await patientRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        // The Detail view briefly renders GenericFallbackView (ADR-039) before
        // useEntityViewActions.loadViewDefinition's own async GraphQL round
        // trip resolves, ~500ms later in practice -- confirmed by direct
        // measurement, not assumed (an earlier pass of this playbook screenshot
        // that transient fallback and mistakenly wrote it up as the entity type
        // having "no ViewDefinition registered," which was never true: the
        // seeder registers one, it just hadn't loaded yet when the shot was
        // taken). Wait for the real templated render before capturing.
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        await Assertions.Expect(templatedView).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Selecting S-0091 from the browser opens its Detail view, rendered from the Patient Detail ViewDefinition Samples.Vitals.Seed registers (ADR-039) -- subject/site/protocol IDs, eligibility status, and the masked legal name/date of birth fields (translated via translations.ts's placeholderTranslations, ADR-087).");

        const string sequenceDiagram = """
            @startuml VitalsWorkflowA_Playbook_Sequence
            autonumber
            actor "Site staff" as user
            participant "Duplex Client\n(client-web-vitals)" as client
            participant "GraphQL Subscription\n(on_trial1_PatientScreened)" as graphql
            database "Event Log" as eventLog

            user -> client: open client-web-vitals
            client -> graphql: subscription (mode: REPLAY)\nBearer <follower-client token, events:follow>
            graphql -> eventLog: SELECT PatientScreened events\nWHERE AppId="trial1" ORDER BY SequenceNumber
            eventLog --> graphql: PatientScreened{S-0091,\n LegalName (masked), DateOfBirth (masked), ...}
            graphql --> client: SSE stream (REPLAY catch-up, then TAIL)
            client -> client: fold into local entity cache\n(IndexedDB, per-instance, ADR-039)
            user -> client: Browse tab -> select S-0091
            client -> user: Detail view (Patient Detail ViewDefinition)\nLegalName/DateOfBirth render "REDACTED"\n(x-masking, requiredClaim "clearance:phi")

            alt caller's own token instead holds "clearance:phi"
              client -> user: LegalName/DateOfBirth render their real values
            end
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Vitals -- Workflow A: Patient Enrollment and Informed Consent", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "site-coordinator", "enroll-and-review-patient.md")));
    }

    // Walks up from the test assembly's own output directory to find the
    // repo root (identified by EventStore.slnx) -- avoids a hardcoded
    // absolute path, which would break for anyone who clones this repo
    // somewhere other than this exact machine's own path.
    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
