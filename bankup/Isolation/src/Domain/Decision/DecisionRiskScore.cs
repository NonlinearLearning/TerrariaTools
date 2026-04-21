namespace Domain.Decision;

/// <summary>
/// 表示决策风险评分。
/// </summary>
public sealed class DecisionRiskScore
{
    private DecisionRiskScore(int score, string reason)
    {
        Score = score;
        Reason = reason;
    }

    public int Score { get; }

    public string Reason { get; }

    public bool IsHighRisk => Score >= 80;

    public static DecisionRiskScore Low(string reason) => new(20, reason);

    public static DecisionRiskScore Medium(string reason) => new(50, reason);

    public static DecisionRiskScore High(string reason) => new(90, reason);
}
