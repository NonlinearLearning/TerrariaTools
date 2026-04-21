namespace Domain.Output.Verification;

/// <summary>
/// 表示行为证据。
/// </summary>
public sealed class BehaviorEvidence
{
    public BehaviorEvidence(string scenarioName, bool passed, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ScenarioName = scenarioName.Trim();
        Passed = passed;
        Summary = summary.Trim();
    }

    public string ScenarioName { get; }

    public bool Passed { get; }

    public string Summary { get; }
}
