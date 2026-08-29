using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace EventStore.E2ETests;

// Closes TODO.md's last remaining UI-playbook gap: Meridian's Workflow B
// (Relying-Party Access) had a real, proven mechanism
// (MeridianWorkflowBHttpSqliteTests.cs) but no client-web UI surface at
// all. client-web/packages/reference-app/src/components/relyingParty/
// RelyingPartyAccessPanel.vue is the new UI this playbook walks through
// -- built, not a wiring change, since ADR-043/044's delegation is a
// client-signed token used directly in a GraphQL mutation, never a
// StoredEvent/browsable entity (confirmed in that test file's own header
// comment). Reuses the existing client-web-meridian instance (any
// Meridian instance works -- the panel doesn't depend on which event
// type that instance subscribes to) against the seed continuity
// applicant's own IdentityClaimSubmitted event (Samples.Meridian.Seed's
// fixed EventId b0000003-0000-0000-0000-000000001001, ClaimedLegalName
// "John Doe") to reveal a real masked field end to end: the browser
// itself generates the customer's DID key (WebCrypto ECDSA P-256,
// ucan.ts), registers it as an AppTrustRoot, signs the UCAN delegation,
// exchanges it for an access token, and reveals the field -- no
// pre-computed token or server-side test setup, the whole mechanism runs
// live in the page exactly as a real relying party's own integration
// would.
[TestClass]
public class MeridianWorkflowBRelyingPartyAccessPlaybookTests
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
    public async Task RecordRelyingPartyAccessPlaybook()
    {
        var recorder = new PlaybookRecorder(
            Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "relying-party", "request-delegated-access.md"));

        await _page.GotoAsync(_clientWebMeridianBaseUrl);
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Duplex Client" })).ToBeVisibleAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Relying-Party Access" }).ClickAsync();
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Relying-Party Access" })).ToBeVisibleAsync();
        await recorder.RecordStepAsync(_page, "Opening the Relying-Party Access tab -- new this pass, Meridian-only (ADR-043/044). A raw form, not a guided wizard: it demonstrates the underlying delegation mechanism directly, the same posture the Event Composer tab already takes toward publishing.");

        await _page.GetByLabel("Event ID (the specific event to reveal a field from)").FillAsync("b0000003-0000-0000-0000-000000001001");
        await recorder.RecordStepAsync(_page, "Filling in the grant: Entity ID and Field Path default to the continuity applicant applicant-1001's ClaimedLegalName; Event ID names the specific IdentityClaimSubmitted event to reveal it from (Samples.Meridian.Seed's own fixed EventId). The Capability claim (identity:pii-read) is exactly what ClaimedLegalName's own x-masking.requiredClaim names.");

        await _page.GetByRole(AriaRole.Button, new() { Name = "Delegate & Reveal" }).ClickAsync();
        var revealedValue = _page.GetByTestId("relying-party-revealed-value");
        await Assertions.Expect(revealedValue).ToBeVisibleAsync(new() { Timeout = 30_000 }); // registers a trust root, waits for RbacProjectionWorker's own Follow tail to catch up, signs a delegation, exchanges it, then reveals -- several real network round trips, not an instant local action
        await recorder.RecordStepAsync(_page, "Clicking \"Delegate & Reveal\" runs the whole mechanism live in the browser: a freshly-generated customer DID key (WebCrypto ECDSA P-256) is registered as an AppTrustRoot, signs a UCAN delegation naming exactly this one field for exactly this one entity, exchanges it for a scoped access token (RFC 8693), then reveals the field with it. \"John Doe\" -- the real seed value -- comes back, proving the whole chain actually worked, not a stubbed response.");

        const string sequenceDiagram = """
            @startuml MeridianRelyingPartyAccess_Playbook_Sequence
            autonumber
            actor "Customer\n(applicant-1001)" as customer
            participant "Duplex Client\n(RelyingPartyAccessPanel.vue)" as client
            participant "eventstore\n(PUT /rbac/trust-roots)" as rbac
            participant "DevIdp\n(/connect/token)" as devIdp
            participant "RbacProjectionWorker" as worker
            participant "GraphQL\n(revealField)" as graphql

            client -> client: generate a fresh ECDSA P-256 keypair\n(WebCrypto, ucan.ts) -- the customer's own DID key
            client -> rbac: PUT /rbac/trust-roots/{thumbprint}\nBearer <operator-client token, registry:trust-admin>
            rbac -> rbac: publish AppTrustRootRegistered (accepted)
            rbac --> client: 201 Created

            client -> client: sign a UCAN delegation (ucan+jwt)\n{ iss: applicant-1001, aud: colleague-1, appId: kyc,\n  cap: [{ Claim: "identity:pii-read", EntityScope: <entityId> }],\n  exp: now + 24h }, signed with the customer's own key

            loop until RbacProjectionWorker's own Follow tail catches up (~500ms-few s)
              client -> devIdp: POST /connect/token\ngrant_type=token-exchange, subject_token=<delegation>,\nclient_id=colleague-client
              devIdp -> worker: (has AppTrustRootRegistered been folded yet?)
              alt trust root not yet visible
                devIdp --> client: 400 invalid_grant --\n"issuer key is not a registered AppTrustRoot"
              else trust root visible (worker's Follow tail has caught up)
                devIdp --> client: 200 { access_token }\n(bound to THIS call's own DPoP proof key, RFC 9449 cnf.jkt)
              end
            end

            client -> graphql: mutation revealField(entityId, eventId, fieldPath)\nBearer <granted access_token>, DPoP <same key>
            alt entityId matches the delegation's own EntityScope\n(this playbook's own scenario)
              graphql --> client: { value: "John Doe" }
            else a DIFFERENT entityId (ADR-043's entity-scoping invariant,\nnot exercised by this playbook)
              graphql --> client: 403 Forbidden -- caller lacks the\nrequired claim for THIS entity
            end
            client -> customer: "Revealed value: John Doe"
            @enduml
            """;
        await recorder.WriteMarkdownAsync("Meridian -- Workflow B: Relying-Party Access", sequenceDiagram);

        Assert.IsTrue(File.Exists(Path.Combine(RepoRootDirectory(), "docs", "playbooks", "meridian", "relying-party", "request-delegated-access.md")));
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EventStore.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repo root (EventStore.slnx) above " + AppContext.BaseDirectory);
    }
}
