using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis.Structure;

/// <summary>
/// Resolves rule-contract structures from Roslyn's public, strongly typed syntax model.
/// </summary>
public static class RuleSyntaxStructureCatalog
{
  /// <summary>
  /// Resolves a direct member of an <see cref="IfStatementSyntax"/>.
  /// </summary>
  public static RuleSyntaxStructureResolution Resolve(
    IfStatementSyntax ifStatement,
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role)
  {
    ArgumentNullException.ThrowIfNull(ifStatement);

    if (kind != RuleSyntaxStructureKind.If)
    {
      return RuleSyntaxStructureResolution.InvalidMember();
    }

    return role switch
    {
      RuleSyntaxStructureRole.Whole => ResolveRequired(ifStatement, kind, role),
      RuleSyntaxStructureRole.Condition => ResolveRequired(ifStatement.Condition, kind, role),
      RuleSyntaxStructureRole.ThenBranch => ResolveRequired(ifStatement.Statement, kind, role),
      RuleSyntaxStructureRole.ElseBranch => ResolveElseBranch(ifStatement, kind, role),
      RuleSyntaxStructureRole.ElseIf => ResolveElseIf(ifStatement, kind, role),
      _ => RuleSyntaxStructureResolution.InvalidMember()
    };
  }

  /// <summary>
  /// Resolves a local declaration as a whole Roslyn syntax structure.
  /// </summary>
  public static RuleSyntaxStructureResolution Resolve(
    VariableDeclaratorSyntax variableDeclarator,
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role)
  {
    ArgumentNullException.ThrowIfNull(variableDeclarator);

    return kind == RuleSyntaxStructureKind.VariableDeclarator &&
      role == RuleSyntaxStructureRole.Whole
      ? ResolveRequired(variableDeclarator, kind, role)
      : RuleSyntaxStructureResolution.InvalidMember();
  }

  /// <summary>
  /// Resolves a logical-and or logical-or expression as a whole typed structure.
  /// </summary>
  public static RuleSyntaxStructureResolution Resolve(
    BinaryExpressionSyntax logicalExpression,
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role)
  {
    ArgumentNullException.ThrowIfNull(logicalExpression);

    return kind == RuleSyntaxStructureKind.LogicalBinary &&
      role == RuleSyntaxStructureRole.Whole &&
      (logicalExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalAndExpression) ||
       logicalExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalOrExpression))
      ? ResolveRequired(logicalExpression, kind, role)
      : RuleSyntaxStructureResolution.InvalidMember();
  }

  /// <summary>
  /// Validates that a marked node is exactly the declared direct structure member.
  /// </summary>
  public static RuleSyntaxStructureResolution Validate(
    SyntaxNode syntaxNode,
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role)
  {
    ArgumentNullException.ThrowIfNull(syntaxNode);

    if (kind == RuleSyntaxStructureKind.VariableDeclarator &&
        role == RuleSyntaxStructureRole.Whole &&
        syntaxNode is VariableDeclaratorSyntax variableDeclarator)
    {
      return Resolve(variableDeclarator, kind, role);
    }

    if (kind == RuleSyntaxStructureKind.LogicalBinary &&
        role == RuleSyntaxStructureRole.Whole &&
        syntaxNode is BinaryExpressionSyntax logicalExpression)
    {
      return Resolve(logicalExpression, kind, role);
    }

    if (kind == RuleSyntaxStructureKind.ExpressionOrStatementHost &&
        role == RuleSyntaxStructureRole.Whole &&
        IsExpressionOrStatementHost(syntaxNode))
    {
      return ResolveRequired(syntaxNode, kind, role);
    }

    if (kind == RuleSyntaxStructureKind.DeclarationHost &&
        role == RuleSyntaxStructureRole.Whole &&
        IsDeclarationHost(syntaxNode))
    {
      return ResolveRequired(syntaxNode, kind, role);
    }

    if (IsParameterUsageDeclaration(kind, role, syntaxNode) ||
        IsParameterUsageCallsite(kind, role, syntaxNode) ||
        IsDelegateUsageDeclaration(kind, role, syntaxNode) ||
        IsDelegateUsageBinding(kind, role, syntaxNode) ||
        IsDelegateUsageCallsite(kind, role, syntaxNode))
    {
      return ResolveRequired(syntaxNode, kind, role);
    }

    return TryFindOwningIf(syntaxNode, role, out var ifStatement)
      ? ResolveAndMatch(ifStatement, syntaxNode, kind, role)
      : RuleSyntaxStructureResolution.InvalidMember();
  }

  /// <summary>
  /// Identifies the exact catalog role represented by an if-related syntax node.
  /// </summary>
  public static bool TryGetIfRole(SyntaxNode syntaxNode, out RuleSyntaxStructureRole role)
  {
    ArgumentNullException.ThrowIfNull(syntaxNode);

    if (syntaxNode is ElseClauseSyntax)
    {
      role = RuleSyntaxStructureRole.ElseBranch;
      return true;
    }

    if (syntaxNode is IfStatementSyntax ifStatement)
    {
      role = ifStatement.Parent is ElseClauseSyntax
        ? RuleSyntaxStructureRole.ElseIf
        : RuleSyntaxStructureRole.Whole;
      return true;
    }

    role = default;
    return false;
  }

  private static RuleSyntaxStructureResolution ResolveAndMatch(
    IfStatementSyntax ifStatement,
    SyntaxNode syntaxNode,
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role)
  {
    var result = Resolve(ifStatement, kind, role);
    return result.Status == RuleSyntaxStructureResolutionStatus.Resolved &&
        ReferenceEquals(result.Structure!.SyntaxNode, syntaxNode)
      ? result
      : RuleSyntaxStructureResolution.InvalidMember();
  }

  private static bool IsExpressionOrStatementHost(SyntaxNode syntaxNode)
  {
    return syntaxNode is ExpressionSyntax or
      StatementSyntax or
      VariableDeclaratorSyntax or
      ArgumentSyntax or
      BaseArgumentListSyntax or
      InterpolationSyntax or
      SwitchExpressionArmSyntax or
      ElseClauseSyntax or
      ArrowExpressionClauseSyntax or
      SwitchSectionSyntax;
  }

  private static bool IsDeclarationHost(SyntaxNode syntaxNode)
  {
    return syntaxNode is BaseListSyntax or
      DelegateDeclarationSyntax or
      EventDeclarationSyntax or
      EventFieldDeclarationSyntax or
      FieldDeclarationSyntax or
      IndexerDeclarationSyntax or
      LocalDeclarationStatementSyntax or
      MethodDeclarationSyntax or
      PropertyDeclarationSyntax or
      SimpleBaseTypeSyntax;
  }

  private static bool IsParameterUsageDeclaration(
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role,
    SyntaxNode syntaxNode)
  {
    return role == RuleSyntaxStructureRole.Declaration &&
      ((kind == RuleSyntaxStructureKind.MethodParameterUsage && syntaxNode is MethodDeclarationSyntax) ||
       (kind == RuleSyntaxStructureKind.LocalFunctionParameterUsage && syntaxNode is LocalFunctionStatementSyntax) ||
       (kind == RuleSyntaxStructureKind.IndexerParameterUsage && syntaxNode is IndexerDeclarationSyntax) ||
       (kind == RuleSyntaxStructureKind.ExtensionMethodParameterUsage && syntaxNode is MethodDeclarationSyntax));
  }

  private static bool IsParameterUsageCallsite(
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role,
    SyntaxNode syntaxNode)
  {
    return role == RuleSyntaxStructureRole.Callsite &&
      (((kind is RuleSyntaxStructureKind.MethodParameterUsage or RuleSyntaxStructureKind.LocalFunctionParameterUsage or RuleSyntaxStructureKind.ExtensionMethodParameterUsage) &&
        syntaxNode is InvocationExpressionSyntax) ||
       (kind == RuleSyntaxStructureKind.IndexerParameterUsage && syntaxNode is ElementAccessExpressionSyntax));
  }

  private static bool IsDelegateUsageDeclaration(
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role,
    SyntaxNode syntaxNode)
  {
    return kind == RuleSyntaxStructureKind.DelegateUsage &&
      role == RuleSyntaxStructureRole.Declaration &&
      syntaxNode is DelegateDeclarationSyntax;
  }

  private static bool IsDelegateUsageBinding(
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role,
    SyntaxNode syntaxNode)
  {
    return kind == RuleSyntaxStructureKind.DelegateUsage &&
      role == RuleSyntaxStructureRole.Binding &&
      syntaxNode is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax;
  }

  private static bool IsDelegateUsageCallsite(
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role,
    SyntaxNode syntaxNode)
  {
    return kind == RuleSyntaxStructureKind.DelegateUsage &&
      role == RuleSyntaxStructureRole.Callsite &&
      syntaxNode is InvocationExpressionSyntax;
  }

  private static bool TryFindOwningIf(
    SyntaxNode syntaxNode,
    RuleSyntaxStructureRole role,
    out IfStatementSyntax ifStatement)
  {
    ifStatement = null!;

    switch (role)
    {
      case RuleSyntaxStructureRole.Whole:
        if (syntaxNode is IfStatementSyntax wholeIf)
        {
          ifStatement = wholeIf;
          return true;
        }

        return false;
      case RuleSyntaxStructureRole.Condition:
      case RuleSyntaxStructureRole.ThenBranch:
        if (syntaxNode.Parent is IfStatementSyntax directOwner)
        {
          ifStatement = directOwner;
          return true;
        }

        return false;
      case RuleSyntaxStructureRole.ElseBranch:
        if (syntaxNode is ElseClauseSyntax elseClause &&
            elseClause.Parent is IfStatementSyntax elseOwner)
        {
          ifStatement = elseOwner;
          return true;
        }

        return false;
      case RuleSyntaxStructureRole.ElseIf:
        if (syntaxNode is IfStatementSyntax elseIf &&
            elseIf.Parent is ElseClauseSyntax parentElse &&
            parentElse.Parent is IfStatementSyntax parentIf)
        {
          ifStatement = parentIf;
          return true;
        }

        return false;
      default:
        return false;
    }
  }

  private static RuleSyntaxStructureResolution ResolveRequired(
    SyntaxNode syntaxNode,
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role)
  {
    return syntaxNode.IsMissing
      ? RuleSyntaxStructureResolution.IncompleteSyntax()
      : RuleSyntaxStructureResolution.Resolved(new RuleSyntaxStructureRef(kind, role, syntaxNode));
  }

  private static RuleSyntaxStructureResolution ResolveElseBranch(
    IfStatementSyntax ifStatement,
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role)
  {
    if (ifStatement.Else is null)
    {
      return RuleSyntaxStructureResolution.AbsentOptionalMember();
    }

    return ResolveRequired(ifStatement.Else, kind, role);
  }

  private static RuleSyntaxStructureResolution ResolveElseIf(
    IfStatementSyntax ifStatement,
    RuleSyntaxStructureKind kind,
    RuleSyntaxStructureRole role)
  {
    if (ifStatement.Else is null)
    {
      return RuleSyntaxStructureResolution.AbsentOptionalMember();
    }

    if (ifStatement.Else.IsMissing || ifStatement.Else.Statement.IsMissing)
    {
      return RuleSyntaxStructureResolution.IncompleteSyntax();
    }

    return ifStatement.Else.Statement is IfStatementSyntax elseIf
      ? RuleSyntaxStructureResolution.Resolved(new RuleSyntaxStructureRef(kind, role, elseIf))
      : RuleSyntaxStructureResolution.InvalidMember();
  }
}
