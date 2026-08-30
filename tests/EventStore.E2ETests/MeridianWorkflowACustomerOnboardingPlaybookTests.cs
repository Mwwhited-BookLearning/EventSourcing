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
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "kyc-analyst", "review-identity-claim.md"));

        await _page.GotoAsync(_clientWebMeridianBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Meridian instance of the Duplex Client, launch-configured (ADR-039) to subscribe to the kyc IdentityClaimSubmitted event type -- the self-attested DID/UCAN claim this workflow's downstream review acts on.");

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
        await recorder.RecordStepAsync(_page, "The Browse tab shows applicant-1001, whose IdentityClaimSubmitted event carries its self-attested did:key DID alongside the claimed legal name and date of birth (ADR-036).");

        await applicantRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        // Briefly renders GenericFallbackView before loadViewDefinition's async
        // GraphQL round trip resolves (~500ms, measured directly) -- wait for
        // the real templated render (see MeridianWorkflowADocumentAndBiometric
        // CapturePlaybookTests's own comment for the full correction history).
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        await Assertions.Expect(templatedView).ToBeVisibleAsync();
        // Corrected caption: the seed publishes this via an ordinary,
        // claims-free principal (no AttestedActorId/AttestedClaims/
        // ReviewPending), so AuthorityStatus reaches "accepted" immediately
        // -- no analyst review actually ran for this specific data. The
        // domain's own self-attestation -> analyst-decision path IS real
        // (MeridianWorkflowA.cs's own header comment), just not what this
        // continuity applicant's seed data exercises; see the sequence
        // diagram's own note for where that path would fit.
        await recorder.RecordStepAsync(_page, "The Detail view, rendered from the registered ApplicantIdentity ViewDefinition, shows the IdentityClaimSubmitted payload itself (masked ClaimedLegalName/DateOfBirth, ADR-009) plus AuthorityStatus: accepted -- an ordinary, immediately-accepted publish for this continuity applicant, not the result of an analyst decision (see the sequence diagram below for the fuller self-attestation path this domain also supports).");

        const string sequenceDiagram = """
            @startuml MeridianKycAnalyst_ReviewIdentityClaim_Sequence
            autonumber
            actor "Applicant" as applicant
            participant "PublishEndpoint\n(Inbox)" as inbox
            participant "Duplex Client\n(client-web-meridian)" as client
            database "Entity Store\n(kyc:ApplicantIdentity:applicant-1001)" as entityStore

            applicant -> inbox: POST /publish/IdentityClaimSubmitted\n{ ApplicantId, Did (self-attested DID key),\n  ClaimedLegalName (masked), DateOfBirth (masked) }

            alt ordinary publish (this continuity applicant's own seed data)
              inbox -> entityStore: fold immediately, AuthorityStatus: "accepted"
            else self-attested, credential-agnostic capture (ADR-035/036)
              inbox -> inbox: AuthorityStatus starts "unattested"\n(AttestedActorId/AttestedClaims present)
              note right: not exercised by this playbook's own seed data --\nreal and proven via NonAuthoritativeCaptureScenarioAssertions
              actor "KYC Analyst" as analyst
              analyst -> inbox: POST /publish/authorityDecision\n{ targetEventId, decision: "accepted", decidingActorId }\nRequiredClaims: "identity:review"
              inbox -> entityStore: fold now (catch-up)
            end

            == Later: staff browse the result via client-web ==
            client -> entityStore: (via GraphQL Subscription, REPLAY)
            client -> applicant: Detail view -- ClaimedLegalName/DateOfBirth\nrender masked (PartialReveal, requiredClaim "identity:pii-read")
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Meridian -- Workflow A: Customer Onboarding and Identity Verification", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "kyc-analyst", "review-identity-claim.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
