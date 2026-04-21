using Domain.Common;
using Domain.Output.Verification.Events;

namespace Domain.Output.Verification;

/// <summary>
/// 表示证据聚合根。
/// </summary>
public sealed class VerificationEvidence : AggregateRoot<Guid>
{
    private readonly List<CompilationEvidence> compilationEvidence = new();
    private readonly List<StaticReasoningEvidence> staticReasoningEvidence = new();
    private readonly List<BehaviorEvidence> behaviorEvidence = new();
    private bool isCollected;

    private VerificationEvidence(Guid id, Guid rewriteResultId)
        : base(id)
    {
        RewriteResultId = rewriteResultId;
        RiskSummary = RiskSummary.NotAssessed();
    }

    public Guid RewriteResultId { get; }

    public RiskSummary RiskSummary { get; private set; }

    public IReadOnlyCollection<CompilationEvidence> CompilationEvidence => compilationEvidence.AsReadOnly();

    public IReadOnlyCollection<StaticReasoningEvidence> StaticReasoningEvidence => staticReasoningEvidence.AsReadOnly();

    public IReadOnlyCollection<BehaviorEvidence> BehaviorEvidence => behaviorEvidence.AsReadOnly();

    public bool IsCollected => isCollected;

    public static VerificationEvidence Create(Guid rewriteResultId)
    {
        return new VerificationEvidence(Guid.NewGuid(), rewriteResultId);
    }

    public void AddCompilationEvidence(CompilationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        EnsureMutable();
        bool exists = compilationEvidence.Any(current =>
            current.Success == evidence.Success &&
            current.DiagnosticCount == evidence.DiagnosticCount &&
            string.Equals(current.Summary, evidence.Summary, StringComparison.Ordinal));
        if (exists)
        {
            return;
        }

        compilationEvidence.Add(evidence);
        RefreshRiskSummary();
    }

    public void AddStaticReasoningEvidence(StaticReasoningEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        EnsureMutable();
        bool exists = staticReasoningEvidence.Any(current =>
            string.Equals(current.SubjectName, evidence.SubjectName, StringComparison.Ordinal) &&
            string.Equals(current.Summary, evidence.Summary, StringComparison.Ordinal));
        if (exists)
        {
            return;
        }

        staticReasoningEvidence.Add(evidence);
        RefreshRiskSummary();
    }

    public void AddBehaviorEvidence(BehaviorEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        EnsureMutable();
        bool exists = behaviorEvidence.Any(current =>
            string.Equals(current.ScenarioName, evidence.ScenarioName, StringComparison.Ordinal) &&
            current.Passed == evidence.Passed &&
            string.Equals(current.Summary, evidence.Summary, StringComparison.Ordinal));
        if (exists)
        {
            return;
        }

        behaviorEvidence.Add(evidence);
        RefreshRiskSummary();
    }

    public void UpdateRiskSummary(RiskSummary riskSummary)
    {
        ArgumentNullException.ThrowIfNull(riskSummary);
        EnsureMutable();
        RiskSummary = riskSummary;
    }

    public bool HasFailures =>
        compilationEvidence.Any(static item => !item.Success) ||
        behaviorEvidence.Any(static item => !item.Passed);

    public void RefreshRiskSummary()
    {
        RiskSummary = RiskSummary.FromEvidence(this);
    }

    public void RecordCollected(Guid correlationId)
    {
        Collect(correlationId);
    }

    public void Collect(Guid correlationId)
    {
        EnsureCollectable();
        RefreshRiskSummary();
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("VerificationEvidenceCollected", resolvedCorrelationId))
        {
            return;
        }

        isCollected = true;
        AddDomainEvent(new VerificationEvidenceCollectedDomainEvent(
            Id,
            resolvedCorrelationId,
            RiskSummary.LevelName,
            RiskSummary.RequiresManualReview));
    }

    private void EnsureMutable()
    {
        if (isCollected)
        {
            throw new InvalidOperationException("验证证据已采集，不能继续修改。");
        }
    }

    private void EnsureCollectable()
    {
        if (compilationEvidence.Count == 0 &&
            staticReasoningEvidence.Count == 0 &&
            behaviorEvidence.Count == 0)
        {
            throw new InvalidOperationException("验证证据至少需要一条证据后才能采集。");
        }
    }
}

/// <summary>
/// 表示编译证据。
/// </summary>
public sealed class CompilationEvidence
{
    public CompilationEvidence(bool success, int diagnosticCount, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentOutOfRangeException.ThrowIfNegative(diagnosticCount);
        Success = success;
        DiagnosticCount = diagnosticCount;
        Summary = summary.Trim();
    }

    public bool Success { get; }

    public int DiagnosticCount { get; }

    public string Summary { get; }
}

/// <summary>
/// 表示静态推理证据。
/// </summary>
public sealed class StaticReasoningEvidence
{
    public StaticReasoningEvidence(string subjectName, string summary)
        : this(Domain.Common.TargetName.Create(subjectName), summary)
    {
    }

    public StaticReasoningEvidence(TargetName subjectName, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        SubjectNameValue = subjectName;
        Summary = summary.Trim();
    }

    public string SubjectName => SubjectNameValue.Value;

    public TargetName SubjectNameValue { get; }

    public string Summary { get; }
}

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

/// <summary>
/// 表示风险摘要。
/// </summary>
public enum RiskLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

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
