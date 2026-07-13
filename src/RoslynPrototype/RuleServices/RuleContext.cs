using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MinimalRoslynCpg.Contracts;
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

    public RuleContext(
      CpgAnalysisContext analysisContext,
      IReadOnlyDictionary<string, string> options,
      RoslynCpgStructureView? structureView = null,
      DeletionAnalysisRuntime? runtime = null)
    {
        _analysisContext = analysisContext;
        _options = options;
        _runtime = runtime ?? DeletionAnalysisRuntime.CreateDefault();
        StructureView = structureView;
    }

    public IRuleOptions Options => this;

    public IRuleAnalysisServices Analysis => this;

    public IRuleGraphBindingServices GraphBinding => this;

    public IRuleStructureViewServices StructureViews => this;

    public RoslynCpgStructureView? StructureView { get; }

    public DeletionAnalysisRuntime Runtime => _runtime;

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
        return new RuleContext(_analysisContext, _options, structureView, _runtime);
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
        return RuleSyntaxAnalysisHelpers.EnumerateAllowedExpressions(root, allowedKinds, _analysisContext);
    }

    public IEnumerable<MethodDeclarationSyntax> EnumerateMethodDeclarations(SyntaxNode root)
    {
        return RuleSyntaxAnalysisHelpers.EnumerateMethodDeclarations(root, _analysisContext);
    }

    public MarkCodeRegion AnalyzeMarkRegion(SyntaxNode anchorNode)
    {
        return new MarkRegionAnalyzer().Analyze(anchorNode, _analysisContext);
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
        graphNode = ResolvePrimaryGraphNode(syntaxNode);
        return graphNode is not null;
    }

    public bool ContainsPrimaryGraphNodeInRegion(SyntaxNode syntaxNode, TextSpan regionSpan)
    {
        var graphNode = ResolvePrimaryGraphNode(syntaxNode);
        return graphNode is not null &&
          graphNode.SpanStart >= regionSpan.Start &&
          graphNode.SpanEnd <= regionSpan.End;
    }

    public IReadOnlyList<RoslynCpgNode> GetGraphNodesByKind(RoslynCpgNodeKind kind)
    {
        return _analysisContext.Graph.NodesByKind(kind).ToList();
    }

    public IReadOnlyList<RoslynCpgEdge> GetGraphEdgesByKind(string sourceId, RoslynCpgEdgeKind kind)
    {
        return _analysisContext.Graph.Edges
          .Where(edge => edge.SourceId == sourceId && edge.Kind == kind)
          .ToList();
    }

    public RoslynCpgNode? FindGraphNodeById(string nodeId)
    {
        return _analysisContext.Graph.Nodes.FirstOrDefault(node => node.Id == nodeId);
    }

    private RoslynCpgNode? ResolvePrimaryGraphNode(SyntaxNode syntaxNode)
    {
        var filePath = syntaxNode.SyntaxTree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return _analysisContext.Graph.Nodes
          .Where(node =>
            !node.IsImplicit &&
            string.Equals(node.FilePath, filePath, StringComparison.Ordinal) &&
            node.SpanStart == syntaxNode.SpanStart &&
            node.SpanEnd == syntaxNode.Span.End)
          .OrderBy(GetBindingPriority)
          .FirstOrDefault();
    }

    private static int GetBindingPriority(RoslynCpgNode node)
    {
        return node.Kind switch
        {
            RoslynCpgNodeKind.Method => 0,
            RoslynCpgNodeKind.MethodParameter => 1,
            RoslynCpgNodeKind.CallSite => 2,
            RoslynCpgNodeKind.MemberAccess => 3,
            RoslynCpgNodeKind.Reference => 4,
            RoslynCpgNodeKind.Operation => 5,
            RoslynCpgNodeKind.OpInvocation => 6,
            RoslynCpgNodeKind.OpBinary => 7,
            RoslynCpgNodeKind.OpAssignment => 8,
            RoslynCpgNodeKind.OpLocalReference => 9,
            RoslynCpgNodeKind.OpParameterReference => 10,
            RoslynCpgNodeKind.OpFieldReference => 11,
            RoslynCpgNodeKind.OpPropertyReference => 12,
            RoslynCpgNodeKind.SyntaxNode => 13,
            _ => 14
        };
    }
}
