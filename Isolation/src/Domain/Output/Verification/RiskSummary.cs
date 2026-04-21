namespace Domain.Output.Verification;

/// <summary>
/// 表示风险摘要。
/// </summary>
public sealed class RiskSummary
{
    public RiskSummary(string levelName, bool requiresManualReview, IReadOnlyCollection<string> items)
        : this(ParseLevel(levelName), requiresManualReview, items)
    {
    }

    public RiskSummary(RiskLevel level, bool requiresManualReview, IReadOnlyCollection<string> items)
    {
        if (level == RiskLevel.Unknown && requiresManualReview)
        {
            throw new InvalidOperationException("未评估风险不能直接要求人工复核。");
        }

        Level = level;
        RequiresManualReview = requiresManualReview;
        Items = items ?? Array.Empty<string>();
    }

    public RiskLevel Level { get; }

    public string LevelName => Level switch
    {
        RiskLevel.Unknown => "未评估",
        _ => Level.ToString(),
    };

    public bool RequiresManualReview { get; }

    public IReadOnlyCollection<string> Items { get; }

    public static RiskSummary NotAssessed()
    {
        return new RiskSummary(RiskLevel.Unknown, false, Array.Empty<string>());
    }

    /// <summary>
    /// 基于证据聚合计算风险摘要。
    /// </summary>
    public static RiskSummary FromEvidence(VerificationEvidence verificationEvidence)
    {
        ArgumentNullException.ThrowIfNull(verificationEvidence);
        bool hasCompilationFailure = verificationEvidence.CompilationEvidence.Any(item => !item.Success);
        bool hasBehaviorFailure = verificationEvidence.BehaviorEvidence.Any(item => !item.Passed);
        bool requiresManualReview = hasCompilationFailure || hasBehaviorFailure;
        RiskLevel level = requiresManualReview ? RiskLevel.High : RiskLevel.Low;
        List<string> items = new();

        if (hasCompilationFailure)
        {
            items.Add("编译验证失败。");
        }

        if (hasBehaviorFailure)
        {
            items.Add("行为验证失败。");
        }

        if (items.Count == 0)
        {
            items.Add("未发现额外风险。");
        }

        return new RiskSummary(level, requiresManualReview, items);
    }

    private static RiskLevel ParseLevel(string levelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(levelName);
        string normalized = levelName.Trim();
        return normalized switch
        {
            "未评估" => RiskLevel.Unknown,
            "Low" => RiskLevel.Low,
            "Medium" => RiskLevel.Medium,
            "High" => RiskLevel.High,
            _ => throw new InvalidOperationException($"未知风险级别：{normalized}"),
        };
    }
}
