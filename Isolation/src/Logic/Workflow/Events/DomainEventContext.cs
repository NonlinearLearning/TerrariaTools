namespace Logic.Workflow.Events;

/// <summary>
/// 表示统一领域事件上下文。
/// </summary>
public sealed class DomainEventContext
{
    public Guid CorrelationId { get; init; }

    public Guid? CausationId { get; init; }

    public string ContextName { get; init; } = string.Empty;

    public string StageName { get; init; } = string.Empty;
}
