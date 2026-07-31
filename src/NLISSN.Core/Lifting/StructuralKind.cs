namespace NLISSN.Core.Lifting;

/// <summary>
/// Names a structure conclusion produced only by the Lift stage.
/// </summary>
public enum StructuralKind
{
  Assignment,
  LocalDefinition,
  If,
  Loop,
  Switch,
  ConditionalExpression,
  Return
}
