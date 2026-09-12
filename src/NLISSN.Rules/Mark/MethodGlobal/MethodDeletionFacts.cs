using NLISSN.Core.Pipeline;

namespace NLISSN.Rules;

internal static class UnreachableMethodFacts
{
    internal static readonly RuleFactKind Marked = RuleFactKind.UnreachableMethodMarked;

    internal static readonly RuleFactKind Propagated = RuleFactKind.UnreachableMethodPropagated;

    internal static readonly RuleFactKind Lifted = RuleFactKind.UnreachableMethodLifted;
}

internal static class UnreferencedMethodFacts
{
    internal static readonly RuleFactKind Marked = RuleFactKind.UnreferencedMethodMarked;

    internal static readonly RuleFactKind Propagated = RuleFactKind.UnreferencedMethodPropagated;

    internal static readonly RuleFactKind Lifted = RuleFactKind.UnreferencedMethodLifted;
}
