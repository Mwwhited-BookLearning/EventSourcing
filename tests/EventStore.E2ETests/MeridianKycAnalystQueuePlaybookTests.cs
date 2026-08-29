using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Closes TODO.md's last "already-built Queue UI" playbook gap, the
// Meridian counterpart to VitalsPrincipalInvestigatorQueuePlaybookTests
// (see that file's own header for the shared reasoning): the KYC Analyst
// Queue (MeridianAnalystQueue.vue -> AuthorityQueue.vue) has no dedicated
// client instance of its own -- AuthorityQueue manages its own
// independent GraphQL subscriptions from hostBaseUrl/authBaseUrl/appId
// props, reachable from ANY Meridian instance's Queue tab. Reuses the
// plain client-web-meridian instance.
//
// Needs a real, continuously-arriving pending item: Samples.Meridian.
// Simulator (EventStore.AppHost/AppHost.cs) publishes a fresh
// IdentityClaimSubmitted + SanctionsScreeningPerformed pair every ~25s,
// alternating MatchFound (every 3rd tick is a hit, i % 3 == 0) so the
// queue shows both hits and clears rather than an unrealistic 100% rate
// -- this test waits for a MatchFound: true item specifically (isPending
// checks payload.matchFound === true), which can take a couple of
// simulator ticks, not fixed seed data.
[TestClass]
public class MeridianKycAnalystQueuePlaybookTests
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
    public async Task RecordDecidePendingMatchPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "compliance-officer", "decide-pending-match.md"));

        await _page.GotoAsync(_clientWebMeridianBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Queue" }).ClickAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "KYC Analyst Queue" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the KYC Analyst Queue tab -- a real, already-built screen (MeridianAnalystQueue.vue) reachable from any Meridian client instance, not new this pass.");

        var queueList = _page.GetByTestId("queue-list");
        await Assertions.Expect(queueList).ToBeVisibleAsync(new() { Timeout = 150_000 }); // Samples.Meridian.Simulator publishes a matched SanctionsScreeningPerformed roughly every 3rd tick (~75s), not every tick
        var firstItem = queueList.Locator("li").First;
        await recorder.RecordStepAsync(_page, "Samples.Meridian.Simulator publishes a fresh SanctionsScreeningPerformed for a new applicant roughly every 25 seconds, alternating MatchFound so the queue shows both hits and clears -- this is a real, live pending match, not fixed seed data.");

        await firstItem.Locator("input[type=text]").Last.FillAsync("confirmed against OFAC-SDN, applicant's own stated DOB does not match");
        await firstItem.GetByRole(AriaRole.Button, new() { Name = "Accept" }).ClickAsync();
        var status = firstItem.Locator("[data-testid^='queue-status-']");
        await Assertions.Expect(status).ToContainTextAsync("accepted", new() { Timeout = 15_000 });
        await recorder.RecordStepAsync(_page, "Filling in the required sign-off reason (Meaning, ADR-066) and clicking Accept publishes an authorityDecision as meridian-analyst-client (claims: identity:aml-review) -- AuthorityDecisionResolver folds the confirmed match into the authoritative Entity Store, and the item disappears from this queue the moment that decision arrives back over the same live subscription.");

        const string sequenceDiagram = """
            @startuml MeridianKycAnalystQueue_Playbook_Sequence
            autonumber
            participant "Samples.Meridian.Simulator" as simulator
            participant "PublishEndpoint\n(Inbox)" as inbox
            participant "Duplex Client\n(Queue tab, AuthorityQueue.vue)" as client
            actor "Compliance officer\n(identity:aml-review)" as officer
            database "Entity Store" as entityStore
            database "Live View" as liveView

            simulator -> inbox: POST /publish/SanctionsScreeningPerformed\n{ ApplicantId, ScreeningDate, ListsChecked,\n  MatchFound: true, MatchConfidence, MatchedName, MatchedListEntryId }\n(every ~25s; roughly 1 in 3 ticks is a match)
            inbox -> inbox: AuthorityStatus: "pending_review" (ADR-042) --\nalways captured regardless of MatchConfidence
            inbox -> liveView: fold into the Live View only (not yet authoritative)
            client -> inbox: subscription on_kyc_SanctionsScreeningPerformed (REPLAY)\n+ subscription on_kyc_authorityDecision (REPLAY)
            inbox --> client: new pending item appears in the Queue list\n(isPending: payload.matchFound === true)

            officer -> client: fill Meaning, click Accept
            client -> inbox: POST /publish/authorityDecision\n{ targetEventId, decision: "accepted", decidingActorId, reason }\nBearer <meridian-analyst-client token, claims: identity:aml-review>

            alt caller instead lacks "identity:review"/"identity:aml-review"
              inbox --> client: 403 Forbidden
            else officer clicks Reject instead (false positive)
              inbox -> inbox: AuthorityStatus: "rejected" --\nnever folds into the authoritative Entity Store, Payload untouched (Annotate)
            end

            inbox -> entityStore: fold now (catch-up) -- MatchedName/MatchedListEntryId\njoin the authoritative record
            inbox --> client: authorityDecision arrives over the live subscription
            client -> client: remove the resolved item from the Queue list
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Meridian -- KYC Analyst Queue: Decide a Pending Sanctions Match", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "compliance-officer", "decide-pending-match.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
