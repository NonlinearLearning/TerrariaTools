using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using NLCPG.Analysis;
using NLCPG.Analysis.FlowSummaries;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Core.Analysis;
using NLISSN.Core.Analysis.ExpressionPropagation;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using System.Threading;

namespace NLISSN.Core.Pipeline;

/// <summary>
/// Owns immutable analysis inputs and shared caches for one source analysis.
/// Rule implementations only receive a stage-specific adapter created by this session.
/// </summary>
internal sealed class AnalysisSession
{
    private readonly CpgAnalysisContext _analysisContext;
    private readonly AnalysisRequestSettings _settings;
    private readonly MarkAnalysisSnapshot _markAnalysisSnapshot;
    private readonly Lazy<LocalSymbolReferenceIndex> _localSymbolReferenceIndex;
    private readonly ConcurrentDictionary<ExpressionSyntax, Lazy<ExpressionTopologyPath>> _expressionTopologies = new();
    private long _expressionTopologyAnalyzeCount;
    private int _structureViewQueryCount;

    internal AnalysisSession(
      CpgAnalysisContext analysisContext,
      AnalysisRequestSettings settings,
      AnalysisRuntime? runtime = null,
      MarkAnalysisSnapshot? markAnalysisSnapshot = null,
      AnalysisEvidenceCollector? evidence = null)
    {
        _analysisContext = analysisContext;
        _settings = settings;
        Runtime = runtime ?? AnalysisRuntime.CreateDefault();
        Evidence = evidence ?? new AnalysisEvidenceCollector();
        _markAnalysisSnapshot = markAnalysisSnapshot ?? new MarkAnalysisSnapshot(analysisContext, Evidence);
        _localSymbolReferenceIndex = new Lazy<LocalSymbolReferenceIndex>(
          () => new LocalSymbolReferenceIndex(
            _analysisContext.SemanticModel,
            _analysisContext.CompilationRoot));
    }

    internal AnalysisRuntime Runtime { get; }

    internal AnalysisEvidenceCollector Evidence { get; }

    internal int StructureViewQueryCount => Volatile.Read(ref _structureViewQueryCount);

    internal IMarkRuleContext CreateMarkContext() => new MarkRuleContext(this);

    internal IPropagationRuleContext CreatePropagationContext(IReadOnlyList<MarkRecord> inputMarks) =>
      new PropagationRuleContext(this, inputMarks);

    internal ILiftRuleContext CreateLiftContext(
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks) =>
      new LiftRuleContext(this, seedMarks, propagatedMarks);

    internal IProposeRuleContext CreateProposeContext() => new ProposeRuleContext(this);

    internal IReadOnlyList<string> GetNormalizedTargetNames() =>
      _settings.TargetNames.Count > 0
        ? _markAnalysisSnapshot.GetNormalizedTargetNames(string.Join(',', _settings.TargetNames))
        : Array.Empty<string>();

    internal NameDescriptor GetTargetNameDescriptor() =>
      _settings.TargetNames.Count > 0
        ? _markAnalysisSnapshot.GetTargetNameDescriptor(string.Join(',', _settings.TargetNames))
        : _markAnalysisSnapshot.GetTargetNameDescriptor(null);

    internal bool GetCachedTargetMatch(
      SyntaxNode syntaxNode,
      NameDescriptor targetNames,
      Func<bool> evaluate) =>
      _markAnalysisSnapshot.GetTargetMatch(syntaxNode, targetNames, evaluate);

    internal SemanticModel SemanticModel => _analysisContext.SemanticModel;

    internal SymbolUsageProfile SymbolUsageProfile => Runtime.GetOrCreateEpochCompilationCache(
      _analysisContext.SemanticModel.Compilation,
      static compilation => new SymbolUsageProfile(compilation));

    internal LocalSymbolReferenceIndex LocalSymbolReferences => _localSymbolReferenceIndex.Value;

    internal SyntaxNode Root => _analysisContext.CompilationRoot;

    internal AnalysisRequestSettings Settings => _settings;

    internal CpgStructureViewQueryResult QueryStructureView(
      IReadOnlyCollection<SyntaxNode> fragments,
      CpgRelationProfile profile,
      CpgQueryDirection direction,
      NLCPGTraversalBudget budget)
    {
        Interlocked.Increment(ref _structureViewQueryCount);
        return new NLCPGStructureViewBuilder().Query(
          fragments,
          _analysisContext,
          profile,
          direction,
          budget,
          Runtime.CacheScopeKey);
    }

    internal NLCPGStructureView BuildStructureView(
      IReadOnlyCollection<SyntaxNode> fragments,
      CpgRelationProfile profile,
      CpgQueryDirection direction,
      NLCPGTraversalBudget budget) =>
      new NLCPGStructureViewBuilder().Build(
        fragments,
        _analysisContext,
        profile,
        direction,
        budget,
        Runtime.CacheScopeKey);

    internal IEnumerable<ExpressionSyntax> EnumerateAllowedExpressions(
      SyntaxNode root,
      IReadOnlyCollection<Microsoft.CodeAnalysis.CSharp.SyntaxKind> allowedKinds) =>
      RuleSyntaxAnalysisHelpers.EnumerateAllowedExpressions(
        root,
        allowedKinds,
        _analysisContext,
        _markAnalysisSnapshot.GetAtomicCandidates(root, allowedKinds));

    internal IEnumerable<MethodDeclarationSyntax> EnumerateMethodDeclarations(SyntaxNode root) =>
      RuleSyntaxAnalysisHelpers.EnumerateMethodDeclarations(root, _analysisContext);

    internal MarkCodeRegion AnalyzeMarkRegion(SyntaxNode anchorNode) =>
      _markAnalysisSnapshot.GetMarkRegion(anchorNode);

    internal IOperation? GetCachedOperation(SyntaxNode syntaxNode) =>
      _markAnalysisSnapshot.GetOperation(syntaxNode);

    internal ExpressionTopologyPath ResolveExpressionTopology(ExpressionSyntax expression)
    {
        var created = new Lazy<ExpressionTopologyPath>(
          () =>
          {
              Interlocked.Increment(ref _expressionTopologyAnalyzeCount);
              return new ExpressionPropagationTopology(
                _markAnalysisSnapshot.GetOperation,
                requireSemanticOverlay: true).Analyze(expression);
          },
          LazyThreadSafetyMode.ExecutionAndPublication);
        return _expressionTopologies.GetOrAdd(expression, created).Value;
    }

    internal ExpressionTopologyMetrics GetExpressionTopologyMetrics()
    {
        var operationMetrics = _markAnalysisSnapshot.GetOperationCacheMetrics();
        return new ExpressionTopologyMetrics(
          Interlocked.Read(ref _expressionTopologyAnalyzeCount),
          _expressionTopologies.Count,
          operationMetrics.Hits,
          operationMetrics.Misses);
    }

    internal NLCPGSliceResult QuerySliceBackward(NodeId sinkNodeId, NLCPGSliceQueryOptions options) =>
      _markAnalysisSnapshot.QuerySliceBackward(sinkNodeId, options);

    internal CpgRelationQueryResult QueryRelation(CpgRelationQuery query) =>
      _markAnalysisSnapshot.QueryRelation(query);

    internal ResolvedCallFlow ResolveCallFlow(
      IInvocationOperation invocation,
      FlowSummaryEndpoint source,
      FlowSummaryEndpoint target)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var result = _analysisContext.CallFlowResolver?.Resolve(invocation, source, target) ??
          new ResolvedCallFlow(
            ResolvedCallFlowStatus.Unknown,
            FlowSummaryResolution.Unknown,
            FlowSummaryMethodKey.From(invocation.TargetMethod),
            null,
            "No call flow resolver is configured.");
        Evidence.RecordFlowSummary(invocation.Syntax, result);
        return result;
    }

    internal bool CanAnalyzeLogicalCondition(ExpressionSyntax expression) =>
      new LogicalConditionMarkAnalyzer().CanAnalyze(expression, _analysisContext);

    internal LogicalConditionMarkAnalysis AnalyzeLogicalCondition(
      ExpressionSyntax seedExpression,
      string targetName) =>
      new LogicalConditionMarkAnalyzer().Analyze(
        seedExpression,
        targetName,
        _analysisContext,
        expression => _markAnalysisSnapshot.GetOperation(expression));

    internal IfStructureAnalysis AnalyzeIfStructure(IfStatementSyntax ifStatement) =>
      new IfStructureAnalyzer().Analyze(ifStatement, _analysisContext);

    internal bool TryFindContainingIf(ExpressionSyntax expression, out IfStructureAnalysis? analysis) =>
      new IfStructureAnalyzer().TryFindContainingIf(expression, _analysisContext, out analysis);

    internal SyntaxNode? FindLogicalHost(ExpressionSyntax expression) =>
      RuleSyntaxAnalysisHelpers.FindLogicalHost(expression, _analysisContext);

    internal LoopStructureAnalysis AnalyzeLoopStructure(StatementSyntax statement) =>
      new LoopStructureAnalyzer().Analyze(statement, _analysisContext);

    internal bool TryResolvePrimaryGraphNode(SyntaxNode syntaxNode, out NLCPGNode? graphNode) =>
      _markAnalysisSnapshot.TryResolvePrimaryGraphNode(syntaxNode, out graphNode);

    internal bool ContainsPrimaryGraphNodeInRegion(SyntaxNode syntaxNode, TextSpan regionSpan) =>
      TryResolvePrimaryGraphNode(syntaxNode, out var graphNode) &&
      graphNode is not null &&
      graphNode.SpanStart >= regionSpan.Start &&
      graphNode.SpanEnd <= regionSpan.End;

    internal IReadOnlyList<NLCPGNode> GetGraphNodesByKind(NLCPGNodeKind kind) =>
      _analysisContext.Graph.GetNodes(kind);

    internal IReadOnlyList<NLCPGEdge> GetGraphEdgesByKind(NodeId sourceNodeId, NLCPGEdgeKind kind) =>
      _analysisContext.Graph.GetOutgoingEdges(sourceNodeId, kind);

    internal NLCPGNode? FindGraphNodeById(NodeId nodeId) => _analysisContext.Graph.GetNode(nodeId);
}
