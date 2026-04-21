namespace Application.Contracts.Output.Verification;

/// <summary>
/// 行为证据 DTO。
/// </summary>
public sealed class BehaviorEvidenceDto
{
    public string ScenarioName { get; set; } = string.Empty;

    public bool Passed { get; set; }

    public string Summary { get; set; } = string.Empty;
}
