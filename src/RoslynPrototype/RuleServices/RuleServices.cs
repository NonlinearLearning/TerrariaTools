using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Analysis;

namespace Rules;

public interface IRuleOptions
{
  bool TryGetOption(string key, out string value);
}

public interface IRuleAnalysisServices
{
  IEnumerable<ExpressionSyntax> EnumerateAllowedExpressions(
    SyntaxNode root,
    IReadOnlyCollection<SyntaxKind> allowedKinds);

  IEnumerable<MethodDeclarationSyntax> EnumerateMethodDeclarations(SyntaxNode root);

  MarkCodeRegion AnalyzeMarkRegion(SyntaxNode anchorNode);

  bool CanAnalyzeLogicalCondition(ExpressionSyntax expression);

  LogicalConditionMarkAnalysis AnalyzeLogicalCondition(
    ExpressionSyntax seedExpression,
    string targetName);

  BinaryExpressionAnalysis AnalyzeBinaryExpression(
    BinaryExpressionSyntax root,
    ExpressionSyntax operand);

  IfStructureAnalysis AnalyzeIfStructure(IfStatementSyntax ifStatement);

  bool TryFindContainingIf(
    ExpressionSyntax expression,
    out IfStructureAnalysis? analysis);

  SyntaxNode? FindLogicalHost(ExpressionSyntax expression);

  LoopStructureAnalysis AnalyzeLoopStructure(StatementSyntax statement);
}

public interface IRuleGraphBindingServices
{
  bool TryResolvePrimaryGraphNode(SyntaxNode syntaxNode, out RoslynCpgNode? graphNode);

  bool ContainsPrimaryGraphNodeInRegion(SyntaxNode syntaxNode, TextSpan regionSpan);
}

public interface IRuleStructureViewServices
{
  RoslynCpgStructureView? StructureView { get; }

  RoslynCpgStructureView BuildStructureView(IReadOnlyCollection<SyntaxNode> fragments);

  RuleContext WithStructureView(RoslynCpgStructureView structureView);
}
