using System.Text.Json.Nodes;
using EventStore.Upcasting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventStore.IntegrationTests;

// ADR-053's own exit criterion for "Upcast Materialization + Downcast"
// (docs/08-build-plan.md): the same registered UpcastFromPrevious expression
// text must evaluate identically whichever IUpcastExpressionEvaluator is
// configured, for a mapping both CEL and JSONata can express. Pure --
// exercises UpcastChain directly against both evaluators, no DB/provider
// fan-out needed (nothing here touches EventStoreContext).
[TestClass]
public class UpcastExpressionEvaluatorTests
{
    [TestMethod]
    public void ASingleHopFieldRenameAndDefaultProducesTheSameResultUnderCelAndJsonata()
    {
        var payload = JsonNode.Parse("""{ "Amount": 100 }""")!;
        const string expressionList = "event.Amount as Amount, 'Unknown' as Status";
        var definitionsByVersion = new Dictionary<int, UpcastableVersion> { [2] = new(2, expressionList) };

        var celOutcome = new UpcastChain(new CelUpcastExpressionEvaluator()).Apply(definitionsByVersion, 1, 2, payload.DeepClone());
        var jsonataOutcome = new UpcastChain(new JsonataUpcastExpressionEvaluator()).Apply(definitionsByVersion, 1, 2, payload.DeepClone());

        Assert.IsInstanceOfType<UpcastOutcome.Success>(celOutcome);
        Assert.IsInstanceOfType<UpcastOutcome.Success>(jsonataOutcome);
        var celSuccess = (UpcastOutcome.Success)celOutcome;
        var jsonataSuccess = (UpcastOutcome.Success)jsonataOutcome;
        Assert.AreEqual(celSuccess.Payload.ToJsonString(), jsonataSuccess.Payload.ToJsonString());
        Assert.AreEqual(100L, (long)celSuccess.Payload["Amount"]!);
        Assert.AreEqual("Unknown", (string)celSuccess.Payload["Status"]!);
    }

    [TestMethod]
    public void AMultiHopChainProducesTheSameResultUnderCelAndJsonata()
    {
        var payload = JsonNode.Parse("""{ "Amount": 50 }""")!;
        var definitionsByVersion = new Dictionary<int, UpcastableVersion>
        {
            [2] = new(2, "event.Amount as Amount, 'Unknown' as Status"),
            [3] = new(3, "event.Amount as Amount, event.Status as Status, 'USD' as Currency"),
        };

        var celOutcome = new UpcastChain(new CelUpcastExpressionEvaluator()).Apply(definitionsByVersion, 1, 3, payload.DeepClone());
        var jsonataOutcome = new UpcastChain(new JsonataUpcastExpressionEvaluator()).Apply(definitionsByVersion, 1, 3, payload.DeepClone());

        Assert.IsInstanceOfType<UpcastOutcome.Success>(celOutcome);
        Assert.IsInstanceOfType<UpcastOutcome.Success>(jsonataOutcome);
        var celSuccess = (UpcastOutcome.Success)celOutcome;
        var jsonataSuccess = (UpcastOutcome.Success)jsonataOutcome;
        Assert.AreEqual(celSuccess.Payload.ToJsonString(), jsonataSuccess.Payload.ToJsonString());
        Assert.AreEqual("USD", (string)jsonataSuccess.Payload["Currency"]!);
    }

    [TestMethod]
    public void JsonataCompilesAndEvaluatesAnArrayAggregationExpressionCelHasNoNativeEquivalentFor()
    {
        // ADR-053's own stated reason for keeping JSONata as a supported
        // alternative -- $sum() over a sequence, which CEL cannot express.
        var payload = JsonNode.Parse("""{ "LineItems": [ { "Amount": 10 }, { "Amount": 25 } ] }""")!;
        var evaluator = new JsonataUpcastExpressionEvaluator();

        Assert.IsTrue(evaluator.TryCompile("$sum(event.LineItems.Amount)", out _));
        var result = evaluator.Evaluate("$sum(event.LineItems.Amount)", payload);

        Assert.AreEqual(35, (double)result!);
    }

    [TestMethod]
    public void TryCompileRejectsASyntacticallyBrokenExpressionUnderBothEngines()
    {
        Assert.IsFalse(new CelUpcastExpressionEvaluator().TryCompile("event.Amount +", out var celError));
        Assert.IsFalse(new JsonataUpcastExpressionEvaluator().TryCompile("event.[[[", out var jsonataError));
        Assert.IsNotNull(celError);
        Assert.IsNotNull(jsonataError);
    }
}
