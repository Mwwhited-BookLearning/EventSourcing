using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Second half of Meridian's Workflow A (see
// MeridianWorkflowADocumentAndBiometricCapturePlaybookTests's own header
// for the full reasoning on why applicant-1001 is the one browsable
// entity this instance can reach). This playbook's narrative focus is
// the downstream half of the same workflow: the applicant's self-attested
// DID/UCAN identity claim (ADR-036) reviewed into an accepted,
// claims-bearing identity record (ADR-035/ADR-042/ADR-046) -- the same
// browse-to-detail mechanism, a distinct feature doc and caption focus,
// per this project's own one-feature-doc-per-playbook-file convention.
[TestClass]
public class MeridianWorkflowACustomerOnboardingPlaybookTests
{
    private static DistributedApplication _app = null!;
    private static string _clientWebMeridianBaseUrl = null!;
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
            .WaitForResourceAsync("client-web-meridian", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var httpClient = _app.CreateHttpClient("client-web-meridian", "http");
        _clientWebMeridianBaseUrl = httpClient.BaseAddress!.ToString().TrimEnd('/');

        using var pollClient = new HttpClient();
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await pollClient.GetAsync(_clientWebMeridianBaseUrl);
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
    public async Task RecordCustomerOnboardingAndIdentityVerificationPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "workflow-a-customer-onboarding-and-identity-verification.md"));

        await _page.GotoAsync(_clientWebMeridianBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Meridian instance of the Duplex Client, launch-configured (ADR-039) to subscribe to the kyc IdentityClaimSubmitted event type -- the self-attested DID/UCAN claim this workflow's downstream review acts on.");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Browse" }).ClickAsync();
        var applicantRow = _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "applicant-1001" });
        await Assertions.Expect(applicantRow).ToBeVisibleAsync(new() { Timeout = 30_000 }); // REPLAY-mode subscription needs a moment to catch up on first load
        await recorder.RecordStepAsync(_page, "The Browse tab shows applicant-1001, whose IdentityClaimSubmitted event carries its self-attested did:key DID alongside the claimed legal name and date of birth (ADR-036).");

        await applicantRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        // Briefly renders GenericFallbackView before loadViewDefinition's async
        // GraphQL round trip resolves (~500ms, measured directly) -- wait for
        // the real templated render (see MeridianWorkflowADocumentAndBiometric
        // CapturePlaybookTests's own comment for the full correction history).
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        await Assertions.Expect(templatedView).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "The Detail view, rendered from the registered ApplicantIdentity ViewDefinition, shows the IdentityClaimSubmitted payload itself (masked ClaimedLegalName/DateOfBirth, ADR-009) plus AuthorityStatus: accepted, this workflow's own end state: an analyst's review (ADR-035/ADR-042/ADR-046) has accepted the self-attested claim into the record.");

        await recorder.WriteMarkdownAsync("Meridian -- Workflow A: Customer Onboarding and Identity Verification");

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "workflow-a-customer-onboarding-and-identity-verification.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
