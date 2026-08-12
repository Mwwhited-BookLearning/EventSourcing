using EventStore.Upcasting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.UnitTests;

// ADR-053 -- "swappable per deployment via configuration... via ordinary
// DI configuration, with no core-engine code change." TODO.md had flagged
// this as not actually true: AddUpcasting() hardcoded CelUpcastExpression
// Evaluator with no configuration input at all. Proves the real switch
// registers the configured engine, not just that the code compiles.
[TestClass]
public class UpcastingConfigurationSwitchTests
{
    private static IUpcastExpressionEvaluator Resolve(string? engine)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(engine is null ? [] : [new KeyValuePair<string, string?>("Upcasting:Engine", engine)])
            .Build();
        var services = new ServiceCollection().AddUpcasting(configuration);
        return services.BuildServiceProvider().GetRequiredService<IUpcastExpressionEvaluator>();
    }

    [TestMethod]
    public void NoConfiguredEngineDefaultsToCel() =>
        Assert.IsInstanceOfType<CelUpcastExpressionEvaluator>(Resolve(engine: null));

    [TestMethod]
    public void AnUnrecognizedEngineValueAlsoDefaultsToCel() =>
        Assert.IsInstanceOfType<CelUpcastExpressionEvaluator>(Resolve(engine: "NotARealEngine"));

    [TestMethod]
    public void ConfiguringJsonataSwitchesTheRegisteredImplementation() =>
        Assert.IsInstanceOfType<JsonataUpcastExpressionEvaluator>(Resolve(engine: "Jsonata"));

    [TestMethod]
    public void TheEngineNameIsCaseInsensitive() =>
        Assert.IsInstanceOfType<JsonataUpcastExpressionEvaluator>(Resolve(engine: "JSONATA"));
}
