namespace Domain.Common;

/// <summary>
/// 表示成员签名值对象。
/// </summary>
/// <param name="Value">标准化后的成员签名。</param>
public readonly record struct MemberSignature(string Value)
{
    /// <summary>
    /// 创建成员签名。
    /// </summary>
    public static MemberSignature Create(string rawValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawValue);
        return new MemberSignature(rawValue.Trim());
    }

    /// <summary>
    /// 根据可空字符串创建成员签名。
    /// </summary>
    public static MemberSignature? CreateNullable(string? rawValue)
    {
        return string.IsNullOrWhiteSpace(rawValue) ? null : Create(rawValue);
    }


    public override string ToString()
    {
        return Value;
    }
}
