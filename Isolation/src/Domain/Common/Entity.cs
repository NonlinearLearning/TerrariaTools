namespace Domain.Common;

/// <summary>
/// 定义实体的基础标识。
/// </summary>
/// <typeparam name="TId">实体标识类型。</typeparam>
public abstract class Entity<TId>
    where TId : notnull
{
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
}
