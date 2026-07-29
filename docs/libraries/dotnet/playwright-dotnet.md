[← Libraries index](../README.md)

# Playwright for .NET (dotnet)

**What it's for:** Microsoft's own cross-browser (Chromium, Firefox,
WebKit) end-to-end browser automation framework — drives a real browser
against a real running application, asserting on rendered UI state and
real user interactions, not a simulated DOM.

**Why bought, not built:** browser automation (protocol-level control of
three independent rendering engines) is a large, general problem with no
project-specific value in reimplementing it. Picked per direct prior
experience and because it's Microsoft's own tool (`ADR-041`'s first-party
preference), with official MSTest base classes (`docs/libraries/dotnet/
mstest.md`) letting E2E UI tests share one test-runner convention with
every other backend test in this design.

## General usage

```csharp
[TestClass]
public class OrderTableTests : PageTest
{
    [TestMethod]
    public async Task PublishingOrder_AppearsInTable()
    {
        await Page.GotoAsync("https://localhost:5173/orders");
        await Page.ClickAsync("button#new-order");
        await Expect(Page.Locator("table tr")).ToHaveCountAsync(1);
    }
}
```

## Where this project uses it

`ADR-055` — UI action/E2E tests (`EventStore.E2ETests`) driving
`ADR-039`'s Vue/MVVM client through a real browser against a real
running deployment (or a `docker-compose`/Aspire-orchestrated test
environment, `ADR-026`).

## Links

- [playwright.dev/dotnet](https://playwright.dev/dotnet/)
- [github.com/microsoft/playwright-dotnet](https://github.com/microsoft/playwright-dotnet)
