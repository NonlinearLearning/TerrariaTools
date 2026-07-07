using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Analysis;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;

namespace Rules;

internal static class RuleAnalysisHelpers
{
  public static IEnumerable<ExpressionSyntax> EnumerateAllowedExpressions(SyntaxNode root, IReadOnlyCollection<SyntaxKind> allowedKinds, CpgAnalysisContext context)
  {
    foreach (var expression in new AtomicExpressionAnalyzer().Analyze(root)) {
      if (!allowedKinds.Contains(expression.Kind())) {
        continue;
      }

      if (!TryAnalyzeExpression(expression, context, out _)) {
        continue;
      }

      yield return expression;
    }
  }

  public static IEnumerable<MethodDeclarationSyntax> EnumerateMethodDeclarations(SyntaxNode root, CpgAnalysisContext context)
  {
    foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()) {
      var analysis = new DefinitionStructureAnalyzer().Analyze(method, context);
      if (analysis.AffectedSyntaxTree.Contains(method)) {
        yield return method;
      }
    }
  }

  public static ExpressionSyntax? FindLogicalHost(ExpressionSyntax expression, CpgAnalysisContext context)
  {
    ExpressionSyntax? logicalHost = null;

    for (var current = expression.Parent as ExpressionSyntax;
         current is not null;
         current = current.Parent as ExpressionSyntax) {
      if (!current.IsKind(SyntaxKind.LogicalAndExpression) &&
          !current.IsKind(SyntaxKind.LogicalOrExpression)) {
        break;
      }

      var analysis = new BinaryExpressionAnalyzer().Analyze(
        (BinaryExpressionSyntax)current,
        expression,
        context);
      if (!analysis.AffectedSyntaxTree.Any(node => ReferenceEquals(node, expression))) {
        break;
      }

      logicalHost = current;
    }

    return logicalHost;
  }

  public static SyntaxNode? FindStructuralHost(ExpressionSyntax expression, CpgAnalysisContext context)
  {
    foreach (var ancestor in expression.Ancestors()) {
      if (TryResolveStructuralHost(ancestor, expression, context, out var host)) {
        return host;
      }

      if (ancestor is StatementSyntax statement) {
        return statement;
      }
    }

    return expression.FirstAncestorOrSelf<StatementSyntax>();
  }

  public static SyntaxNode? FindAssignmentOrDefinitionHost(ExpressionSyntax expression, CpgAnalysisContext context)
  {
    _ = context;

    foreach (var ancestor in expression.Ancestors()) {
      if (ancestor is AssignmentExpressionSyntax assignmentExpression &&
          (assignmentExpression.Right.Span.Contains(expression.Span) ||
           assignmentExpression.Left.Span.Contains(expression.Span))) {
        return assignmentExpression;
      }

      if (ancestor is EqualsValueClauseSyntax equalsValueClause &&
          equalsValueClause.Value.Span.Contains(expression.Span) &&
          equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator) {
        return variableDeclarator;
      }
    }

    return null;
  }

  public static DecisionUnit CreateDeleteDecision(string ruleId, SyntaxNode anchorNode, string reason, SyntaxNode? sourceNode = null, string? conflictKey = null)
  {
    var anchorFragment = CreateFragment(anchorNode, "anchor", DecisionActionKind.Delete);
    var fragments = new List<MinimalRoslynCpg.Model.RoslynCpgNode> { anchorFragment };
    var relations = new List<MinimalRoslynCpg.Model.RoslynCpgEdge>();
    var bindings = new List<(MinimalRoslynCpg.Model.RoslynCpgNode Fragment, SyntaxNode Node)>
    {
      (anchorFragment, anchorNode)
    };

    if (sourceNode is not null && !ReferenceEquals(sourceNode, anchorNode)) {
      var sourceFragment = CreateFragment(sourceNode, "source");
      fragments.Add(sourceFragment);
      relations.Add(DecisionCpgFactory.CreateRelation("derived-from", sourceFragment, anchorFragment));
      bindings.Add((sourceFragment, sourceNode));
    }

    var unitNode = DecisionCpgFactory.CreateUnit(
      ruleId,
      DecisionActionKind.Delete,
      anchorFragment,
      reason: reason,
      conflictKey: conflictKey ?? DecisionCpgFactory.BuildNodeKey(anchorNode));
    relations.Insert(0, DecisionCpgFactory.CreateContainment(unitNode, anchorFragment));
    if (fragments.Count > 1) {
      relations.Insert(1, DecisionCpgFactory.CreateContainment(unitNode, fragments[1]));
    }

    return new DecisionUnit(
      ruleId,
      DecisionActionKind.Delete,
      unitNode,
      fragments,
      relations,
      DecisionCpgFactory.CreateSyntaxBindings(bindings.ToArray()),
      conflictKey: conflictKey ?? DecisionCpgFactory.BuildNodeKey(anchorNode),
      reason: reason);
  }

  public static MarkRecord CreateMark(string ruleId, SyntaxNode syntaxNode, string reason)
  {
    return new MarkRecord(ruleId, syntaxNode, null, null, reason);
  }

  private static bool TryAnalyzeExpression(ExpressionSyntax expression, CpgAnalysisContext context, out IReadOnlyList<SyntaxNode> affectedNodes)
  {
    affectedNodes = Array.Empty<SyntaxNode>();

    switch (expression) {
      case BinaryExpressionSyntax binaryExpression:
        affectedNodes = new BinaryExpressionAnalyzer()
          .Analyze(binaryExpression, binaryExpression.Left, context)
          .AffectedSyntaxTree;
        return true;
      case AssignmentExpressionSyntax assignmentExpression:
        affectedNodes = new AssignmentExpressionAnalyzer()
          .Analyze(assignmentExpression, context)
          .AffectedSyntaxTree;
        return true;
      case PrefixUnaryExpressionSyntax prefixUnaryExpression:
        affectedNodes = new UnaryExpressionAnalyzer()
          .Analyze(prefixUnaryExpression, context)
          .AffectedSyntaxTree;
        return true;
      case InvocationExpressionSyntax:
      case ObjectCreationExpressionSyntax:
      case ImplicitObjectCreationExpressionSyntax:
      case MemberAccessExpressionSyntax:
      case MemberBindingExpressionSyntax:
      case ElementAccessExpressionSyntax:
      case ConditionalAccessExpressionSyntax:
        affectedNodes = new CallAndAccessStructureAnalyzer()
          .Analyze(expression, context)
          .AffectedSyntaxTree;
        return true;
      case IdentifierNameSyntax:
      case LiteralExpressionSyntax:
        affectedNodes = new[] { expression };
        return true;
      default:
        return false;
    }
  }

  private static bool TryResolveStructuralHost(SyntaxNode ancestor, ExpressionSyntax expression, CpgAnalysisContext context, out SyntaxNode? host)
  {
    host = null;

    switch (ancestor) {
      case IfStatementSyntax ifStatement when ifStatement.Condition == expression:
        host = ifStatement;
        return true;
      case ReturnStatementSyntax returnStatement when returnStatement.Expression == expression:
        host = returnStatement;
        return true;
      case ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax:
        var loopAnalysis = new LoopStructureAnalyzer().Analyze((StatementSyntax)ancestor, context);
        if (loopAnalysis.AffectedSyntaxTree.Any(node => ReferenceEquals(node, expression))) {
          host = ancestor;
          return true;
        }

        return false;
      case SwitchStatementSyntax switchStatement when ReferenceEquals(switchStatement.Expression, expression):
        var switchAnalysis = new SwitchStructureAnalyzer().Analyze(switchStatement, context);
        if (switchAnalysis.AffectedSyntaxTree.Any(node => ReferenceEquals(node, expression))) {
          host = switchStatement;
          return true;
        }

        return false;
      default:
        return false;
    }
  }

  private static MinimalRoslynCpg.Model.RoslynCpgNode CreateFragment(SyntaxNode node, string role, DecisionActionKind? localAction = null)
  {
    return DecisionCpgFactory.CreateFragment(
      $"frag:{DecisionCpgFactory.BuildNodeKey(node)}",
      node,
      role,
      localAction);
  }
}
