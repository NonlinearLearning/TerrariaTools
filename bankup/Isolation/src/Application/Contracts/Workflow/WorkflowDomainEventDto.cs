namespace Application.Contracts.Workflow;

/// <summary>
/// 表示工作流领域事件 DTO。
/// </summary>
public sealed class WorkflowDomainEventDto
{
    public int Sequence { get; set; }

    public Guid EventId { get; set; }

    public string EventName { get; set; } = string.Empty;

    public string ContextName { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    public Guid CorrelationId { get; set; }

    public Guid? CausationId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Summary { get; set; } = string.Empty;
}
