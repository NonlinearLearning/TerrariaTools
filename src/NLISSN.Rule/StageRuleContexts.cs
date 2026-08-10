using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Analysis;
using NLCPG.Analysis.FlowSummaries;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Analysis;
using NLISSN.Core.Analysis.ExpressionPropagation;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;

namespace NLISSN.Core.Pipeline;

internal sealed class MarkRuleContext : IMarkRuleContext
{
    private readonly AnalysisSession _session;

    internal MarkRuleContext(AnalysisSession session) => _session = session;

    public SemanticModel SemanticModel => _session.SemanticModel;
    public AnalysisRuntime Runtime => _session.Runtime;
    public IReadOnlyList<string> GetNormalizedTargetNames() => _session.GetNormalizedTargetNames();
    public NameDescriptor GetTargetNameDescriptor() => _session.GetTargetNameDescriptor();
    public bool GetCachedTargetMatch(SyntaxNode syntaxNode, NameDescriptor targetNames, Func<bool> evaluate) =>
      _session.GetCachedTargetMatch(syntaxNode, targetNames, evaluate);
    public IReadOnlyList<string> DeleteClassNames => _session.Settings.DeleteClassNames;

    public bool DeleteUnreferencedMethods => _session.Settings.DeleteUnreferencedMethods;

    public bool ClearUnusedInterfaceImplementations => _session.Settings.ClearUnusedInterfaceImplementations;

    public bool PrivatizeInternalOnlyPublicMethods => _session.Settings.PrivatizeInternalOnlyPublicMethods;
    public IEnumerable<ExpressionSyntax> EnumerateAllowedExpressions(
      SyntaxNode root,
      IReadOnlyCollection<Microsoft.CodeAnalysis.CSharp.SyntaxKind> allowedKinds) =>
      _session.EnumerateAllowedExpressions(root, allowedKinds);
    public IEnumerable<MethodDeclarationSyntax> EnumerateMethodDeclarations(SyntaxNode root) =>
      _session.EnumerateMethodDeclarations(root);
    public MarkCodeRegion AnalyzeMarkRegion(SyntaxNode anchorNode) => _session.AnalyzeMarkRegion(anchorNode);
    public IOperation? GetCachedOperation(SyntaxNode syntaxNode) => _session.GetCachedOperation(syntaxNode);
    public bool CanAnalyzeLogicalCondition(ExpressionSyntax expression) => _session.CanAnalyzeLogicalCondition(expression);
    public LogicalConditionMarkAnalysis AnalyzeLogicalCondition(ExpressionSyntax seedExpression, string targetName) =>
      _session.AnalyzeLogicalCondition(seedExpression, targetName);
    public bool TryResolvePrimaryGraphNode(SyntaxNode syntaxNode, out NLCPGNode? graphNode) =>
      _session.TryResolvePrimaryGraphNode(syntaxNode, out graphNode);
    public bool ContainsPrimaryGraphNodeInRegion(SyntaxNode syntaxNode, Microsoft.CodeAnalysis.Text.TextSpan regionSpan) =>
      _session.ContainsPrimaryGraphNodeInRegion(syntaxNode, regionSpan);
    public IReadOnlyList<NLCPGNode> GetGraphNodesByKind(NLCPGNodeKind kind) => _session.GetGraphNodesByKind(kind);
    public IReadOnlyList<NLCPGEdge> GetGraphEdgesByKind(NodeId sourceNodeId, NLCPGEdgeKind kind) =>
      _session.GetGraphEdgesByKind(sourceNodeId, kind);
    public NLCPGNode? FindGraphNodeById(NodeId nodeId) => _session.FindGraphNodeById(nodeId);
}

internal sealed class PropagationRuleContext : IPropagationRuleContext
{
    private readonly AnalysisSession _session;
    private readonly Lazy<CpgStructureViewQueryResult> _structureViewQuery;

    internal PropagationRuleContext(AnalysisSession session, IReadOnlyList<MarkRecord> inputMarks)
    {
        _session = session;
        _structureViewQuery = CreateStructureViewQuery(session, inputMarks.Select(mark => mark.SyntaxNode));
    }

    public SemanticModel SemanticModel => _session.SemanticModel;
    public AnalysisRuntime Runtime => _session.Runtime;
    public SyntaxNode Root => _session.Root;
    public CpgStructureViewQueryResult StructureViewQuery => _structureViewQuery.Value;
    public ResolvedCallFlow ResolveCallFlow(
      IInvocationOperation invocation,
      FlowSummaryEndpoint source,
      FlowSummaryEndpoint target) => _session.ResolveCallFlow(invocation, source, target);
    public ExpressionTopologyPath ResolveExpressionTopology(ExpressionSyntax expression) =>
      _session.ResolveExpressionTopology(expression);

    private static Lazy<CpgStructureViewQueryResult> CreateStructureViewQuery(
      AnalysisSession session,
      IEnumerable<SyntaxNode> fragments)
    {
        var fragmentList = fragments.Distinct().ToArray();
        return new Lazy<CpgStructureViewQueryResult>(
          () => fragmentList.Length == 0
            ? new CpgStructureViewQueryResult(null, CpgQueryStatus.Disconnected, "The rule node has no input marks.")
            : session.QueryStructureView(
              fragmentList,
              CpgRelationProfile.StructuralContainment,
              CpgQueryDirection.Bidirectional,
              new NLCPGTraversalBudget(16, 1, 1, 4096, 8192)),
          LazyThreadSafetyMode.ExecutionAndPublication);
    }
}

internal sealed class LiftRuleContext : ILiftRuleContext
{
    private readonly AnalysisSession _session;
    private readonly Lazy<CpgStructureViewQueryResult> _structureViewQuery;

    internal LiftRuleContext(
      AnalysisSession session,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        _session = session;
        var fragments = seedMarks.Select(mark => mark.SyntaxNode)
          .Concat(propagatedMarks.Select(mark => mark.Mark.SyntaxNode))
          .Distinct()
          .ToArray();
        _structureViewQuery = new Lazy<CpgStructureViewQueryResult>(
          () => fragments.Length == 0
            ? new CpgStructureViewQueryResult(null, CpgQueryStatus.Disconnected, "The rule node has no input marks.")
            : session.QueryStructureView(
              fragments,
              CpgRelationProfile.StructuralContainment,
              CpgQueryDirection.Bidirectional,
              new NLCPGTraversalBudget(16, 1, 1, 4096, 8192)),
          LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public SyntaxNode Root => _session.Root;
    public CpgStructureViewQueryResult StructureViewQuery => _structureViewQuery.Value;
    public IfStructureAnalysis AnalyzeIfStructure(IfStatementSyntax ifStatement) => _session.AnalyzeIfStructure(ifStatement);
    public bool TryFindContainingIf(ExpressionSyntax expression, out IfStructureAnalysis? analysis) =>
      _session.TryFindContainingIf(expression, out analysis);
    public SyntaxNode? FindLogicalHost(ExpressionSyntax expression) => _session.FindLogicalHost(expression);
    public LoopStructureAnalysis AnalyzeLoopStructure(StatementSyntax statement) => _session.AnalyzeLoopStructure(statement);
    public ExpressionTopologyPath ResolveExpressionTopology(ExpressionSyntax expression) =>
      _session.ResolveExpressionTopology(expression);
}

internal sealed class ProposeRuleContext : IProposeRuleContext
{
    private readonly AnalysisSession _session;

    internal ProposeRuleContext(AnalysisSession session) => _session = session;

    public SemanticModel SemanticModel => _session.SemanticModel;
    public AnalysisRuntime Runtime => _session.Runtime;
    public ExpressionTopologyPath ResolveExpressionTopology(ExpressionSyntax expression) =>
      _session.ResolveExpressionTopology(expression);
}
