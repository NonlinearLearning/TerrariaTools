using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Analysis;
using MinimalRoslynCpg.Model;
using RoslynPrototype.Analysis;

namespace Rules;

/// <summary>
/// 规则执行时共享的最小上下文。
/// </summary>
public sealed class RuleContext :
  IRuleOptions,
  IRuleAnalysisServices,
  IRuleGraphBindingServices,
  IRuleStructureViewServices
{
    private readonly CpgAnalysisContext _analysisContext;
    private readonly IReadOnlyDictionary<string, string> _options;
    private readonly DeletionAnalysisRuntime _runtime;
    private readonly MarkAnalysisSnapshot _markAnalysisSnapshot;

    public RuleContext(
      CpgAnalysisContext analysisContext,
      IReadOnlyDictionary<string, string> options,
      RoslynCpgStructureView? structureView = null,
      DeletionAnalysisRuntime? runtime = null,
      MarkAnalysisSnapshot? markAnalysisSnapshot = null)
    {
        _analysisContext = analysisContext;
        _options = options;
        _runtime = runtime ?? DeletionAnalysisRuntime.CreateDefault();
        _markAnalysisSnapshot = markAnalysisSnapshot ?? new MarkAnalysisSnapshot(analysisContext);
        StructureView = structureView;
    }

    public IRuleOptions Options => this;

    public IRuleAnalysisServices Analysis => this;

    public IRuleGraphBindingServices GraphBinding => this;

    public IRuleStructureViewServices StructureViews => this;

    public RoslynCpgStructureView? StructureView { get; }

    public DeletionAnalysisRuntime Runtime => _runtime;

    public MarkAnalysisTelemetry MarkAnalysisTelemetry => _markAnalysisSnapshot.Telemetry;

    public RoslynCpgStructureViewCacheTelemetry StructureViewCacheTelemetry =>
      RoslynCpgStructureViewBuilder.GetCacheTelemetry(_analysisContext);

    public MarkAnalysisSnapshot.MarkRuleTelemetryScope BeginMarkRuleTelemetry(
      int ruleOrder,
      string ruleId,
      string? groupKey)
    {
        return _markAnalysisSnapshot.BeginRuleTelemetry(ruleOrder, ruleId, groupKey);
    }

    public IReadOnlyList<string> GetNormalizedTargetNames()
    {
        return TryGetOption("target-name", out var targetName)
          ? _markAnalysisSnapshot.GetNormalizedTargetNames(targetName)
          : Array.Empty<string>();
    }

    public TargetNameDescriptor GetTargetNameDescriptor()
    {
        return TryGetOption("target-name", out var targetName)
          ? _markAnalysisSnapshot.GetTargetNameDescriptor(targetName)
          : _markAnalysisSnapshot.GetTargetNameDescriptor(null);
    }

    public bool GetCachedTargetMatch(
      SyntaxNode syntaxNode,
      TargetNameDescriptor targetNames,
      Func<bool> evaluate)
    {
        return _markAnalysisSnapshot.GetTargetMatch(syntaxNode, targetNames, evaluate);
    }

    /// <summary>
    /// 当前源码的 Roslyn 语义模型。
    /// </summary>
    public SemanticModel SemanticModel => _analysisContext.SemanticModel;

    /// <summary>
    /// 当前分析源码的语法树根节点。
    /// </summary>
    public SyntaxNode Root => _analysisContext.CompilationRoot;

    public bool TryGetOption(string key, out string value)
    {
        return _options.TryGetValue(key, out value!);
    }

    public RuleContext WithStructureView(RoslynCpgStructureView structureView)
    {
        return new RuleContext(_analysisContext, _options, structureView, _runtime, _markAnalysisSnapshot);
    }

    public RoslynCpgStructureView BuildStructureView(IReadOnlyCollection<SyntaxNode> fragments)
    {
        return new RoslynCpgStructureViewBuilder().Build(
          fragments,
          _analysisContext,
          _runtime.CacheScopeKey);
    }

    public IEnumerable<ExpressionSyntax> EnumerateAllowedExpressions(
      SyntaxNode root,
      IReadOnlyCollection<Microsoft.CodeAnalysis.CSharp.SyntaxKind> allowedKinds)
    {
        return RuleSyntaxAnalysisHelpers.EnumerateAllowedExpressions(
          root,
          allowedKinds,
          _analysisContext,
          _markAnalysisSnapshot.GetAtomicCandidates(root, allowedKinds));
    }

    public IEnumerable<MethodDeclarationSyntax> EnumerateMethodDeclarations(SyntaxNode root)
    {
        return RuleSyntaxAnalysisHelpers.EnumerateMethodDeclarations(root, _analysisContext);
    }

    public MarkCodeRegion AnalyzeMarkRegion(SyntaxNode anchorNode)
    {
        return _markAnalysisSnapshot.GetMarkRegion(anchorNode);
    }

    public IOperation? GetCachedOperation(SyntaxNode syntaxNode)
    {
        return _markAnalysisSnapshot.GetOperation(syntaxNode);
    }

    public RoslynCpgSliceResult QuerySliceBackward(
      NodeId sinkNodeId,
      RoslynCpgSliceQueryOptions options)
    {
        return _markAnalysisSnapshot.QuerySliceBackward(sinkNodeId, options);
    }

    public bool CanAnalyzeLogicalCondition(ExpressionSyntax expression)
    {
        return new LogicalConditionMarkAnalyzer().CanAnalyze(expression, _analysisContext);
    }

    public LogicalConditionMarkAnalysis AnalyzeLogicalCondition(
      ExpressionSyntax seedExpression,
      string targetName)
    {
        return new LogicalConditionMarkAnalyzer().Analyze(seedExpression, targetName, _analysisContext);
    }

    public BinaryExpressionAnalysis AnalyzeBinaryExpression(
      BinaryExpressionSyntax root,
      ExpressionSyntax operand)
    {
        return new BinaryExpressionAnalyzer().Analyze(root, operand, _analysisContext);
    }

    public IfStructureAnalysis AnalyzeIfStructure(IfStatementSyntax ifStatement)
    {
        return new IfStructureAnalyzer().Analyze(ifStatement, _analysisContext);
    }

    public bool TryFindContainingIf(
      ExpressionSyntax expression,
      out IfStructureAnalysis? analysis)
    {
        return new IfStructureAnalyzer().TryFindContainingIf(expression, _analysisContext, out analysis);
    }

    public SyntaxNode? FindLogicalHost(ExpressionSyntax expression)
    {
        return RuleSyntaxAnalysisHelpers.FindLogicalHost(expression, _analysisContext);
    }

    public LoopStructureAnalysis AnalyzeLoopStructure(StatementSyntax statement)
    {
        return new LoopStructureAnalyzer().Analyze(statement, _analysisContext);
    }

    public bool TryResolvePrimaryGraphNode(SyntaxNode syntaxNode, out RoslynCpgNode? graphNode)
    {
        return _markAnalysisSnapshot.TryResolvePrimaryGraphNode(syntaxNode, out graphNode);
    }

    public bool ContainsPrimaryGraphNodeInRegion(SyntaxNode syntaxNode, TextSpan regionSpan)
    {
        return TryResolvePrimaryGraphNode(syntaxNode, out var graphNode) &&
          graphNode is not null &&
          graphNode.SpanStart >= regionSpan.Start &&
          graphNode.SpanEnd <= regionSpan.End;
    }

    public IReadOnlyList<RoslynCpgNode> GetGraphNodesByKind(RoslynCpgNodeKind kind)
    {
        return _analysisContext.Graph.NodesByKind(kind).ToList();
    }

    public IReadOnlyList<RoslynCpgEdge> GetGraphEdgesByKind(NodeId sourceNodeId, RoslynCpgEdgeKind kind)
    {
        return _analysisContext.Graph.GetOutgoingEdges(sourceNodeId, kind);
    }

    public RoslynCpgNode? FindGraphNodeById(NodeId nodeId)
    {
        return _analysisContext.Graph.GetNode(nodeId);
    }

}
