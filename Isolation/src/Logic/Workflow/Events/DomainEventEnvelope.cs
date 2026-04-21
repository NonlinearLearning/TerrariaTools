using Domain.Common.Events;

namespace Logic.Workflow.Events;

/// <summary>
/// 表示带顺序的领域事件信封。
/// </summary>
public sealed class DomainEventEnvelope
{
    public DomainEventEnvelope(int sequence, IDomainEvent domainEvent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentNullException.ThrowIfNull(domainEvent);
        Sequence = sequence;
        DomainEvent = domainEvent;
    }

    public int Sequence { get; }

    public IDomainEvent DomainEvent { get; }
}
