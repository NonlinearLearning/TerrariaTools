using NLISSN.Core.Pipeline;

namespace NLISSN.Rules;

internal static class InternalOnlyPublicMethodFacts
{
  internal static readonly RuleFactKind Marked = RuleFactKind.InternalOnlyPublicMethodMarked;

  internal static readonly RuleFactKind Propagated = RuleFactKind.InternalOnlyPublicMethodPropagated;

  internal static readonly RuleFactKind Lifted = RuleFactKind.InternalOnlyPublicMethodLifted;
}

internal static class UnusedInterfaceImplementationFacts
{
  internal static readonly RuleFactKind Marked = RuleFactKind.UnusedInterfaceImplementationMarked;

  internal static readonly RuleFactKind Propagated = RuleFactKind.UnusedInterfaceImplementationPropagated;

  internal static readonly RuleFactKind Lifted = RuleFactKind.UnusedInterfaceImplementationLifted;
}
