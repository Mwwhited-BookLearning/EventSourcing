using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Closes Vitals' Workflow C (Trial Data Export and Subject Rights,
// TODO.md) -- the one proving-ground use case that had NO client-web UI
// surface at all before this pass. BitemporalPlaybackControl.vue/
// OfflineBundleViewer.vue and their own underlying API client functions
// (exportLineage/downloadBundle/playbackAsOf, playbackClient.ts) already
// existed, fully built and unit-tested, but nothing in App.vue ever
// wired them into a reachable screen (confirmed by grep -- neither
// component was imported anywhere outside its own spec file). New
// LineageExportAndPlaybackPanel.vue (a domain-agnostic "Lineage &
// Playback" tab, reachable from ANY client instance, not gated to a
// specific AppId the way the Queue/Relying-Party tabs are) is the
// missing glue.
//
// Reuses the plain client-web-vitals instance against the seed
// continuity subject (trial1:patient:S-0091) -- exports its real lineage
// bundle live against the running server, no pre-computed fixture.
[TestClass]
public class VitalsWorkflowCLineageExportAndPlaybackPlaybookTests
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
    public async Task RecordExportAndPlaybackPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "sponsor-auditor", "export-and-playback-lineage.md"));

        await _page.GotoAsync(_clientWebVitalsBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Lineage & Playback" }).ClickAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Lineage Export and Bitemporal Playback" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Lineage & Playback tab -- new this pass, and domain-agnostic (reachable from any client-web instance, not gated to a specific AppId the way the Queue/Relying-Party tabs are). Mirrors the feature doc's own Salt mockup: one Entity ID field feeds Export Lineage Bundle; a separate As-of SequenceNumber field feeds System-Time Playback.");

        await _page.GetByTestId("export-entity-id").FillAsync("trial1:patient:S-0091");
        await _page.GetByTestId("export-button").ClickAsync();
        var eventList = _page.GetByTestId("toggle-event-list");
        await Assertions.Expect(eventList).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await recorder.RecordStepAsync(_page, "Exporting the seed continuity subject's own lineage bundle. This runs the real exportLineage GraphQL query (events:lineage:read) against the live server, downloads the resulting NDJSON bundle from its own bundleUrl, and renders it directly on this screen via the already-built OfflineBundleViewer.vue -- the same offline-player verification screen a downloaded export's own standalone viewer uses, reused here rather than duplicated.");

        await eventList.ClickAsync();
        var eventTable = _page.GetByTestId("event-list");
        await Assertions.Expect(eventTable).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Viewing the full event list shows every event in this bundle's own chain, each with its own SequenceNumber, OccurredAt, and LateArrivalFlag, exactly as exported (no second masking/claims enforcement point, ADR-068's own \"enforced once, at export time\" rule).");

        // Reads the real first row's own SequenceNumber rather than
        // assuming one -- it varies run to run depending on how much
        // else this AppHost boot's own seed/simulator activity already
        // published before this subject's own first event.
        var firstSequenceNumberText = await eventTable.Locator("tbody tr").First.Locator("td").First.InnerTextAsync();
        var firstSequenceNumber = int.Parse(firstSequenceNumberText);

        await _page.GetByTestId("playback-entity-id").FillAsync("trial1:patient:S-0091");
        await _page.GetByTestId("playback-starting-sequence-number").FillAsync(firstSequenceNumber.ToString());
        await _page.GetByTestId("playback-play").ClickAsync();
        var playbackData = _page.GetByTestId("playback-data");
        await Assertions.Expect(playbackData).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await recorder.RecordStepAsync(_page, $"System-Time Playback reconstructs this same entity as of SequenceNumber {firstSequenceNumber} -- its own very first event -- via the real playbackAsOf GraphQL query (BitemporalPlaybackControl.vue). The [<]/[>] controls step to the immediately adjacent SequenceNumber, each a fresh reconstruction against the live server, never a cached snapshot (ADR-068's own stated v1 scope).");

        const string sequenceDiagram = """
            @startuml VitalsLineageExportAndPlayback_Playbook_Sequence
            autonumber
            actor "Sponsor auditor" as auditor
            participant "Duplex Client\n(Lineage & Playback tab)" as client
            participant "GraphQL\n(exportLineage / playbackAsOf)" as graphql
            participant "GET /lineage-exports/{exportId}" as bundleEndpoint
            database "Event Log" as eventLog

            auditor -> client: Entity ID: trial1:patient:S-0091, click Export Lineage Bundle
            client -> graphql: query exportLineage(entityId)\nBearer <follower-client token, events:lineage:read>
            graphql -> graphql: CheckRootAsync -- caller holds the required\nRead claim for this entity's own root event(s)?
            alt caller lacks the required Read claim
              graphql --> client: Forbidden
            else caller is authorized (this playbook's own scenario)
              graphql -> eventLog: build the bundle (manifest + every\nevent in this entity's own chain)
              graphql --> client: { bundleUrl: "/lineage-exports/{exportId}" }
              client -> bundleEndpoint: GET {bundleUrl}\nBearer <same token> (15-minute retrieval window)
              bundleEndpoint --> client: NDJSON (manifest line + one line per event)
              client -> client: parseNdjson + verifyBundle (manifest hash\nrecomputed from each event's own ChainHash) --\nrendered via OfflineBundleViewer.vue, no server round trip
            end

            == Separately: System-Time Playback (VCR-style, arrival-order fold) ==
            auditor -> client: Entity ID + As-of SequenceNumber, click Play
            client -> graphql: query playbackAsOf(entityId, asOfSequenceNumber)
            alt no reconstruction exists at or before that SequenceNumber
              graphql --> client: null
            else a reconstruction exists
              graphql --> client: { data, lateArrivalCorrectionShown }
              client -> auditor: [<] and [>] step to the adjacent SequenceNumber,\nre-fetching fresh each time -- never a cached snapshot
            end
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Vitals -- Workflow C: Trial Data Export and Bitemporal Playback", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "vitals", "sponsor-auditor", "export-and-playback-lineage.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
