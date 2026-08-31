namespace NRulesDmnSpike;

public interface IAdverseEventClassifier
{
    string Classify(int severityScore, string eventType);
}
