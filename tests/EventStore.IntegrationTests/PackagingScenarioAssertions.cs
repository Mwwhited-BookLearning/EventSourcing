using EventStore.Attachments;
using EventStore.Erasure;
using EventStore.Masking;
using EventStore.Streaming;
using EventStore.Upcasting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// "Release Engineering, Packaging & Supply Chain" (docs/08-build-plan.md,
// ADR-062) -- the exit criterion's own text: "EventStore.Abstractions
// contains only interfaces, no implementation, confirmed by a build-time
// check." A durable, automated check rather than a one-time manual
// eyeball of the assembly's contents.
[TestClass]
public class PackagingScenarioAssertions
{
    [TestMethod]
    public void EveryPublicTypeInEventStoreAbstractionsIsAnInterface()
    {
        // Each moved interface kept its ORIGINAL namespace (EventStore.
        // Masking, EventStore.Erasure, ...) to avoid a consumer-side
        // `using` churn across every implementation -- assembly identity
        // (what ships in EventStore.Abstractions.dll) and C# namespace are
        // orthogonal. typeof(IMaskingStrategy).Assembly pins the actual
        // EventStore.Abstractions assembly the reflection below scans.
        var assembly = typeof(IMaskingStrategy).Assembly;
        var publicTypes = assembly.GetExportedTypes();

        Assert.IsTrue(publicTypes.Length > 0, "the assembly should export at least the catalogued interfaces");
        foreach (var type in publicTypes)
            Assert.IsTrue(type.IsInterface, $"{type.FullName} is not an interface -- EventStore.Abstractions must carry only interfaces, no implementation (ADR-062)");
    }

    [TestMethod]
    public void EventStoreAbstractionsCarriesEveryCurrentlyBuiltImplementerFacingSeam()
    {
        // The 5 catalogued interfaces (docs/extensibility-points.md) that
        // are genuinely implementer-facing with no back-reference into the
        // engine's own internals -- see EventStore.Abstractions.csproj's
        // own header comment for why IEventLineageQueryProvider/
        // IJsonPathTranslator and IProjection<T>/IInterchangeFormatAdapter
        // are deliberately NOT here.
        var assembly = typeof(IMaskingStrategy).Assembly;
        var interfaceNames = assembly.GetExportedTypes().Where(t => t.IsInterface).Select(t => t.Name).ToHashSet();

        var expected = new[]
        {
            nameof(IMaskingStrategy),
            nameof(IStreamRedactionStrategy),
            nameof(IUpcastExpressionEvaluator),
            nameof(IErasureKeyStore),
            nameof(IAttachmentContentStore),
        };
        foreach (var name in expected)
            Assert.IsTrue(interfaceNames.Contains(name), $"expected {name} in EventStore.Abstractions");
        Assert.AreEqual(expected.Length, interfaceNames.Count, "an interface was added or removed without updating this list -- keep both in sync");
    }
}
