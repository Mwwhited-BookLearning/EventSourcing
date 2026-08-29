using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Covers the periodic-screening half of Meridian's Workflow C only.
// Samples.Meridian.Seed already publishes SanctionsScreeningPerformed for
// applicant-1001 (folded onto the same ApplicantIdentity entity Workflow
// A's own playbooks browse), but the existing client-web-meridian
// instance's subscription is fixed to IdentityClaimSubmitted, so none of
// this event's own fields (ScreeningDate, ListsChecked, MatchFound) were
// ever reachable from it -- same one-event-type-per-instance reasoning as
// the Vitals Device/IonmAlert/AdverseEvent instances. A dedicated
// client-web-meridian-screening instance (EventStore.AppHost/AppHost.cs)
// fixes that.
//
// The SAR-escalation half (SarFilingRecorded, MeridianWorkflowC.cs's own
// RequiredSignature step-up gate) is NOT covered here -- Samples.Meridian.
// Seed deliberately never publishes it (no step-up authentication flow
// exists in the seeder), a real, still-open gap (TODO.md), not silently
// worked around.
[TestClass]
public class MeridianWorkflowCPeriodicScreeningPlaybookTests
{
    private static DistributedApplication _app = null!;
    private static string _clientWebMeridianScreeningBaseUrl = null!;
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
            .WaitForResourceAsync("client-web-meridian-screening", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var httpClient = _app.CreateHttpClient("client-web-meridian-screening", "http");
        _clientWebMeridianScreeningBaseUrl = httpClient.BaseAddress!.ToString().TrimEnd('/');

        using var pollClient = new HttpClient();
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await pollClient.GetAsync(_clientWebMeridianScreeningBaseUrl);
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
    public async Task RecordPeriodicScreeningPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "workflow-c-periodic-screening-and-sar-escalation.md"));

        await _page.GotoAsync(_clientWebMeridianScreeningBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Meridian-Screening instance of the Duplex Client -- a dedicated client-web instance launch-configured (ADR-039) to subscribe to the kyc SanctionsScreeningPerformed event type.");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Browse" }).ClickAsync();
        var applicantRow = _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "applicant-1001" });
        await Assertions.Expect(applicantRow).ToBeVisibleAsync(new() { Timeout = 30_000 }); // REPLAY-mode subscription needs a moment to catch up on first load
        await recorder.RecordStepAsync(_page, "Switching to the Browse tab. The seed continuity applicant applicant-1001's periodic sanctions screening (Samples.Meridian.Seed) is already present via REPLAY-mode catch-up.");

        await applicantRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        // Unlike the Device/IonmAlert/AdverseEvent instances (no
        // ViewDefinition registered at all for those EntityTypes), a
        // Detail ViewDefinition genuinely IS registered for
        // "applicantidentity" (Samples.Meridian.Seed) -- so, after the
        // same transient fallback-before-resolution window documented on
        // the other Meridian playbooks, this one settles on the real
        // templated render too, even though that template's own bound
        // fields (applicantId/documentType/claimedLegalName/dateOfBirth/
        // did) don't match SanctionsScreeningPerformed's payload shape at
        // all -- a genuine, real finding, not another timing artifact.
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        await Assertions.Expect(templatedView).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Selecting applicant-1001 opens its Detail view. The registered ApplicantIdentity template renders here too, but its own bound fields (applicantId, documentType, claimedLegalName, dateOfBirth, did) don't match this subscription's SanctionsScreeningPerformed payload shape at all -- every field but applicantId renders blank. ScreeningDate/ListsChecked/MatchFound are real, published data (Samples.Meridian.Seed), just not reachable through this particular template -- a genuine ViewDefinition/payload-shape mismatch, not a masking or subscription defect.");

        await recorder.WriteMarkdownAsync("Meridian -- Workflow C: Periodic Screening and SAR Escalation (screening half only)");

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "workflow-c-periodic-screening-and-sar-escalation.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
