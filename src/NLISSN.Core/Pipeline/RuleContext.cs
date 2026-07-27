using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NLCPG.Contracts;
using NLCPG.Analysis;
using NLCPG.Model;
using NLISSN.Core.Analysis;

namespace NLISSN.Rules;

/// 规则执行时共享的最小上下文。
public sealed class RuleContext :
  IRuleOptions,
  IRuleAnalysisServices,
  IRuleGraphBindingServices,
  IRuleStructureViewServices
{
    private readonly CpgAnalysisContext _analysisContext;
    private readonly IReadOnlyDictionary<string, string> _options;
    private readonly  AnalysisRuntime _runtime;
    private readonly MarkAnalysisSnapshot _markAnalysisSnapshot;

    // 绑定本次分析的源码上下文、运行时和可选结构视图，供规则阶段统一访问。
    public RuleContext(CpgAnalysisContext analysisContext, IReadOnlyDictionary<string, string> options, NLCPGStructureView? structureView = null,  AnalysisRuntime? runtime = null, MarkAnalysisSnapshot? markAnalysisSnapshot = null)
    {
        _analysisContext = analysisContext;
        _options = options;
        _runtime = runtime ??  AnalysisRuntime.CreateDefault();
        _markAnalysisSnapshot = markAnalysisSnapshot ?? new MarkAnalysisSnapshot(analysisContext);
        StructureView = structureView;
    }

    public IRuleOptions Options => this;

    public IRuleAnalysisServices Analysis => this;

    public IRuleGraphBindingServices GraphBinding => this;

    public IRuleStructureViewServices StructureViews => this;

    public NLCPGStructureView? StructureView { get; }

    public  AnalysisRuntime Runtime => _runtime;

    // 解析并返回标准化后的目标名列表，供规则按同一名称集合匹配。
    public IReadOnlyList<string> GetNormalizedTargetNames()
    {
        return TryGetOption("target-name", out var targetName)
          ? _markAnalysisSnapshot.GetNormalizedTargetNames(targetName)
          : Array.Empty<string>();
    }

    // 返回带缓存键的目标名描述对象，避免不同规则重复拆分相同的选项值。
    public TargetNameDescriptor GetTargetNameDescriptor()
    {
        return TryGetOption("target-name", out var targetName)
          ? _markAnalysisSnapshot.GetTargetNameDescriptor(targetName)
          : _markAnalysisSnapshot.GetTargetNameDescriptor(null);
    }

    // 为指定语法节点缓存一次目标匹配判断，避免多条规则重复求值同一条件。
    public bool GetCachedTargetMatch(SyntaxNode syntaxNode, TargetNameDescriptor targetNames, Func<bool> evaluate)
    {
        return _markAnalysisSnapshot.GetTargetMatch(syntaxNode, targetNames, evaluate);
    }

    /// 当前源码的 Roslyn 语义模型。
    public SemanticModel SemanticModel => _analysisContext.SemanticModel;

    /// 当前分析源码的语法树根节点。
    public SyntaxNode Root => _analysisContext.CompilationRoot;

    // 读取一项原始 CLI 选项值，并保持调用方常见的 Try 模式。
    public bool TryGetOption(string key, out string value)
    {
        return _options.TryGetValue(key, out value!);
    }

    // 在复用当前运行时和分析快照的前提下替换结构视图，供下游规则局部收敛结构事实。
    public RuleContext WithStructureView(NLCPGStructureView structureView)
    {
        return new RuleContext(_analysisContext, _options, structureView, _runtime, _markAnalysisSnapshot);
    }

    // 根据一组语法片段从主图中构建局部结构视图，并携带当前缓存作用域键。
    public NLCPGStructureView BuildStructureView(IReadOnlyCollection<SyntaxNode> fragments)
    {
        return new NLCPGStructureViewBuilder().Build(
          fragments,
          _analysisContext,
          _runtime.CacheScopeKey);
    }

    // 枚举当前根节点内既属于原子候选又满足允许种类的表达式。
    public IEnumerable<ExpressionSyntax> EnumerateAllowedExpressions(SyntaxNode root, IReadOnlyCollection<Microsoft.CodeAnalysis.CSharp.SyntaxKind> allowedKinds)
    {
        return RuleSyntaxAnalysisHelpers.EnumerateAllowedExpressions(
          root,
          allowedKinds,
          _analysisContext,
          _markAnalysisSnapshot.GetAtomicCandidates(root, allowedKinds));
    }

    // 枚举当前根节点下可作为定义宿主参与规则分析的方法声明。
    public IEnumerable<MethodDeclarationSyntax> EnumerateMethodDeclarations(SyntaxNode root)
    {
        return RuleSyntaxAnalysisHelpers.EnumerateMethodDeclarations(root, _analysisContext);
    }

    // 计算一个锚点在 mark 阶段允许直接分析的局部语法区域。
    public MarkCodeRegion AnalyzeMarkRegion(SyntaxNode anchorNode)
    {
        return _markAnalysisSnapshot.GetMarkRegion(anchorNode);
    }

    // 复用语义模型缓存返回某个语法节点对应的 Roslyn IOperation。
    public IOperation? GetCachedOperation(SyntaxNode syntaxNode)
    {
        return _markAnalysisSnapshot.GetOperation(syntaxNode);
    }

    // 对指定 sink 节点执行反向切片查询，并复用同参查询结果。
    public NLCPGSliceResult QuerySliceBackward(NodeId sinkNodeId, NLCPGSliceQueryOptions options)
    {
        return _markAnalysisSnapshot.QuerySliceBackward(sinkNodeId, options);
    }

    // 判断一个表达式是否位于当前支持的逻辑条件结构内。
    public bool CanAnalyzeLogicalCondition(ExpressionSyntax expression)
    {
        return new LogicalConditionMarkAnalyzer().CanAnalyze(expression, _analysisContext);
    }

    // 在逻辑条件内部解析目标命中、操作数组和优选标记宿主。
    public LogicalConditionMarkAnalysis AnalyzeLogicalCondition(ExpressionSyntax seedExpression, string targetName)
    {
        return new LogicalConditionMarkAnalyzer().Analyze(seedExpression, targetName, _analysisContext);
    }

    // 分析二元表达式链中的受影响语法节点集合。
    public BinaryExpressionAnalysis AnalyzeBinaryExpression(BinaryExpressionSyntax root, ExpressionSyntax operand)
    {
        return new BinaryExpressionAnalyzer().Analyze(root, operand, _analysisContext);
    }

    // 提取 if 或 else-if 片段的局部结构事实，供后续传播和提案使用。
    public IfStructureAnalysis AnalyzeIfStructure(IfStatementSyntax ifStatement)
    {
        return new IfStructureAnalyzer().Analyze(ifStatement, _analysisContext);
    }

    // 沿祖先向上寻找包住当前表达式的最小 if 结构分析结果。
    public bool TryFindContainingIf(ExpressionSyntax expression, out IfStructureAnalysis? analysis)
    {
        return new IfStructureAnalyzer().TryFindContainingIf(expression, _analysisContext, out analysis);
    }

    // 在逻辑表达式祖先链上寻找可作为冲突宿主的逻辑二元表达式。
    public SyntaxNode? FindLogicalHost(ExpressionSyntax expression)
    {
        return RuleSyntaxAnalysisHelpers.FindLogicalHost(expression, _analysisContext);
    }

    // 分析循环语句头与语句体的局部结构节点。
    public LoopStructureAnalysis AnalyzeLoopStructure(StatementSyntax statement)
    {
        return new LoopStructureAnalyzer().Analyze(statement, _analysisContext);
    }

    // 把一个语法节点绑定回主图中的首选图节点。
    public bool TryResolvePrimaryGraphNode(SyntaxNode syntaxNode, out NLCPGNode? graphNode)
    {
        return _markAnalysisSnapshot.TryResolvePrimaryGraphNode(syntaxNode, out graphNode);
    }

    // 判断一个语法节点的主图节点是否完全落在给定文本区域内。
    public bool ContainsPrimaryGraphNodeInRegion(SyntaxNode syntaxNode, TextSpan regionSpan)
    {
        return TryResolvePrimaryGraphNode(syntaxNode, out var graphNode) &&
          graphNode is not null &&
          graphNode.SpanStart >= regionSpan.Start &&
          graphNode.SpanEnd <= regionSpan.End;
    }

    // 返回主图里某一节点种类的全部节点列表，供规则做图级过滤。
    public IReadOnlyList<NLCPGNode> GetGraphNodesByKind(NLCPGNodeKind kind)
    {
        return _analysisContext.Graph.NodesByKind(kind).ToList();
    }

    // 返回某个图节点沿指定边类型发出的所有出边。
    public IReadOnlyList<NLCPGEdge> GetGraphEdgesByKind(NodeId sourceNodeId, NLCPGEdgeKind kind)
    {
        return _analysisContext.Graph.GetOutgoingEdges(sourceNodeId, kind);
    }

    // 按 NodeId 从主图中定位一个节点，供规则在图事实和语法绑定之间跳转。
    public NLCPGNode? FindGraphNodeById(NodeId nodeId)
    {
        return _analysisContext.Graph.GetNode(nodeId);
    }

}
