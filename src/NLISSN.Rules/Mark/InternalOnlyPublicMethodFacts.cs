using NLISSN.Core.Pipeline;

namespace NLISSN.Rules;

internal static class InternalOnlyPublicMethodFacts
{
  internal static readonly RuleSemanticTag Marked = new("InternalOnlyPublicMethod.Marked");

  internal static readonly RuleSemanticTag Propagated = new("InternalOnlyPublicMethod.Propagated");

  internal static readonly RuleSemanticTag Lifted = new("InternalOnlyPublicMethod.Lifted");
}

internal static class UnusedInterfaceImplementationFacts
{
  internal static readonly RuleSemanticTag Marked = new("UnusedInterfaceImplementation.Marked");

  internal static readonly RuleSemanticTag Propagated = new("UnusedInterfaceImplementation.Propagated");

  internal static readonly RuleSemanticTag Lifted = new("UnusedInterfaceImplementation.Lifted");
}
