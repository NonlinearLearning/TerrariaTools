namespace Domain.Common;

/// <summary>
/// 表示领域目标名称值对象。
/// </summary>
/// <param name="Value">标准化后的目标名称。</param>
public readonly record struct TargetName(string Value)
{
    /// <summary>
    /// 创建标准化后的目标名称。
    /// </summary>
    public static TargetName Create(string rawValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawValue);
        return new TargetName(rawValue.Trim());
    }

    /// <summary>
    /// 判断是否与输入名称相同。
    /// </summary>
    public bool Matches(string? other)
    {
        return !string.IsNullOrWhiteSpace(other)
            && string.Equals(Value, other.Trim(), StringComparison.Ordinal);
    }


    public override string ToString()
    {
        return Value;
    }
}
