namespace NLCPG.Contracts;

/// 标识决策边表示的语义关系。
public enum NLCPGDecisionRelationKind
{
    AccessibilityToPrivate,
    ClearedTo,
    DerivedFrom,
    Inherits,
    ReducedTo,
    ReplacedWith,
}
