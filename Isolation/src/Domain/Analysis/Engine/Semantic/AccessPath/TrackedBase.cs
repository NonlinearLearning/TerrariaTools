namespace Domain.Analysis.Engine.Semantic.AccessPath;

/// <summary>
/// 表示数据流或访问路径分析时正在跟踪的基对象。
/// </summary>
public interface ITrackedBase
{
}

/// <summary>
/// 表示按名字跟踪的变量。
/// </summary>
public sealed record TrackedNamedVariable(string Name) : ITrackedBase;

/// <summary>
/// 表示调用返回值。
/// </summary>
public sealed record TrackedReturnValue(string Code) : ITrackedBase
{

    public override string ToString() => $"TrackedReturnValue({Code})";
}

/// <summary>
/// 表示字面量值。
/// </summary>
public sealed record TrackedLiteral(string Code) : ITrackedBase
{

    public override string ToString() => $"TrackedLiteral({Code})";
}

/// <summary>
/// 表示方法引用。
/// </summary>
public sealed record TrackedMethod(string Code) : ITrackedBase
{

    public override string ToString() => $"TrackedMethod({Code})";
}

/// <summary>
/// 表示类型引用。
/// </summary>
public sealed record TrackedTypeRef(string Code, string? EvalType = null) : ITrackedBase
{

    public override string ToString() => $"TrackedTypeRef({Code})";
}

/// <summary>
/// 表示别名表达式。
/// </summary>
public sealed record TrackedAlias(string Code) : ITrackedBase
{

    public override string ToString() => $"TrackedAlias({Code})";
}

/// <summary>
/// 表示未知跟踪源。
/// </summary>
public sealed record TrackedUnknown : ITrackedBase
{
    /// <summary>
    /// 共享单例。
    /// </summary>
    public static TrackedUnknown Instance { get; } = new();


    public override string ToString() => "TrackedUnknown";
}

/// <summary>
/// 表示形式返回值。
/// </summary>
public sealed record TrackedFormalReturn : ITrackedBase
{
    /// <summary>
    /// 共享单例。
    /// </summary>
    public static TrackedFormalReturn Instance { get; } = new();


    public override string ToString() => "TrackedFormalReturn";
}
