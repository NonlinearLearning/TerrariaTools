using Domain.Execution;

namespace Application.Contracts.Execution;

/// <summary>
/// 改写计划 DTO。
/// </summary>
public sealed class RewritePlanDto
{
    public Guid Id { get; set; }

    public PlanMetadataDto Metadata { get; set; } = new();

    public IReadOnlyCollection<PlanChangeItemDto> ChangeItems { get; set; } = Array.Empty<PlanChangeItemDto>();

    public IReadOnlyCollection<PlanConflict> Conflicts { get; set; } = Array.Empty<PlanConflict>();
}

/// <summary>
/// 计划元数据 DTO。
/// </summary>
public sealed class PlanMetadataDto
{
    public string PlanName { get; set; } = string.Empty;

    public string CompilerVersion { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string? Note { get; set; }
}

/// <summary>
/// 计划项 DTO。
/// </summary>
public sealed class PlanChangeItemDto
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public PlanTargetDto PlanTarget { get; set; } = new();

    public PlanAction PlanAction { get; set; }

    public int Order { get; set; }

    public IReadOnlyCollection<PlanReason> Reasons { get; set; } = Array.Empty<PlanReason>();
}

/// <summary>
/// 计划目标 DTO。
/// </summary>
public sealed class PlanTargetDto
{
    public string DocumentPath { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    public string? MemberSignature { get; set; }

    public string? AnchorText { get; set; }
}

/// <summary>
/// 执行结果 DTO。
/// </summary>
public sealed class RewriteResultDto
{
    public Guid Id { get; set; }

    public Guid RewritePlanId { get; set; }

    public IReadOnlyCollection<FileChangeDto> FileChanges { get; set; } = Array.Empty<FileChangeDto>();

    public IReadOnlyCollection<ExecutionTraceDto> ExecutionTraces { get; set; } = Array.Empty<ExecutionTraceDto>();

    public IReadOnlyCollection<ExecutionFailureDto> ExecutionFailures { get; set; } =
        Array.Empty<ExecutionFailureDto>();
}

/// <summary>
/// 文件变更 DTO。
/// </summary>
public sealed class FileChangeDto
{
    public string DocumentPath { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public IReadOnlyCollection<string> AffectedTargets { get; set; } = Array.Empty<string>();
}

/// <summary>
/// 执行轨迹 DTO。
/// </summary>
public sealed class ExecutionTraceDto
{
    public Guid PlanChangeItemId { get; set; }

    public string StepName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>
/// 执行失败 DTO。
/// </summary>
public sealed class ExecutionFailureDto
{
    public Guid PlanChangeItemId { get; set; }

    public string FailureType { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool Retryable { get; set; }
}
