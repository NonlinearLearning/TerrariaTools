namespace Sample;

/// <summary>
/// 表达式循环样例玩家。
/// </summary>
public sealed class Player
{
    /// <summary>
    /// 执行样例更新逻辑并返回是否允许。
    /// </summary>
    /// <param name="value">输入值。</param>
    /// <returns>是否允许。</returns>
    public bool Update(int value)
    {
        // dome:delete
        bool allowed = Run(value) && (value > 0);
        return allowed;
    }

    /// <summary>
    /// 执行基础判断逻辑。
    /// </summary>
    /// <param name="value">输入值。</param>
    /// <returns>判断结果。</returns>
    private static bool Run(int value)
    {
        return value > 0;
    }
}
