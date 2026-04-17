namespace Analysis.Model;

/// <summary>
/// 表示符号级目标标识。
///
/// 这里的“符号”故意做得比单一声明种类更宽，
/// 在 Joern 语境里它可以覆盖方法、字段、局部变量、参数等实体，
/// 目的是给上层领域逻辑提供一个稳定统一的身份入口。
/// </summary>
public sealed record SymbolId(string Value) : TargetId(Value);
