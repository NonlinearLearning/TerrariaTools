namespace Analysis.X2Cpg;

/// <summary>
/// 保存 x2cpg 前端共享常量。
///
/// 对应 Joern `Defines.scala`。这些值用于无法解析类型、方法或字段时保持
/// CPG 属性稳定。
/// </summary>
public static class Defines
{
    public const string Any = "ANY";
    public const string UnresolvedNamespace = "<unresolvedNamespace>";
    public const string UnresolvedSignature = "<unresolvedSignature>";
    public const string StaticInitMethodName = "<clinit>";
    public const string ConstructorMethodName = "<init>";
    public const string DynamicCallUnknownFullName = "<unknownFullName>";
    public const string ClosurePrefix = "<lambda>";
    public const string LeftAngularBracket = "<";
    public const string Unknown = "<unknown>";
    public const string UnknownField = "<unknownField>";
}
