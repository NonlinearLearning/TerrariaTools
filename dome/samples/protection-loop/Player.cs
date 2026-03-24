namespace Sample;

/// <summary>
/// 样例物品类型。
/// </summary>
public sealed class Item
{
    /// <summary>
    /// 获取或设置物品值。
    /// </summary>
    public int Value { get; set; }
}

/// <summary>
/// 保护路径样例玩家。
/// </summary>
public sealed class Player
{
    /// <summary>
    /// 执行样例更新逻辑并返回结果。
    /// </summary>
    /// <param name="seed">输入种子值。</param>
    /// <returns>更新结果。</returns>
    public int Update(int seed)
    {
        // dome:delete
        int count = seed;
        var item = new Item { Value = count };
        int next = count;
        return next;
    }
}
