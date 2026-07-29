using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis;

public interface IRuleAnalysis
{
  IEnumerable<ExpressionSyntax> EnumerateAllowedExpressions(SyntaxNode root, IReadOnlyCollection<SyntaxKind> allowedKinds);

  IEnumerable<MethodDeclarationSyntax> EnumerateMethodDeclarations(SyntaxNode root);

  MarkCodeRegion AnalyzeMarkRegion(SyntaxNode anchorNode);

  bool CanAnalyzeLogicalCondition(ExpressionSyntax expression);

  LogicalConditionMarkAnalysis AnalyzeLogicalCondition(ExpressionSyntax seedExpression, string targetName);

  BinaryExpressionAnalysis AnalyzeBinaryExpression(BinaryExpressionSyntax root, ExpressionSyntax operand);

  IfStructureAnalysis AnalyzeIfStructure(IfStatementSyntax ifStatement);

  bool TryFindContainingIf(ExpressionSyntax expression, out IfStructureAnalysis? analysis);

  SyntaxNode? FindLogicalHost(ExpressionSyntax expression);

  LoopStructureAnalysis AnalyzeLoopStructure(StatementSyntax statement);
}
