using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using EventStore.Dpop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// ADR-039's client-local outbox / ADR-069's pluggable flush triggers, both
// previously verified only by mocked Vitest specs (deviceReadingOutbox.spec.ts,
// stores/outbox.spec.ts) -- never against a real browser. Direct request:
// "write that as a playwrite test. and if its not there I would even like
// buttons to force offline/online as well as the ability to detect
// online/offline automatically." This is that real, in-browser proof, for
// BOTH mechanisms:
//   1. Genuine automatic detection -- Playwright's own BrowserContext.
//      SetOfflineAsync toggles the real Chromium network layer, which
//      flips navigator.onLine and fires real window 'offline'/'online'
//      events -- useOnlineStatus's own listener, unchanged, is what
//      reacts here, exactly as it would to a real Wi-Fi drop.
//   2. The new manual override -- App.vue's "Go Offline"/"Go Online"
//      buttons (useConnectivityStore, added this pass), independent of
//      real network state, for a deterministic demo/test toggle.
//
// Targets a throwaway schema/entity this test registers for itself
// (AppId "e2e-offline-demo", EventType "OrderPlaced") rather than any real
// Vitals/Meridian business event type -- a genuine, previously-
// undiscovered gap found while building this test: every real domain
// event type's own RequiredClaims (e.g. PatientScreened's "patient:
// enroll") are held by NO seeded HTTP client at all, only asserted
// in-process by Samples.Vitals.Seed/Simulator directly; no real browser
// session could ever have published one over HTTP. This test's own
// schema has no RequiredClaims, so the new demo-dispatcher-client
// (DevIdpSeeder.cs, added this pass) can publish it for real.
//
// This capability is domain-agnostic (App.vue/mvvm-client-level, not
// Vitals/Meridian-specific) -- hence docs/playbooks/core/, a new sibling
// to the vitals/meridian domain folders (docs/playbooks/README.md's own
// naming convention still applies: {domain}/{role}/{task}.md, "core" being
// the domain here since no proving-ground persona owns this screen).
[TestClass]
public class OfflineOutboxSyncPlaybookTests
{
    private const string DemoAppId = "e2e-offline-demo";
    private const string DemoEventType = "OrderPlaced";
    private const string DemoOrderId = "o-e2e-offline-1";

    private static DistributedApplication _app = null!;
    private static string _clientWebVitalsBaseUrl = null!;
    private static IPlaywright _playwright = null!;
    private static IBrowser _browser = null!;
    private IBrowserContext _context = null!;
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

        await RegisterSchemaAndSeedAsync();

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }

    // Registers a throwaway, no-RequiredClaims schema and publishes one
    // seed event -- real DPoP-bound HTTP calls (ADR-017), the same
    // mechanism this session's own cross-provider content-parity
    // scratchpad script already proved works against a live DevIdp/host.
    private static async Task RegisterSchemaAndSeedAsync()
    {
        using var devidp = _app.CreateHttpClient("devidp", "http");
        using var eventstore = _app.CreateHttpClient("eventstore", "https");

        var operatorKey = DpopKeyPair.Generate();
        var dispatcherKey = DpopKeyPair.Generate();
        var operatorToken = await GetTokenAsync(devidp, "operator-client", "operator-client-secret", "registry:admin", operatorKey);
        var dispatcherToken = await GetTokenAsync(devidp, "demo-dispatcher-client", "demo-dispatcher-client-secret", "events:follow events:publish", dispatcherKey);

        // Lowercase property names, deliberately -- NOT this repo's usual
        // PascalCase schema convention. `EventTypeSchemaReader` exposes
        // every registered field to GraphQL/the client camelCased
        // regardless of how the schema itself spelled it (confirmed
        // against every other schema in this repo, e.g. "CheckId"
        // resolves as `checkId`), so a client that later re-publishes a
        // PATCH built from its own cached (camelCased) read-side data --
        // exactly what App.vue's submitAmountCommand does, merging the
        // cached entity's known fields under a new Amount value -- can
        // only satisfy a `required` check whose OWN declared name is
        // already lowercase. A real PascalCase schema (every actual
        // Vitals/Meridian one) cannot be satisfied this way at all
        // without a client-side PascalCase re-encoding step this project
        // has never built; sidestepped here by owning the schema.
        const string schema = """
            {
              "type": "object",
              "properties": {
                "orderId": { "type": "string" },
                "amount": { "type": "number" }
              },
              "required": ["orderId"]
            }
            """;
        var registerPayload = JsonSerializer.Serialize(new
        {
            appId = DemoAppId,
            jsonSchema = schema,
            filterableFields = Array.Empty<object>(),
            changeKind = "Full",
            entityIdField = "$.orderId",
            parentValidationMode = "Permissive",
        });
        using var registerRequest = new HttpRequestMessage(HttpMethod.Put, $"registry/{DemoEventType}")
        { Content = new StringContent(registerPayload, Encoding.UTF8, "application/json") };
        await SendAuthedAsync(eventstore, registerRequest, operatorToken, operatorKey);

        var seedPayload = JsonSerializer.Serialize(new { appId = DemoAppId, schemaVersion = 1, payload = JsonSerializer.Serialize(new { orderId = DemoOrderId, amount = 1 }) });
        using var publishRequest = new HttpRequestMessage(HttpMethod.Post, $"publish/{DemoEventType}")
        { Content = new StringContent(seedPayload, Encoding.UTF8, "application/json") };
        await SendAuthedAsync(eventstore, publishRequest, dispatcherToken, dispatcherKey);
    }

    private static async Task<string> GetTokenAsync(HttpClient devidp, string clientId, string clientSecret, string scope, DpopKeyPair key)
    {
        var tokenUrl = new Uri(devidp.BaseAddress!, "connect/token").ToString();
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = clientSecret, ["scope"] = scope };
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Add("DPoP", key.CreateProof("POST", tokenUrl));
        var response = await devidp.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Token request for {clientId} failed: {response.StatusCode} {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task SendAuthedAsync(HttpClient http, HttpRequestMessage request, string token, DpopKeyPair key)
    {
        var htu = new Uri(http.BaseAddress!, request.RequestUri!).GetLeftPart(UriPartial.Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("DPoP", key.CreateProof(request.Method.Method, htu, token));
        var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{request.Method} {request.RequestUri} failed: {response.StatusCode} {body}");
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
        // A dedicated context per test (not just a new page on the shared
        // default context, the pattern every other playbook in this
        // project uses) -- SetOfflineAsync applies at the context level,
        // and this test is the first to need that lifecycle at all.
        _context = await _browser.NewContextAsync();
        _page = await _context.NewPageAsync();
    }

    [TestCleanup]
    public async Task TestCleanupAsync()
    {
        await _context.CloseAsync();
    }

    [TestMethod]
    public async Task RecordOfflineOutboxSyncPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "core", "user", "go-offline-and-resync.md"));

        // appConfig.ts's own documented override mechanism ("query string
        // wins if present") -- reusing the already-running client-web-
        // vitals Vite server, pointed at this test's own throwaway
        // schema/entity instead of that instance's baked-in trial1/
        // PatientScreened config. demo-dispatcher-client is the one
        // identity holding both events:follow (this instance's own live
        // subscription) and events:publish (the command it dispatches).
        var query = "appId=" + DemoAppId +
            "&entityType=orderplaced&eventType=" + DemoEventType + "&entityIdField=orderId" +
            "&clientId=demo-dispatcher-client&clientSecret=demo-dispatcher-client-secret" +
            "&scope=" + Uri.EscapeDataString("events:follow events:publish");
        await _page.GotoAsync($"{_clientWebVitalsBaseUrl}/?{query}");
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByTestId("connectivity-status")).ToHaveTextAsync("online");
        // REPLAY mode (subscribeToEntity) catches up this test's own
        // already-published seed event on first subscribe -- the "Set
        // Amount" button enabling is the observable signal that
        // currentEntityId has been populated from it.
        await Assertions.Expect(_page.GetByTestId("set-amount")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await recorder.RecordStepAsync(_page, "Opening the Duplex Client, pointed (via query-string overrides, appConfig.ts's own documented mechanism) at this playbook's own throwaway demo entity. The header shows the real, automatically-detected connectivity state -- online -- and the seed order this test published is already loaded into the \"Dispatch a command\" panel below.");

        // --- Part 1: REAL automatic detection, via a genuine network drop ---
        await _context.SetOfflineAsync(true);
        await Assertions.Expect(_page.GetByTestId("connectivity-status")).ToHaveTextAsync("offline", new() { Timeout = 15_000 });
        await recorder.RecordStepAsync(_page, "Automatic detection: Playwright drops the real network (BrowserContext.SetOfflineAsync), which flips the browser's own navigator.onLine and fires a real 'offline' event -- useOnlineStatus's existing listener (no new code) reacts, and the header switches to \"offline\" with no manual action taken.");

        // Naive UI's n-input renders `data-testid` on its own wrapper <div>,
        // not the real <input> nested inside -- FillAsync needs the actual
        // editable element (found by actually running this: Playwright
        // refused with "Element is not an <input>... and does not have a
        // role allowing [aria-readonly]" against the bare testid locator).
        await _page.GetByTestId("amount-input").Locator("input").FillAsync("111");
        await _page.GetByTestId("set-amount").ClickAsync();
        await Assertions.Expect(_page.Locator(".app-content p", new() { HasText = "queued in the local outbox" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Dispatching a command while genuinely offline: the command is durably enqueued in the client's local outbox (IndexedDB, ADR-039) rather than lost or blocked -- the status message confirms it queued instead of dispatching.");

        await _context.SetOfflineAsync(false);
        await Assertions.Expect(_page.GetByTestId("connectivity-status")).ToHaveTextAsync("online", new() { Timeout = 15_000 });
        await Assertions.Expect(_page.Locator(".app-header p", new() { HasText = "0 command(s) queued" })).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await recorder.RecordStepAsync(_page, "Restoring the real network: the browser's own 'online' event fires automatically, useOnlineStatus's listener flushes the outbox with no user action, and the header's queued-command count returns to 0 -- the automatic reconnect-and-sync cycle, proven against a genuine network transition.");

        // --- Part 2: the new manual override, independent of real network state ---
        await _page.GetByTestId("force-offline").ClickAsync();
        await Assertions.Expect(_page.GetByTestId("connectivity-status")).ToHaveTextAsync("offline");
        await recorder.RecordStepAsync(_page, "Manual override: clicking the new \"Go Offline\" button (useConnectivityStore, added this pass) forces the app into the offline path even though the real network is still up -- for a deterministic demo/test, independent of actual connectivity.");

        await _page.GetByTestId("amount-input").Locator("input").FillAsync("222");
        await _page.GetByTestId("set-amount").ClickAsync();
        await Assertions.Expect(_page.Locator(".app-content p", new() { HasText = "queued in the local outbox" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "A command dispatched while manually forced offline queues exactly the same way a real-network drop does -- both paths converge on the one durable outbox, useEntityViewActions never distinguishes why it's offline.");

        await _page.GetByTestId("force-online").ClickAsync();
        await Assertions.Expect(_page.GetByTestId("connectivity-status")).ToHaveTextAsync("online");
        await Assertions.Expect(_page.Locator(".app-header p", new() { HasText = "0 command(s) queued" })).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await recorder.RecordStepAsync(_page, "Clicking \"Go Online\" clears the manual override and immediately flushes the outbox -- both the manually-queued and (from Part 1) any still-pending command are delivered, confirmed by the queued-command count returning to 0.");

        const string sequenceDiagram = """
            @startuml OfflineOutboxSync_Playbook_Sequence
            autonumber
            actor "Site staff" as user
            participant "Duplex Client\n(client-web-vitals)" as client
            participant "useOnlineStatus\n(real navigator events)" as onlineStatus
            participant "useConnectivityStore\n(manual override)" as connectivity
            database "Client Outbox\n(IndexedDB, ADR-039)" as outbox
            participant "eventstore" as server

            == Automatic detection (real network) ==
            user -> client: (network drops)
            onlineStatus -> onlineStatus: window 'offline' event -> isOnline = false
            user -> client: dispatch command (Amount)
            client -> outbox: enqueue (Pending) -- NOT flushed (isEffectivelyOnline() false)
            user -> client: (network restored)
            onlineStatus -> onlineStatus: window 'online' event -> isOnline = true
            onlineStatus -> client: onOnline() callback
            client -> outbox: flush()
            outbox -> server: publish queued command
            server --> outbox: 2xx -- entry marked Delivered

            == Manual override (buttons) ==
            user -> connectivity: click "Go Offline"
            connectivity -> connectivity: forcedOffline = true
            user -> client: dispatch command (Amount)
            client -> outbox: enqueue (Pending) -- NOT flushed (forcedOffline true)
            user -> connectivity: click "Go Online"
            connectivity -> connectivity: forcedOffline = false
            client -> outbox: flush()
            outbox -> server: publish queued command
            server --> outbox: 2xx -- entry marked Delivered
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Core -- Go Offline and Resync (Automatic Detection + Manual Override)", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "core", "user", "go-offline-and-resync.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
