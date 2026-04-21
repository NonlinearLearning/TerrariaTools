namespace Domain.Rules;

/// <summary>
/// 表示规则参与模式。
/// </summary>
public enum RuleParticipationMode
{
    Unknown = 0,
    MarkOnly = 1,
    Candidate = 2,
    Protection = 3,
    Decision = 4,
    EvidenceOnly = 5,
}
