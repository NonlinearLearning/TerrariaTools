namespace Domain.Common;

/// <summary>
/// 标记聚合根实体。
/// </summary>
/// <typeparam name="TId">聚合根标识类型。</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    /// <summary>
    /// 初始化聚合根。
    /// </summary>
    /// <param name="id">聚合根标识。</param>
    protected AggregateRoot(TId id)
        : base(id)
    {
    }
}
