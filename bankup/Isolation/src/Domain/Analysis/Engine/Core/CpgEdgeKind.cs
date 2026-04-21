namespace Domain.Analysis.Engine.Core;

/// <summary>
/// 定义阶段一、阶段二需要的核心边类型。
///
/// 阶段一重点是结构边，例如 AST 和 CONTAINS。
/// 阶段二重点是语义边，例如类型关系、调用关系、控制流关系。
/// 当前已经开始补最小数据流层，因此这里补上 `ReachingDef`。
/// </summary>
public enum CpgEdgeKind
{
    Unknown = 0,
    Ast,
    Contains,
    TaggedBy,
    Ref,
    EvalType,
    TypeRef,
    InheritsFrom,
    AliasOf,
    Call,
    MethodRef,
    ParameterLink,
    Cfg,
    Dominates,
    PostDominates,
    Cdg,
    ReachingDef,
    Argument,
    Receiver,
    Condition,
    TrueBody,
    FalseBody,
    DoBody,
    TryBody,
    CatchBody,
    FinallyBody,
    ForInit,
    ForUpdate,
    ForBody,
    Binds,
    Capture,
    IsCallForImport,
}
