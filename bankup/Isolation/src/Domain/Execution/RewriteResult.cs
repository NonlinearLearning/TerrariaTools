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
    private ExecutionStatus status;

    private RewriteResult(Guid id, Guid rewritePlanId)
        : base(id)
    {
        RewritePlanId = rewritePlanId;
        ProducedAt = DateTimeOffset.UtcNow;
        status = ExecutionStatus.Pending;
    }

    public Guid RewritePlanId { get; }

    public DateTimeOffset ProducedAt { get; }

    public IReadOnlyCollection<FileChange> FileChanges => fileChanges.AsReadOnly();

    public IReadOnlyCollection<ExecutionTrace> ExecutionTraces => executionTraces.AsReadOnly();

    public IReadOnlyCollection<ExecutionFailure> ExecutionFailures => executionFailures.AsReadOnly();

    public ExecutionStatus Status => status;

    public static RewriteResult Create(Guid rewritePlanId)
    {
        return new RewriteResult(Guid.NewGuid(), rewritePlanId);
    }

    public void StartExecution(Guid correlationId)
    {
        if (status != ExecutionStatus.Pending)
        {
            throw new InvalidOperationException("执行只能从待开始状态进入运行中。");
        }

        status = ExecutionStatus.Running;
    }

    public void AddFileChange(FileChange fileChange)
    {
        ArgumentNullException.ThrowIfNull(fileChange);
        EnsureExecutionOpen();
        bool exists = fileChanges.Any(current =>
            string.Equals(current.DocumentPath.Value, fileChange.DocumentPath.Value, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.Summary, fileChange.Summary, StringComparison.Ordinal));
        if (exists)
        {
            return;
        }

        fileChanges.Add(fileChange);
    }

    public void AddExecutionTrace(ExecutionTrace executionTrace)
    {
        ArgumentNullException.ThrowIfNull(executionTrace);
        EnsureExecutionOpen();
        bool exists = executionTraces.Any(current =>
            current.PlanChangeItemId == executionTrace.PlanChangeItemId &&
            string.Equals(current.StepName, executionTrace.StepName, StringComparison.Ordinal) &&
            string.Equals(current.Message, executionTrace.Message, StringComparison.Ordinal));
        if (exists)
        {
            return;
        }

        executionTraces.Add(executionTrace);
    }

    public void AddExecutionFailure(ExecutionFailure executionFailure)
    {
        ArgumentNullException.ThrowIfNull(executionFailure);
        EnsureExecutionOpen();
        bool exists = executionFailures.Any(current =>
            current.PlanChangeItemId == executionFailure.PlanChangeItemId &&
            string.Equals(current.FailureType, executionFailure.FailureType, StringComparison.Ordinal) &&
            string.Equals(current.Message, executionFailure.Message, StringComparison.Ordinal));
        if (exists)
        {
            return;
        }

        executionFailures.Add(executionFailure);
    }

    public void CompleteExecution(Guid correlationId)
    {
        EnsureExecutionOpen();
        EnsureHasObservedOutcome();
        status = ExecutionStatus.Completed;
        Guid resolvedCorrelationId = correlationId == Guid.Empty ? Id : correlationId;
        if (HasDomainEvent("ExecutionCompleted", resolvedCorrelationId))
        {
            return;
        }

        AddDomainEvent(new Events.ExecutionCompletedDomainEvent(
            Id,
            resolvedCorrelationId,
            fileChanges.Count,
            executionFailures.Count));
    }

    public void FailExecution(Guid planChangeItemId, string failureType, string message, bool retryable)
    {
        EnsureExecutionOpen();
        AddExecutionFailure(new ExecutionFailure(planChangeItemId, failureType, message, retryable));
    }

    public void MarkFileChanged(DocumentPath path, string summary, IReadOnlyCollection<string> affectedTargets)
    {
        AddFileChange(new FileChange(path, summary, affectedTargets));
    }

    public bool HasTerminalFailure()
    {
        return executionFailures.Any(static item => !item.Retryable);
    }

    public void EnsureExecutionOpen()
    {
        if (status == ExecutionStatus.Pending)
        {
            throw new InvalidOperationException("执行尚未开始，不能记录结果。");
        }

        if (status == ExecutionStatus.Completed)
        {
            throw new InvalidOperationException("执行已完成，不能继续记录结果。");
        }
    }

    private void EnsureHasObservedOutcome()
    {
        if (fileChanges.Count == 0 &&
            executionTraces.Count == 0 &&
            executionFailures.Count == 0)
        {
            throw new InvalidOperationException("执行结果至少需要一条文件变更、执行轨迹或执行失败后才能完成。");
        }
    }
}

public enum ExecutionStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
}

/// <summary>
/// 表示文件变更。
/// </summary>
public sealed class FileChange
{
    public FileChange(DocumentPath documentPath, string summary, IReadOnlyCollection<string> affectedTargets)
        : this(
            documentPath,
            summary,
            (affectedTargets ?? Array.Empty<string>()).Select(Domain.Common.TargetName.Create).ToArray())
    {
    }

    public FileChange(DocumentPath documentPath, string summary, IReadOnlyCollection<TargetName> affectedTargets)
    {
        ArgumentNullException.ThrowIfNull(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(affectedTargets);
        DocumentPath = documentPath;
        Summary = summary.Trim();
        AffectedTargetValues = affectedTargets.ToArray();
    }

    public DocumentPath DocumentPath { get; }

    public string Summary { get; }

    public IReadOnlyCollection<string> AffectedTargets => AffectedTargetValues.Select(static item => item.Value).ToArray();

    public IReadOnlyCollection<TargetName> AffectedTargetValues { get; }
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
