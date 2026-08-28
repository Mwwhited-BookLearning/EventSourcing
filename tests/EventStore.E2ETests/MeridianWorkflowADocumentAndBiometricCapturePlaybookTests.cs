using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Extends UI-playbook coverage beyond Vitals' Workflow A (TODO.md) --
// same PlaybookRecorder mechanism, unchanged, walked against
// client-web-meridian instead. That instance's launch configuration
// (EventStore.AppHost/AppHost.cs) subscribes it to ONE fixed event type,
// IdentityClaimSubmitted (VITE_EVENT_TYPE), over VITE_ENTITY_TYPE
// "applicantidentity" -- ADR-039's per-instance model means only entities
// that have published AT LEAST ONE IdentityClaimSubmitted event ever reach
// this instance's Browse cache. The seed continuity applicant
// (applicant-1001, Samples.Meridian.Seed/Program.cs) publishes exactly
// that event, alongside IdentityDocumentUploaded/BiometricCaptureRecorded/
// SanctionsScreeningPerformed folded onto the same entity -- so this one
// browsable entity genuinely covers both of Workflow A's feature docs
// (this one, and MeridianWorkflowACustomerOnboardingPlaybookTests).
//
// Confirmed by reading subscriptionBuilder.ts directly: a GraphQL
// Subscription field is built per (AppId, EventType) pair, never per
// EntityType -- there is no server-side or client-side mechanism today
// for one client-web instance to browse entities across more than the
// one event type it was launched with. This is exactly why Vitals'
// Device/AdverseEvent/IonmAlert entities (Workflows B/C/D) are NOT
// reachable from the existing client-web-vitals instance (locked to
// PatientScreened/patient) -- see TODO.md's own entry for that gap
// rather than re-deriving it here.
[TestClass]
public class MeridianWorkflowADocumentAndBiometricCapturePlaybookTests
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
    public async Task RecordDocumentAndBiometricCapturePlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "workflow-a-document-and-biometric-capture.md"));

        await _page.GotoAsync(_clientWebMeridianBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Meridian instance of the Duplex Client. It's launch-configured (ADR-039) to subscribe to the kyc IdentityClaimSubmitted event type.");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Browse" }).ClickAsync();
        var applicantRow = _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "applicant-1001" });
        await Assertions.Expect(applicantRow).ToBeVisibleAsync(new() { Timeout = 30_000 }); // REPLAY-mode subscription needs a moment to catch up on first load
        await recorder.RecordStepAsync(_page, "Switching to the Browse tab. The continuity applicant applicant-1001 (Samples.Meridian.Seed) is already present, having published IdentityDocumentUploaded, BiometricCaptureRecorded, and IdentityClaimSubmitted -- all folded onto the one kyc:ApplicantIdentity:applicant-1001 entity.");

        await applicantRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        var detailHeading = _page.GetByRole(AriaRole.Heading).Filter(new() { HasText = "applicant-1001" });
        await Assertions.Expect(detailHeading).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Selecting applicant-1001 opens its Detail view -- rendered generically, via GenericFallbackView (ADR-039); the registered ApplicantIdentity ViewDefinition doesn't resolve at runtime here (a real, unexplained gap -- TODO.md), matching the same symptom already seen for Vitals' Patient. This subscription's own payload type is IdentityClaimSubmitted's, so only that event's fields (DID, claimed legal name, date of birth, document type) render -- ExtractedDocumentNumber/biometric fields from the upstream capture events aren't part of this payload shape, even though all three events fold onto the same entity in the Entity Store itself.");

        await recorder.WriteMarkdownAsync("Meridian -- Workflow A: Document and Biometric Capture");

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "workflow-a-document-and-biometric-capture.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
