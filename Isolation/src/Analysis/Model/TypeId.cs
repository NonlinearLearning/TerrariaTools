namespace Analysis.Model;

/// <summary>
/// 表示类型级目标标识。
/// </summary>
public sealed record TypeId(string Value) : TargetId(Value);
