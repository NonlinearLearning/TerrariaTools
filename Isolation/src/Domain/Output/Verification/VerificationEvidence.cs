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
