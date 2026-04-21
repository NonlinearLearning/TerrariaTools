using Domain.Execution;

namespace Logic.Propagation.Events;

/// <summary>
/// 表示传播阶段领域事件发布输入。
/// </summary>
public sealed class PropagationDomainEventPublishInput
{
    public Guid RunCorrelationId { get; init; }

    public Guid WorkspaceContextId { get; init; }

    public PropagationResolution Resolution { get; init; } = null!;

    public PlanAction? PlanAction { get; init; }

    public Guid? ParentCandidateId { get; init; }

    public string? ParentTargetName { get; init; }

    public IReadOnlyCollection<LinkedActionDescriptor> LinkedActions { get; init; } = Array.Empty<LinkedActionDescriptor>();
}

/// <summary>
/// 表示联动动作描述。
/// </summary>
public sealed class LinkedActionDescriptor
{
    public string ActionName { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
}
