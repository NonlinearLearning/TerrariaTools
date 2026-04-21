namespace Logic.Analysis.Engine.X2Cpg.DataStructures;

/// <summary>
/// 表示 CPG 转换阶段的作用域类型。
///
/// 对应 Joern `VariableScopeManager.ScopeType`。
/// </summary>
public enum ScopeType
{
    MethodScope,
    BlockScope,
    TypeDeclScope,
}
