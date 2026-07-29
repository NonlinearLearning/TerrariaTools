using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Analysis.Structure;

/// <summary>
/// Identifies a Roslyn syntax structure family that can participate in a rule contract.
/// </summary>
public enum RuleSyntaxStructureKind
{
  If = 0,
  VariableDeclarator = 1,
  LogicalBinary = 2,
  ExpressionOrStatementHost = 3,
  DeclarationHost = 4,
  MethodParameterUsage = 5,
  LocalFunctionParameterUsage = 6,
  IndexerParameterUsage = 7,
  DelegateUsage = 8,
  ExtensionMethodParameterUsage = 9
}

/// <summary>
/// Identifies one direct member role inside a syntax structure family.
/// </summary>
public enum RuleSyntaxStructureRole
{
  Whole = 0,
  Condition = 1,
  ThenBranch = 2,
  ElseBranch = 3,
  ElseIf = 4,
  Declaration = 5,
  Callsite = 6,
  Binding = 7
}

/// <summary>
/// Describes whether a requested direct syntax member exists and is usable.
/// </summary>
public enum RuleSyntaxStructureResolutionStatus
{
  Resolved = 0,
  AbsentOptionalMember = 1,
  IncompleteSyntax = 2,
  InvalidMember = 3
}

/// <summary>
/// Keeps the original Roslyn node for one resolved structure member.
/// </summary>
public sealed record RuleSyntaxStructureRef(
  RuleSyntaxStructureKind Kind,
  RuleSyntaxStructureRole Role,
  SyntaxNode SyntaxNode);

/// <summary>
/// Returns a resolved structure member or an explicit reason it is unavailable.
/// </summary>
public sealed record RuleSyntaxStructureResolution(
  RuleSyntaxStructureResolutionStatus Status,
  RuleSyntaxStructureRef? Structure)
{
  public static RuleSyntaxStructureResolution Resolved(RuleSyntaxStructureRef structure)
  {
    return new RuleSyntaxStructureResolution(RuleSyntaxStructureResolutionStatus.Resolved, structure);
  }

  public static RuleSyntaxStructureResolution AbsentOptionalMember()
  {
    return new RuleSyntaxStructureResolution(RuleSyntaxStructureResolutionStatus.AbsentOptionalMember, null);
  }

  public static RuleSyntaxStructureResolution IncompleteSyntax()
  {
    return new RuleSyntaxStructureResolution(RuleSyntaxStructureResolutionStatus.IncompleteSyntax, null);
  }

  public static RuleSyntaxStructureResolution InvalidMember()
  {
    return new RuleSyntaxStructureResolution(RuleSyntaxStructureResolutionStatus.InvalidMember, null);
  }
}
