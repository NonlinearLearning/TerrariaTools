using Domain.Common.Events;

namespace Domain.Common;

/// <summary>
/// 定义实体的基础标识。
/// </summary>
/// <typeparam name="TId">实体标识类型。</typeparam>
public abstract class Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> domainEvents = new();

    /// <summary>
    /// 初始化实体。
    /// </summary>
    /// <param name="id">实体标识。</param>
    protected Entity(TId id)
    {
        Id = id;
    }

    /// <summary>
    /// 获取实体标识。
    /// </summary>
    public TId Id { get; }

    /// <summary>
    /// 获取实体记录的领域事件。
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => domainEvents.AsReadOnly();

    /// <summary>
    /// 清空当前实体已记录的领域事件。
    /// </summary>
    public void ClearDomainEvents()
    {
        domainEvents.Clear();
    }

    /// <summary>
    /// 记录领域事件。
    /// </summary>
    protected TDomainEvent AddDomainEvent<TDomainEvent>(TDomainEvent domainEvent)
        where TDomainEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        domainEvents.Add(domainEvent);
        return domainEvent;
    }

    /// <summary>
    /// 判断当前实体是否已经记录过指定事件。
    /// </summary>
    protected bool HasDomainEvent(string eventName, Guid correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        return domainEvents.Any(item =>
            item.CorrelationId == correlationId &&
            string.Equals(item.EventName, eventName, StringComparison.Ordinal));
    }
}
