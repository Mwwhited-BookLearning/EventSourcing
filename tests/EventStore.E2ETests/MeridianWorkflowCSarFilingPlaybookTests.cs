using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Closes the SAR-escalation half of Meridian's Workflow C: Samples.Meridian.
// Seed now performs a real step-up-authenticated SarFilingRecorded publish
// (a compliance officer's ClaimsPrincipal carrying "acr"/"auth_time"
// directly -- the exact mechanism MeridianWorkflowCScenarioAssertions.cs's
// own "AfterSteppingUp..." test proves, satisfied here without a real
// DevIdp round trip since this seeder talks to PublishService in-process,
// same posture as every other seed publish). A dedicated
// client-web-meridian-sarfiling instance (SarFilingRecorded, VITE_ENTITY_TYPE
// applicantidentity) makes the resulting event Browse-reachable.
[TestClass]
public class MeridianWorkflowCSarFilingPlaybookTests
{
    private static DistributedApplication _app = null!;
    private static string _clientWebMeridianSarFilingBaseUrl = null!;
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
            .WaitForResourceAsync("client-web-meridian-sarfiling", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var httpClient = _app.CreateHttpClient("client-web-meridian-sarfiling", "http");
        _clientWebMeridianSarFilingBaseUrl = httpClient.BaseAddress!.ToString().TrimEnd('/');

        using var pollClient = new HttpClient();
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await pollClient.GetAsync(_clientWebMeridianSarFilingBaseUrl);
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
    public async Task RecordSarFilingPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "compliance-officer", "file-sar.md"));

        await _page.GotoAsync(_clientWebMeridianSarFilingBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Meridian-SarFiling instance of the Duplex Client -- a dedicated client-web instance launch-configured (ADR-039) to subscribe to the kyc SarFilingRecorded event type.");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Browse" }).ClickAsync();
        // ADR-099 -- EntityBrowser now paginates (page size 10); the filter box
        // is the way to reach a specific row once the simulator has pushed
        // more than a page of entities in front of it (found by actually
        // running this playbook, not assumed).
        await _page.GetByTestId("entity-browser-filter").FillAsync("applicant-1001");
        var applicantRow = _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "applicant-1001" });
        // See VitalsWorkflowAPlaybookTests.cs's identical comment -- waits
        // for the debounced server-side "contains" filter to actually take
        // effect (table settles to 1 row), not just for the target row's
        // own visibility, which can be trivially true before that happens.
        await Assertions.Expect(_page.Locator("tbody tr")).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await Assertions.Expect(applicantRow).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Switching to the Browse tab. The seed's SAR filing for applicant-1001 (Samples.Meridian.Seed) is already present via REPLAY-mode catch-up -- a compliance officer's step-up-authenticated publish, gated by MeridianWorkflowC.cs's RequiredSignature: [\"urn:kyc:acr:step-up\"] (RFC 9470, ADR-066).");

        await applicantRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        await Assertions.Expect(templatedView).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Selecting applicant-1001 opens its Detail view. The registered ApplicantIdentity template now covers this event type's own fields too (TODO.md's ViewDefinition/payload-shape mismatch, fixed by extending the one shared template): Filing Reference ID (SAR-2026-00417) renders plainly, and Narrative renders masked (\"***\") -- a live demonstration of x-masking on this specific field (MeridianWorkflowC.cs's own requiredClaim: identity:aml-review), not a rendering gap.");

        const string sequenceDiagram = """
            @startuml MeridianSarFiling_Playbook_Sequence
            autonumber
            actor "Compliance officer\n(identity:aml-review)" as officer
            participant "PublishEndpoint\n(Inbox)" as inbox
            database "Event Log" as eventLog
            database "Entity Store\n(kyc:ApplicantIdentity)" as entityStore

            officer -> inbox: POST /publish/SarFilingRecorded\n{ ApplicantId, TargetScreeningEventId,\n  FilingReferenceId, Narrative }\nBearer <JWT, acr not recent enough>
            alt caller's token doesn't satisfy RequiredSignature.AcrValues/MaxAge\n(this playbook's own seed data starts here)
              inbox --> officer: 401 WWW-Authenticate: step-up required\n(acr_values="urn:kyc:acr:step-up")
              officer -> officer: re-authenticate (IdP's own mechanism, ADR-066) --\nSamples.Meridian.Seed simulates this by attaching\n"acr"/"auth_time" claims directly, no real DevIdp round trip
              officer -> inbox: retry POST /publish/SarFilingRecorded\n(same payload, stepped-up token, Meaning: "approved filing")
            end
            inbox -> eventLog: INSERT StoredEvent (SarFilingRecorded)\nSignature: { SignerId, SignedAt, Meaning: "approved filing",\n  Acr: "urn:kyc:acr:step-up" }
            inbox -> entityStore: fold (Partial) onto ApplicantIdentity,\nAuthorityStatus: "accepted"
            note right: the actual FinCEN BSA E-Filing submission is out of scope\n(ADR-072's IInterchangeFormatAdapter seam, not built here)

            == Later: staff browse the result via client-web ==
            participant "Duplex Client\n(client-web-meridian-sarfiling)" as client
            client -> entityStore: (via GraphQL Subscription on_kyc_SarFilingRecorded, REPLAY)
            client -> officer: Detail view -- FilingReferenceId renders plainly;\nNarrative renders masked ("***", x-masking requiredClaim\n"identity:aml-review")
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Meridian -- Workflow C: SAR Filing", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "compliance-officer", "file-sar.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
