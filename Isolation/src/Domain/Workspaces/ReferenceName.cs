namespace Domain.Workspaces;

/// <summary>
/// 表示引用名称值对象。
/// </summary>
/// <param name="Value">标准化后的引用名称。</param>
public readonly record struct ReferenceName(string Value)
{
    public static ReferenceName Create(string rawValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawValue);
        return new ReferenceName(rawValue.Trim());
    }

    public override string ToString()
    {
        return Value;
    }
}
