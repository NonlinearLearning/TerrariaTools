using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NLCPG.Model;
using NLISSN.Core.Analysis;

namespace NLISSN.Rules;

public interface IRuleOptions
{
  bool TryGetOption(string key, out string value);
}

public interface IRuleAnalysisServices
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

public interface IRuleGraphBindingServices
{
  bool TryResolvePrimaryGraphNode(SyntaxNode syntaxNode, out NLCPGNode? graphNode);

  bool ContainsPrimaryGraphNodeInRegion(SyntaxNode syntaxNode, TextSpan regionSpan);
}

public interface IRuleStructureViewServices
{
  NLCPGStructureView? StructureView { get; }

  NLCPGStructureView BuildStructureView(IReadOnlyCollection<SyntaxNode> fragments);

  RuleContext WithStructureView(NLCPGStructureView structureView);
}
