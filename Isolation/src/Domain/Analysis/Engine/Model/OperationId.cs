namespace Domain.Analysis.Engine.Model;

/// <summary>
/// 表示操作级目标标识。
///
/// 它面向领域层表达“可执行、可求值、可参与规则判断”的行为目标，
/// 例如调用、控制结构等。
/// </summary>
public sealed record OperationId(string Value) : TargetId(Value);
