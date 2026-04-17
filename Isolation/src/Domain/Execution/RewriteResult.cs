using Domain.Common;
using Domain.Workspaces;

namespace Domain.Execution;

/// <summary>
/// 表示执行结果聚合根。
/// </summary>
public sealed class RewriteResult : AggregateRoot<Guid>
{
    private readonly List<FileChange> fileChanges = new();
    private readonly List<ExecutionTrace> executionTraces = new();
    private readonly List<ExecutionFailure> executionFailures = new();

    private RewriteResult(Guid id, Guid rewritePlanId)
        : base(id)
    {
        RewritePlanId = rewritePlanId;
        ProducedAt = DateTimeOffset.UtcNow;
    }

    public Guid RewritePlanId { get; }

    public DateTimeOffset ProducedAt { get; }

    public IReadOnlyCollection<FileChange> FileChanges => fileChanges.AsReadOnly();

    public IReadOnlyCollection<ExecutionTrace> ExecutionTraces => executionTraces.AsReadOnly();

    public IReadOnlyCollection<ExecutionFailure> ExecutionFailures => executionFailures.AsReadOnly();

    public static RewriteResult Create(Guid rewritePlanId)
    {
        return new RewriteResult(Guid.NewGuid(), rewritePlanId);
    }

    public void AddFileChange(FileChange fileChange)
    {
        ArgumentNullException.ThrowIfNull(fileChange);
        fileChanges.Add(fileChange);
    }

    public void AddExecutionTrace(ExecutionTrace executionTrace)
    {
        ArgumentNullException.ThrowIfNull(executionTrace);
        executionTraces.Add(executionTrace);
    }

    public void AddExecutionFailure(ExecutionFailure executionFailure)
    {
        ArgumentNullException.ThrowIfNull(executionFailure);
        executionFailures.Add(executionFailure);
    }
}

/// <summary>
/// 表示文件变更。
/// </summary>
public sealed class FileChange
{
    public FileChange(DocumentPath documentPath, string summary, IReadOnlyCollection<string> affectedTargets)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        DocumentPath = documentPath;
        Summary = summary.Trim();
        AffectedTargets = affectedTargets;
    }

    public DocumentPath DocumentPath { get; }

    public string Summary { get; }

    public IReadOnlyCollection<string> AffectedTargets { get; }
}

/// <summary>
/// 表示执行轨迹。
/// </summary>
public sealed class ExecutionTrace
{
    public ExecutionTrace(Guid planChangeItemId, string stepName, string message, DateTimeOffset recordedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        PlanChangeItemId = planChangeItemId;
        StepName = stepName.Trim();
        Message = message.Trim();
        RecordedAt = recordedAt;
    }

    public Guid PlanChangeItemId { get; }

    public string StepName { get; }

    public string Message { get; }

    public DateTimeOffset RecordedAt { get; }
}

/// <summary>
/// 表示执行失败。
/// </summary>
public sealed class ExecutionFailure
{
    public ExecutionFailure(Guid planChangeItemId, string failureType, string message, bool retryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        PlanChangeItemId = planChangeItemId;
        FailureType = failureType.Trim();
        Message = message.Trim();
        Retryable = retryable;
    }

    public Guid PlanChangeItemId { get; }

    public string FailureType { get; }

    public string Message { get; }

    public bool Retryable { get; }
}
