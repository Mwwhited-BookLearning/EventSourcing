[← Libraries index](../README.md)

# Moq (dotnet)

**What it's for:** the most widely-used .NET mocking library — fluent
`Mock<T>` setup/verification for stubbing interfaces and virtual members
in unit tests, so a test can isolate one class from its real
dependencies (a real `IEventLineageQueryProvider`, a real `IErasureKeyStore`,
...) without spinning them up.

**Why bought, not built:** hand-rolling test doubles for every interface
this design defines (`IMaskingStrategy`, `IPayloadMasker`, `IProjection<T>`,
...) is pure boilerplate a mocking library exists specifically to remove.

**One honest flag, not a silent pick — `ADR-055`**: `Moq`'s 2023
`SponsorLink` incident (a build-time NuGet package that collected hashed
email addresses without clear consent) caused real, lasting community
trust damage; many teams permanently migrated to `NSubstitute` even
after the feature was removed. `Moq` itself is technically sound today
and remains the most widely used .NET mocking library — adopted here per
direct preference, with this caveat recorded so the choice is informed.
Nothing in this design's test suites depends on a `Moq`-specific API
surface, so swapping to `NSubstitute` later, if trust becomes a real
concern, is a mechanical change, not a redesign.

## General usage

```csharp
var mockMasker = new Mock<IMaskingStrategy>();
mockMasker.Setup(m => m.Mask(It.IsAny<JsonNode>(), It.IsAny<JsonObject>()))
          .Returns(JsonValue.Create("***"));

var result = mockMasker.Object.Mask(realValue, config);
mockMasker.Verify(m => m.Mask(realValue, config), Times.Once);
```

## Where this project uses it

`ADR-055` — the mocking library for `EventStore.UnitTests`, isolating
each class under test from the real implementations of every seam
catalogued in `docs/extensibility-points.md`.

## Links

- [github.com/devlooped/moq](https://github.com/devlooped/moq)
- [documentation.help/Moq](https://github.com/devlooped/moq/wiki/Quickstart)
