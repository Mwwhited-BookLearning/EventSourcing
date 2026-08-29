using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Extends UI-playbook coverage with a real, already-built screen that
// never had a playbook of its own: the Principal Investigator Queue
// (client-web/packages/reference-app/src/components/queue/
// VitalsPiQueue.vue -> AuthorityQueue.vue), Vitals' own "Domain Decision
// Queue" build-plan item. Unlike every other Vitals playbook, this one
// needs no dedicated single-event-type client instance -- AuthorityQueue
// manages its own independent GraphQL subscriptions from hostBaseUrl/
// authBaseUrl/appId props directly, reachable from ANY Vitals instance's
// Queue tab (App.vue's own queueDomain gate, appId === "trial1").
// Reuses the plain client-web-vitals instance.
//
// Needs a REAL, continuously-arriving pending item, not fixed seed data:
// Samples.Vitals.Simulator (EventStore.AppHost/AppHost.cs) publishes a
// fresh IonmAlertRaised with ReviewPending: true every ~20s, starting
// immediately once it's running (no initial delay, Program.cs's own
// while(true) loop) -- this is exactly the signal
// usePendingAuthorityQueue's own isPending (AuthorityStatus ===
// "pending_review") filters on.
[TestClass]
public class VitalsPrincipalInvestigatorQueuePlaybookTests
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

        await resourceNotificationService
            .WaitForResourceAsync("client-web-vitals", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var httpClient = _app.CreateHttpClient("client-web-vitals", "http");
        _clientWebVitalsBaseUrl = httpClient.BaseAddress!.ToString().TrimEnd('/');

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
    public async Task RecordDecidePendingAlertPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "principal-investigator", "decide-pending-alert.md"));

        await _page.GotoAsync(_clientWebVitalsBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Queue" }).ClickAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Principal Investigator Queue" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Principal Investigator Queue tab -- a real, already-built screen (VitalsPiQueue.vue) reachable from any Vitals client instance, not new this pass.");

        var queueList = _page.GetByTestId("queue-list");
        await Assertions.Expect(queueList).ToBeVisibleAsync(new() { Timeout = 60_000 }); // Samples.Vitals.Simulator publishes a fresh, real pending IonmAlertRaised roughly every 20s
        var firstItem = queueList.Locator("li").First;
        await recorder.RecordStepAsync(_page, "Samples.Vitals.Simulator publishes a fresh IonmAlertRaised with ReviewPending: true roughly every 20 seconds -- this is a real, live pending item, not fixed seed data. The queue subscribes to both the raiser event type and authorityDecision live, so a decision anywhere resolves this list immediately.");

        await firstItem.Locator("input[type=text]").Last.FillAsync("reviewed against the live SSEP trace");
        await firstItem.GetByRole(AriaRole.Button, new() { Name = "Accept" }).ClickAsync();
        var status = firstItem.Locator("[data-testid^='queue-status-']");
        await Assertions.Expect(status).ToContainTextAsync("accepted", new() { Timeout = 15_000 });
        await recorder.RecordStepAsync(_page, "Filling in the required sign-off reason (Meaning, ADR-066) and clicking Accept publishes an authorityDecision as vitals-pi-client (claims: review:ionm) -- AuthorityDecisionResolver folds the target IonmAlertRaised into the authoritative Entity Store, and the item disappears from this queue the moment that decision arrives back over the same live subscription.");

        const string sequenceDiagram = """
            @startuml VitalsPiQueue_Playbook_Sequence
            autonumber
            participant "Samples.Vitals.Simulator" as simulator
            participant "PublishEndpoint\n(Inbox)" as inbox
            participant "Duplex Client\n(Queue tab, AuthorityQueue.vue)" as client
            actor "Principal Investigator\n(review:ionm)" as pi
            database "Entity Store" as entityStore

            simulator -> inbox: POST /publish/IonmAlertRaised\n{ AlertId, SubjectId, Finding, Severity }\nReviewPending: true (every ~20s, forever)
            inbox -> inbox: AuthorityStatus: "pending_review"
            client -> inbox: subscription on_trial1_IonmAlertRaised (REPLAY)\n+ subscription on_trial1_authorityDecision (REPLAY)
            inbox --> client: new pending item appears in the Queue list

            pi -> client: fill Meaning, click Accept
            client -> inbox: POST /publish/authorityDecision\n{ targetEventId, decision: "accepted", decidingActorId, reason }\nBearer <vitals-pi-client token, claims: review:ionm>

            alt caller instead lacks "review:ionm"
              inbox --> client: 403 Forbidden
            else PI clicks Reject instead of Accept
              inbox -> inbox: AuthorityStatus: "rejected" --\nnever folds into the authoritative Entity Store
            end

            inbox -> entityStore: fold now (catch-up) -- AuthorityStatus: "accepted"
            inbox --> client: authorityDecision arrives over the live subscription
            client -> client: remove the resolved item from the Queue list
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Vitals -- Principal Investigator Queue: Decide a Pending IONM Alert", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "principal-investigator", "decide-pending-alert.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
