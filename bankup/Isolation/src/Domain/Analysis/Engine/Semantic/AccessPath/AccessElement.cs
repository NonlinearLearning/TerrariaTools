namespace Domain.Analysis.Engine.Semantic.AccessPath;

/// <summary>
/// 表示访问路径中的单个访问元素。
///
/// 这部分对齐 Joern `AccessElement.scala` 的核心语义，
/// 但先保留当前 C# 版最需要的最小模型：
/// - 常量成员访问；
/// - 任意成员访问；
/// - 指针偏移；
/// - 解引用与取地址。
/// </summary>
public abstract record AccessElement(string DisplayText) : IComparable<AccessElement>
{
    /// <summary>
    /// 获取当前元素的稳定种类值。
    /// </summary>
    public abstract int Kind { get; }


    public int CompareTo(AccessElement? other)
    {
        if (other is null)
        {
            return 1;
        }

        int kindCompare = Kind.CompareTo(other.Kind);
        return kindCompare != 0
            ? kindCompare
            : string.Compare(DisplayText, other.DisplayText, StringComparison.Ordinal);
    }


    public sealed override string ToString() => DisplayText;
}

/// <summary>
/// 表示常量成员访问，例如字段名或属性名。
/// </summary>
public sealed record ConstantAccess(string Name) : AccessElement(Name)
{

    public override int Kind => 0x01010101;
}

/// <summary>
/// 表示任意成员访问。
/// </summary>
public sealed record VariableAccess() : AccessElement("?")
{

    public override int Kind => 0x02020202;

    /// <summary>
    /// 共享单例。
    /// </summary>
    public static VariableAccess Instance { get; } = new();
}

/// <summary>
/// 表示任意指针偏移。
/// </summary>
public sealed record VariablePointerShift() : AccessElement("<?>")
{

    public override int Kind => 0x03030303;

    /// <summary>
    /// 共享单例。
    /// </summary>
    public static VariablePointerShift Instance { get; } = new();
}

/// <summary>
/// 表示一次解引用。
/// </summary>
public sealed record IndirectionAccess() : AccessElement("*")
{

    public override int Kind => 0x04040404;

    /// <summary>
    /// 共享单例。
    /// </summary>
    public static IndirectionAccess Instance { get; } = new();
}

/// <summary>
/// 表示一次取地址。
/// </summary>
public sealed record AddressOf() : AccessElement("&")
{

    public override int Kind => 0x05050505;

    /// <summary>
    /// 共享单例。
    /// </summary>
    public static AddressOf Instance { get; } = new();
}

/// <summary>
/// 表示固定逻辑偏移。
/// </summary>
public sealed record PointerShift(int LogicalOffset) : AccessElement($"<{LogicalOffset}>")
{

    public override int Kind => 0x06060606;
}
