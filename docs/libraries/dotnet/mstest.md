[← Libraries index](../README.md)

# MSTest (dotnet)

**What it's for:** Microsoft's own first-party .NET unit-testing
framework — attributes (`[TestClass]`/`[TestMethod]`), assertions
(`Assert`/`CollectionAssert`), and (since v3) a modernized runner via the
`Microsoft.Testing.Platform`, with no Visual Studio dependency required
to execute tests.

**Why bought, not built:** a test framework is exactly the kind of
general-purpose infrastructure `ADR-041` already says to buy rather than
build. Picked over `xUnit`/`NUnit` specifically for being Microsoft's
own first-party framework — the same reasoning that already picked
`Microsoft.Extensions.Compliance.Redaction` and `System.Text.Json` over
third-party equivalents — and because `Playwright` (`ADR-055`) directly
supports MSTest base classes, letting backend unit tests, integration
tests, and E2E UI tests all share one runner/assertion convention.

## General usage

```csharp
[TestClass]
public class EventAppenderTests
{
    [TestMethod]
    public async Task Append_ComputesChainHash_FromPriorEvent()
    {
        var appender = new EventAppender(fakeContext, fakeClock);
        var result = await appender.AppendAsync(newEvent);
        Assert.AreEqual(expectedHash, result.ChainHash);
    }
}
```

## Where this project uses it

`ADR-055` — the one test framework for `EventStore.UnitTests`,
`EventStore.IntegrationTests` (via `Testcontainers`), and
`EventStore.E2ETests` (via `Playwright`'s MSTest base classes).

## Links

- [learn.microsoft.com/dotnet/core/testing/unit-testing-mstest-intro](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-intro)
- [github.com/microsoft/testfx](https://github.com/microsoft/testfx)
