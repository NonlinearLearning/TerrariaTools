namespace MinimalRoslynCpg.Contracts;

/// <summary>
/// 枚举最小 Roslyn CPG builder 产出的边种类。
/// </summary>
public enum RoslynCpgEdgeKind
{
    SyntaxChild,
    TokenChild,
    ParameterLink,
    DeclaresSymbol,
    ReferencesSymbol,
    Ref,
    HasType,
    EvalType,
    ReturnsType,
    ContainsSymbol,
    BaseType,
    InheritsFrom,
    RefersToType,
    SyntaxHasOperation,
    OpHasSyntax,
    OpResolvesToSymbol,
    CallTargets,
    AccessesMember,
    DecisionContains,
    DecisionRelation,
    OpChild,
    OpArgument,
    OpInstance,
    OpTarget,
    OpCondition,
    OpBody,
    OpWhenTrue,
    OpWhenFalse,
    CfgNext,
    CfgTrue,
    CfgFalse,
    DataFlow,
}
