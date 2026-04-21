using Domain.Rules;

namespace Domain.Decision;

/// <summary>
/// 表示保护条件。
/// </summary>
public sealed class DecisionProtection
{
    public DecisionProtection(Guid candidateId, RuleCode ruleCode, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        CandidateId = candidateId;
        RuleCode = ruleCode;
        Description = description.Trim();
    }

    public Guid CandidateId { get; }

    public RuleCode RuleCode { get; }

    public string Description { get; }
}
