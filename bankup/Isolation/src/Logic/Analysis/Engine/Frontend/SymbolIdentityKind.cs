namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 表示稳定符号标识的分类。
/// </summary>
public enum SymbolIdentityKind
{
    Unknown = 0,
    Method = 1,
    Parameter = 2,
    Local = 3,
    Field = 4,
    Property = 5,
    NamedType = 6,
}
