namespace MinimalRoslynCpg.Contracts;

/// <summary>
/// Identifies the semantic relation represented by a decision edge.
/// </summary>
public enum RoslynCpgDecisionRelationKind
{
  AccessibilityToPrivate,
  ClearedTo,
  DerivedFrom,
  Inherits,
  ReducedTo,
  ReplacedWith,
}
