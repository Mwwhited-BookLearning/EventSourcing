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
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "compliance-officer", "review-periodic-screening.md"));

        await _page.GotoAsync(_clientWebMeridianScreeningBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Meridian-Screening instance of the Duplex Client -- a dedicated client-web instance launch-configured (ADR-039) to subscribe to the kyc SanctionsScreeningPerformed event type.");

        await _page.GetByRole(AriaRole.Link, new() { Name = "Browse" }).ClickAsync();
        // ADR-099 -- EntityBrowser now paginates (page size 10); the filter box
        // is the way to reach a specific row once the simulator has pushed
        // more than a page of entities in front of it (found by actually
        // running this playbook, not assumed).
        await _page.GetByTestId("entity-browser-filter").FillAsync("applicant-1001");
        var applicantRow = _page.GetByRole(AriaRole.Row).Filter(new() { HasText = "applicant-1001" });
        await Assertions.Expect(applicantRow).ToBeVisibleAsync(new() { Timeout = 30_000 }); // REPLAY-mode subscription needs a moment to catch up on first load
        await recorder.RecordStepAsync(_page, "Switching to the Browse tab. The seed continuity applicant applicant-1001's periodic sanctions screening (Samples.Meridian.Seed) is already present via REPLAY-mode catch-up.");

        await applicantRow.GetByRole(AriaRole.Button, new() { Name = "View" }).ClickAsync();
        // Unlike the Device/IonmAlert/AdverseEvent instances (no
        // ViewDefinition registered at all for those EntityTypes), a
        // Detail ViewDefinition genuinely IS registered for
        // "applicantidentity" (Samples.Meridian.Seed) -- after the same
        // transient fallback-before-resolution window documented on the
        // other Meridian playbooks, this one settles on the real templated
        // render, which now includes screeningDate/listsChecked/matchFound
        // bindings alongside Workflow A's own five (TODO.md's tracked
        // ViewDefinition/payload-shape mismatch, fixed by extending the
        // one shared template rather than by building a second one).
        var templatedView = _page.GetByLabel("Entity (ViewDefinition-rendered)");
        await Assertions.Expect(templatedView).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Selecting applicant-1001 opens its Detail view. The registered ApplicantIdentity template now covers this event type's own fields too -- Screening Date (2026-07-30, the later, matched screening -- this instance's entity cache reflects the most recent SanctionsScreeningPerformed for this applicant) and Match Found (true). Lists Checked renders blank -- EventTypeSchemaReader.cs deliberately skips top-level array-typed properties when building this GraphQL payload type at all (an already-documented, accepted narrowing, not a new gap), so ListsChecked was never selectable to begin with. Document Type/Claimed Legal Name/Date of Birth/DID stay blank here, same reasoning as before: those are IdentityClaimSubmitted's own fields, not this subscription's.");

        const string sequenceDiagram = """
            @startuml MeridianPeriodicScreening_Playbook_Sequence
            autonumber
            participant "Screening worker\n(ISanctionsScreeningProvider)" as worker
            participant "PublishEndpoint\n(Inbox)" as inbox
            actor "Compliance officer\n(identity:aml-review)" as officer
            actor "Any other caller" as other
            database "Entity Store\n(kyc:ApplicantIdentity)" as entityStore
            database "Live View" as liveView

            worker -> inbox: POST /publish/SanctionsScreeningPerformed\n{ ApplicantId, ScreeningDate, ListsChecked, MatchFound }

            alt MatchFound = false (this playbook's own first seed screening)
              inbox -> entityStore: fold (Partial) immediately, AuthorityStatus: "accepted"
            else MatchFound = true (this playbook's own second seed screening)
              inbox -> inbox: ReviewPending: true --\nAuthorityStatus: "pending_review" (ADR-042),\nalways captured regardless of MatchConfidence
              inbox -> liveView: fold into the Live View only\n(not yet the authoritative Entity Store)
              other -> inbox: POST /publish/authorityDecision\n{ targetEventId, decision, decidingActorId }
              inbox --> other: 403 Forbidden -- lacks "identity:review"\nor "identity:aml-review" (RequiredClaims OR-set)
              officer -> inbox: POST /publish/authorityDecision\n{ targetEventId, decision: "accepted"|"rejected",\n  decidingActorId, reason }
              alt decision = "accepted" (this playbook's own seed data)
                inbox -> entityStore: fold now (catch-up) -- MatchedName/\nMatchedListEntryId/etc. join the authoritative record
              else decision = "rejected" (false positive)
                inbox -> inbox: AuthorityStatus: "rejected" --\nnever folds into the authoritative Entity Store
              end
            end

            == Later: staff browse the result via client-web ==
            participant "Duplex Client\n(client-web-meridian-screening)" as client
            client -> entityStore: (via GraphQL Subscription on_kyc_SanctionsScreeningPerformed, REPLAY)
            client -> officer: Detail view -- ScreeningDate/MatchFound render;\nMatchedName/MatchedListEntryId stay masked without\nthe "identity:aml-review" reveal claim
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Meridian -- Workflow C: Periodic Screening and SAR Escalation (screening half only)", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "compliance-officer", "review-periodic-screening.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
