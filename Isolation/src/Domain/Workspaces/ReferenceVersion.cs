namespace Domain.Workspaces;

/// <summary>
/// 表示引用版本值对象。
/// </summary>
/// <param name="Value">标准化后的引用版本。</param>
public readonly record struct ReferenceVersion(string Value)
{
    public static ReferenceVersion Create(string rawValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawValue);
        return new ReferenceVersion(rawValue.Trim());
    }

    public override string ToString()
    {
        return Value;
    }
}
