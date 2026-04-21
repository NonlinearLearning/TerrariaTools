namespace Domain.Rules;

/// <summary>
/// 表示规则优先级。
/// </summary>
public readonly record struct RulePriority : IComparable<RulePriority>, Domain.Common.ISharedKernelType
{
    /// <summary>
    /// 初始化规则优先级。
    /// </summary>
    /// <param name="value">优先级值。</param>
    public RulePriority(int value)
    {
        Value = value;
    }

    /// <summary>
    /// 获取优先级数值。值越大，优先级越高。
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// 获取最高优先级。
    /// </summary>
    public static RulePriority Highest => new(400);

    /// <summary>
    /// 获取高优先级。
    /// </summary>
    public static RulePriority High => new(300);

    /// <summary>
    /// 获取普通优先级。
    /// </summary>
    public static RulePriority Normal => new(200);

    /// <summary>
    /// 获取低优先级。
    /// </summary>
    public static RulePriority Low => new(100);

    /// <summary>
    /// 获取最低优先级。
    /// </summary>
    public static RulePriority Lowest => new(0);

    /// <summary>
    /// 比较优先级。
    /// </summary>
    public int CompareTo(RulePriority other)
    {
        return Value.CompareTo(other.Value);
    }

    /// <summary>
    /// 判断当前优先级是否高于另一个优先级。
    /// </summary>
    public bool HigherThan(RulePriority other)
    {
        return Value > other.Value;
    }

    /// <summary>
    /// 判断当前优先级是否低于另一个优先级。
    /// </summary>
    public bool LowerThan(RulePriority other)
    {
        return Value < other.Value;
    }

    /// <summary>
    /// 返回字符串表示。
    /// </summary>
    public override string ToString()
    {
        return Value.ToString();
    }
}
