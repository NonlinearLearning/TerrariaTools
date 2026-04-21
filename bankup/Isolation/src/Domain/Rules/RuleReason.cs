namespace Domain.Rules;

/// <summary>
/// 表示规则理由。
/// </summary>
public sealed class RuleReason
{
    private readonly List<string> factReferences = new();
    private readonly List<string> relatedTargetKeys = new();

    /// <summary>
    /// 初始化规则理由。
    /// </summary>
    public RuleReason(
        string reasonCode,
        string message,
        RuleReasonKind reasonKind,
        IEnumerable<string>? factReferences = null,
        IEnumerable<string>? relatedTargetKeys = null,
        RuleReasonRiskLevel riskLevel = RuleReasonRiskLevel.None,
        string? traceSummary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ReasonCode = reasonCode.Trim();
        Message = message.Trim();
        ReasonKind = reasonKind;
        this.factReferences = NormalizeItems(factReferences);
        this.relatedTargetKeys = NormalizeItems(relatedTargetKeys);
        RiskLevel = riskLevel;
        TraceSummary = traceSummary?.Trim();
    }

    /// <summary>
    /// 获取理由编码。
    /// </summary>
    public string ReasonCode { get; }

    /// <summary>
    /// 获取人类可读消息。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 获取理由种类。
    /// </summary>
    public RuleReasonKind ReasonKind { get; }

    /// <summary>
    /// 获取事实引用集合。
    /// </summary>
    public IReadOnlyCollection<string> FactReferences => factReferences.AsReadOnly();

    /// <summary>
    /// 获取相关目标集合。
    /// </summary>
    public IReadOnlyCollection<string> RelatedTargetKeys => relatedTargetKeys.AsReadOnly();

    /// <summary>
    /// 获取风险级别。
    /// </summary>
    public RuleReasonRiskLevel RiskLevel { get; }

    /// <summary>
    /// 获取轨迹摘要。
    /// </summary>
    public string? TraceSummary { get; }

    /// <summary>
    /// 判断是否为阻断理由。
    /// </summary>
    public bool IsBlockingReason()
    {
        return ReasonKind is RuleReasonKind.Protection or RuleReasonKind.Rejection or RuleReasonKind.Failure;
    }

    /// <summary>
    /// 判断是否为证据理由。
    /// </summary>
    public bool IsEvidenceReason()
    {
        return ReasonKind == RuleReasonKind.Evidence;
    }

    private static List<string> NormalizeItems(IEnumerable<string>? items)
    {
        return items?.Where(static current => !string.IsNullOrWhiteSpace(current))
            .Select(static current => current.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList()
            ?? new List<string>();
    }
}

/// <summary>
/// 表示规则理由种类。
/// </summary>
public enum RuleReasonKind
{
    Unknown = 0,
    Match = 1,
    Skip = 2,
    Candidate = 3,
    Protection = 4,
    Conflict = 5,
    Rejection = 6,
    Evidence = 7,
    Failure = 8,
}

/// <summary>
/// 表示规则理由风险级别。
/// </summary>
public enum RuleReasonRiskLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}
