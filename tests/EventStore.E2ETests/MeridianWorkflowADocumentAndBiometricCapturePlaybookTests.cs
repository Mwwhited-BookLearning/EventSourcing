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
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "applicant", "capture-identity-documents.md"));

        await _page.GotoAsync(_clientWebMeridianBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Meridian instance of the Duplex Client. It's launch-configured (ADR-039) to subscribe to the kyc IdentityClaimSubmitted event type.");

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
        await recorder.RecordStepAsync(_page, "Switching to the Browse tab. The continuity applicant applicant-1001 (Samples.Meridian.Seed) is already present, having published IdentityDocumentUploaded, BiometricCaptureRecorded, and IdentityClaimSubmitted -- all folded onto the one kyc:ApplicantIdentity:applicant-1001 entity.");

        await applicantRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        // Briefly renders GenericFallbackView before useEntityViewActions.
        // loadViewDefinition's async GraphQL round trip resolves (~500ms,
        // measured directly) -- wait for the real templated render, the same
        // fix applied to VitalsWorkflowAPlaybookTests after an earlier pass of
        // this file screenshot that transient state and wrongly wrote it up
        // as the ApplicantIdentity ViewDefinition never resolving at all.
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        await Assertions.Expect(templatedView).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Selecting applicant-1001 opens its Detail view, rendered from the ApplicantIdentity Detail ViewDefinition Samples.Meridian.Seed registers (ADR-039). This subscription's own payload type is IdentityClaimSubmitted's, so only that event's fields (DID, claimed legal name, date of birth, document type) render -- ExtractedDocumentNumber/biometric fields from the upstream capture events aren't part of this payload shape, even though all three events fold onto the same entity in the Entity Store itself.");

        const string sequenceDiagram = """
            @startuml MeridianWorkflowADocument_Playbook_Sequence
            autonumber
            actor "Applicant" as applicant
            participant "PublishEndpoint\n(Inbox)" as inbox
            participant "Duplex Client\n(client-web-meridian)" as client
            participant "GraphQL Subscription\n(on_kyc_IdentityClaimSubmitted)" as graphql
            database "Event Log" as eventLog
            database "Entity Store\n(kyc:ApplicantIdentity:applicant-1001)" as entityStore

            applicant -> inbox: POST /publish/IdentityDocumentUploaded\n{ ApplicantId, DocumentType, ExtractedDocumentNumber }
            inbox -> eventLog: INSERT StoredEvent (accepted)
            inbox -> entityStore: fold (Partial) onto ApplicantIdentity
            applicant -> inbox: POST /publish/BiometricCaptureRecorded\n{ ApplicantId, CaptureType, LivenessCheckResult, LivenessConfidence }
            inbox -> eventLog: INSERT StoredEvent (accepted)
            inbox -> entityStore: fold (Partial) onto the SAME ApplicantIdentity
            applicant -> inbox: POST /publish/IdentityClaimSubmitted\n{ ApplicantId, Did, ClaimedLegalName (masked),\n  DateOfBirth (masked), DocumentType }
            inbox -> eventLog: INSERT StoredEvent (accepted)
            inbox -> entityStore: fold (Partial) onto the SAME ApplicantIdentity

            == Later: staff browse the result via client-web ==
            client -> graphql: subscription on_kyc_IdentityClaimSubmitted\n(mode: REPLAY)\nBearer <follower-client token, events:follow>
            graphql -> eventLog: SELECT IdentityClaimSubmitted events WHERE AppId="kyc"
            eventLog --> graphql: IdentityClaimSubmitted{applicant-1001, ...}
            graphql --> client: SSE stream
            client -> applicant: Detail view shows ONLY this subscription's\nown fields (Did/ClaimedLegalName/DateOfBirth/DocumentType) --\nExtractedDocumentNumber/biometric fields are real,\nfolded into the Entity Store, but not part of THIS\nsubscription's payload shape (ADR-039, one event type per instance)
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Meridian -- Workflow A: Document and Biometric Capture", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "applicant", "capture-identity-documents.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
