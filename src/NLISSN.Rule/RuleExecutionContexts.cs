using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NLCPG.Analysis;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Analysis;

namespace NLISSN.Core.Pipeline;

public interface IMarkRuleContext
{
  SemanticModel SemanticModel { get; }
  AnalysisRuntime Runtime { get; }
  IReadOnlyList<string> GetNormalizedTargetNames();
  NameDescriptor GetTargetNameDescriptor();
  bool GetCachedTargetMatch(SyntaxNode syntaxNode, NameDescriptor targetNames, Func<bool> evaluate);
  bool TryGetOption(string key, out string value);
  IEnumerable<ExpressionSyntax> EnumerateAllowedExpressions(
    SyntaxNode root,
    IReadOnlyCollection<Microsoft.CodeAnalysis.CSharp.SyntaxKind> allowedKinds);
  IEnumerable<MethodDeclarationSyntax> EnumerateMethodDeclarations(SyntaxNode root);
  MarkCodeRegion AnalyzeMarkRegion(SyntaxNode anchorNode);
  IOperation? GetCachedOperation(SyntaxNode syntaxNode);
  bool CanAnalyzeLogicalCondition(ExpressionSyntax expression);
  LogicalConditionMarkAnalysis AnalyzeLogicalCondition(ExpressionSyntax seedExpression, string targetName);
  bool TryResolvePrimaryGraphNode(SyntaxNode syntaxNode, out NLCPGNode? graphNode);
  bool ContainsPrimaryGraphNodeInRegion(SyntaxNode syntaxNode, TextSpan regionSpan);
  IReadOnlyList<NLCPGNode> GetGraphNodesByKind(NLCPGNodeKind kind);
  IReadOnlyList<NLCPGEdge> GetGraphEdgesByKind(NodeId sourceNodeId, NLCPGEdgeKind kind);
  NLCPGNode? FindGraphNodeById(NodeId nodeId);
}

public interface ISemanticRuleContext
{
  SemanticModel SemanticModel { get; }
  AnalysisRuntime Runtime { get; }
}

public interface IPropagationRuleContext : ISemanticRuleContext
{
  SyntaxNode Root { get; }
  NLCPGStructureView? StructureView { get; }
}

public interface ILiftRuleContext
{
  SyntaxNode Root { get; }
  NLCPGStructureView? StructureView { get; }
  IfStructureAnalysis AnalyzeIfStructure(IfStatementSyntax ifStatement);
  bool TryFindContainingIf(ExpressionSyntax expression, out IfStructureAnalysis? analysis);
  SyntaxNode? FindLogicalHost(ExpressionSyntax expression);
  LoopStructureAnalysis AnalyzeLoopStructure(StatementSyntax statement);
}

public interface IProposeRuleContext : ISemanticRuleContext
{
}
