using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// ADR-101's own client-side deliverable: "My Tasks" (client-web/packages/
// reference-app/src/views/TasksView.vue -> components/tasks/MyTasksView.vue),
// the cross-domain read model fed by the new "vitals-flows"/"meridian-flows"
// worker hosts (EventStore.AppHost/AppHost.cs) rather than a bespoke
// per-domain subscription -- the first playbook to drive those two new
// AppHost resources at all, not just PendingTaskProjectionSqliteTests'
// own two-WebApplicationFactory HTTP proof.
//
// Needs a REAL, continuously-arriving pending item, the same reasoning
// VitalsPrincipalInvestigatorQueuePlaybookTests already established:
// Samples.Vitals.Simulator publishes a fresh IonmAlertRaised with
// ReviewPending: true every ~20s, forever -- vitals-flows' own
// VitalsWorkflowDFlow raises an open "review:ionm" task for every one of
// these regardless of ReviewPending/acknowledgment (see that flow's own
// .puml), so this is a genuinely live signal, not fixed seed data.
[TestClass]
public class TasksListPlaybookTests
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
        // The read model this whole playbook exercises -- wait for it
        // explicitly, not just client-web-vitals, so a failure to start
        // (e.g. a real regression in the AppHost wiring this pass added)
        // fails fast with a clear resource name instead of a generic
        // "tasks-list never appeared" timeout later.
        await resourceNotificationService
            .WaitForResourceAsync("vitals-flows", KnownResourceStates.Running)
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
    public async Task RecordMyTasksPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "my-tasks", "discover-and-open-a-pending-task.md"));

        await _page.GotoAsync(_clientWebVitalsBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await _page.GetByRole(AriaRole.Link, new() { Name = "My Tasks" }).ClickAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "My Tasks" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the My Tasks tab -- one cross-domain list (ADR-101), reachable from any client-web instance regardless of AppId, backed by the myTasks GraphQL query over the flow engine's own PendingTask read model.");

        var tasksList = _page.GetByTestId("tasks-list");
        // Generous timeout: the Simulator's own ~20s publish cadence, plus
        // vitals-flows' ProjectionHost catch-up cycle, plus useMyTasks' own
        // 10s poll interval, can stack up to noticeably more than the
        // Queue playbook's 60s budget for a single live IonmAlertRaised.
        await Assertions.Expect(tasksList).ToBeVisibleAsync(new() { Timeout = 90_000 });
        var firstRow = tasksList.Locator("tbody tr").First;
        await recorder.RecordStepAsync(_page, "Samples.Vitals.Simulator publishes a fresh IonmAlertRaised roughly every 20 seconds -- vitals-flows (a real AppHost worker resource, ADR-101) walks the real embedded intraoperative-monitoring-and-alert-response.puml against it and raises an open 'review:ionm' task the moment it arrives, polled into view here within a few seconds.");

        await firstRow.GetByRole(AriaRole.Button, new() { Name = "Open" }).ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync(new Regex(".*/queue$"));
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Principal Investigator Queue" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "\"Open\" navigates to the existing Principal Investigator Queue screen (AuthorityQueue.vue) -- the task list only discovers work, the accept/reject decision UI is unchanged (ADR-101's own Consequences).");

        const string sequenceDiagram = """
            @startuml MyTasks_Playbook_Sequence
            autonumber
            participant "Samples.Vitals.Simulator" as simulator
            participant "eventstore\n(Host.Postgres)" as eventstore
            participant "vitals-flows\n(ProjectionHost<PendingTask>)" as flows
            database "PendingTasks\n(shared SQLite)" as pendingTasks
            participant "Duplex Client\n(My Tasks tab)" as client
            actor "Principal Investigator" as pi

            simulator -> eventstore: POST /publish/IonmAlertRaised\n{ AlertId, SubjectId, Finding, Severity }\n(every ~20s, forever)
            flows -> eventstore: QUERY /graphql\nsubscription on_trial1_IonmAlertRaised (Follow, REPLAY)
            eventstore --> flows: new IonmAlertRaised event
            flows -> flows: walk the real embedded .puml (FlowInterpreter) -> open "review:ionm" task
            flows -> pendingTasks: upsert PendingTask row

            client -> eventstore: QUERY /graphql { myTasks { ... } }\n(polled every 10s, as vitals-pi-client)
            eventstore -> pendingTasks: read (read-only PendingTasksDbContext)
            eventstore --> client: the open task appears in the list

            pi -> client: click "Open"
            client -> client: navigate to /queue (AuthorityQueue.vue) -- decision UI unchanged
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Vitals -- My Tasks: Discover and Open a Pending Task", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "my-tasks", "discover-and-open-a-pending-task.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
