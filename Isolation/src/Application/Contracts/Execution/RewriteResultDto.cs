using Application.Contracts;

namespace Application.Contracts.Execution;

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
