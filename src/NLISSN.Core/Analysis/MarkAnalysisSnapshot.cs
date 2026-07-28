using System.Collections.Concurrent;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Analysis;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLISSN.Core.Analysis;

/// 保存一次分析中 Mark 规则共享的、运行范围线程安全事实。
public sealed class MarkAnalysisSnapshot
{
    private readonly CpgAnalysisContext _analysisContext;
    private readonly IReadOnlyDictionary<GraphBindingKey, NLCPGNode> _graphBindings;
    private readonly ConcurrentDictionary<SyntaxNode, Lazy<AtomicCandidateFacts>> _atomicCandidates = new();
    private readonly ConcurrentDictionary<SyntaxNode, Lazy<IOperation?>> _operations = new();
    private readonly ConcurrentDictionary<SyntaxNode, Lazy<MarkRegionFacts>> _regions = new();
    private readonly ConcurrentDictionary<TargetMatchKey, Lazy<bool>> _targetMatches = new();
    private readonly ConcurrentDictionary<SliceQueryKey, Lazy<NLCPGSliceResult>> _sliceQueries = new();
    private readonly ConcurrentDictionary<string, Lazy<TargetNameDescriptor>> _targetNameDescriptors =
      new(StringComparer.Ordinal);

    // 为一次分析运行建立共享快照，并预先索引语法到图节点的稳定绑定。
    public MarkAnalysisSnapshot(CpgAnalysisContext analysisContext)
    {
        _analysisContext = analysisContext;
        _graphBindings = BuildGraphBindingIndex(analysisContext.Graph.Nodes);
    }

    // 返回目标名描述里的展示名称列表，供规则做顺序稳定的目标名遍历。
    public IReadOnlyList<string> GetNormalizedTargetNames(string? targetName)
    {
        return GetTargetNameDescriptor(targetName).DisplayNames;
    }

    // 解析并缓存目标名描述对象，统一显示名、查找集和缓存键。
    public TargetNameDescriptor GetTargetNameDescriptor(string? targetName)
    {
        var key = targetName ?? string.Empty;
        return _targetNameDescriptors.GetOrAdd(
          key,
          static value => new Lazy<TargetNameDescriptor>(
            () => TargetNameDescriptor.Create(value),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    // 为语法节点和目标名组合缓存匹配结果，避免重复执行外部判断逻辑。
    public bool GetTargetMatch(SyntaxNode syntaxNode, TargetNameDescriptor targetNames, Func<bool> evaluate)
    {
        var key = new TargetMatchKey(syntaxNode, targetNames.CacheKey);
        var created = new Lazy<bool>(evaluate, LazyThreadSafetyMode.ExecutionAndPublication);
        return _targetMatches.GetOrAdd(key, created).Value;
    }

    // 返回当前根节点下所有可直接参与 mark 的原子表达式候选。
    public IReadOnlyList<ExpressionSyntax> GetAtomicCandidates(SyntaxNode root)
    {
        return GetAtomicCandidateFacts(root).Candidates;
    }

    // 返回当前根节点下指定语法种类的原子表达式候选子集。
    public IReadOnlyList<ExpressionSyntax> GetAtomicCandidates(
      SyntaxNode root,
      IReadOnlyCollection<Microsoft.CodeAnalysis.CSharp.SyntaxKind> allowedKinds)
    {
        var facts = GetAtomicCandidateFacts(root);
        if (allowedKinds.Count == 1)
        {
            var kind = allowedKinds.First();
            return facts.CandidatesByKind.TryGetValue(kind, out var bucket)
              ? bucket
              : Array.Empty<ExpressionSyntax>();
        }

        return facts.Candidates
          .Where(expression => allowedKinds.Contains(expression.Kind()))
          .ToArray();
    }

    // 缓存并返回语法节点的 Roslyn IOperation，供规则多次复用语义结果。
    public IOperation? GetOperation(SyntaxNode syntaxNode)
    {
        var created = new Lazy<IOperation?>(
          () => _analysisContext.SemanticModel.GetOperation(syntaxNode),
          LazyThreadSafetyMode.ExecutionAndPublication);
        return _operations.GetOrAdd(syntaxNode, created).Value;
    }

    // 计算并缓存锚点所在的 mark 区域事实，再返回面向当前锚点的区域对象。
    public MarkCodeRegion GetMarkRegion(SyntaxNode anchorNode)
    {
        var regionNode = MarkRegionAnalyzer.ResolveRegionNode(anchorNode);
        var created = new Lazy<MarkRegionFacts>(
          () => MarkRegionFacts.FromRegionNode(regionNode),
          LazyThreadSafetyMode.ExecutionAndPublication);
        return _regions.GetOrAdd(regionNode, created).Value.Create(anchorNode);
    }

    // 按文件路径和 span 把语法节点绑定回主图中的首选节点。
    public bool TryResolvePrimaryGraphNode(SyntaxNode syntaxNode, out NLCPGNode? graphNode)
    {
        var filePath = syntaxNode.SyntaxTree.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            graphNode = null;
            return false;
        }

        var key = new GraphBindingKey(filePath, syntaxNode.SpanStart, syntaxNode.Span.End);
        return _graphBindings.TryGetValue(key, out graphNode);
    }

    // 执行并缓存一次反向切片查询，避免同一 sink 和查询预算重复跑图查询。
    public NLCPGSliceResult QuerySliceBackward(NodeId sinkNodeId, NLCPGSliceQueryOptions options)
    {
        var key = SliceQueryKey.Create(sinkNodeId, options);
        var created = new Lazy<NLCPGSliceResult>(
          () => new NLCPGSliceQuery(_analysisContext.Graph).QueryBackward(sinkNodeId, options),
          LazyThreadSafetyMode.ExecutionAndPublication);
        return _sliceQueries.GetOrAdd(key, created).Value;
    }

    private AtomicCandidateFacts GetAtomicCandidateFacts(SyntaxNode root)
    {
        var created = new Lazy<AtomicCandidateFacts>(
          () => CreateAtomicCandidateFacts(root),
          LazyThreadSafetyMode.ExecutionAndPublication);
        return _atomicCandidates.GetOrAdd(root, created).Value;
    }

    private static AtomicCandidateFacts CreateAtomicCandidateFacts(SyntaxNode root)
    {
        var candidates = new AtomicExpressionAnalyzer().Analyze(root);
        var buckets = candidates
          .GroupBy(expression => expression.Kind())
          .ToDictionary(group => group.Key, group => (IReadOnlyList<ExpressionSyntax>)group.ToArray());
        return new AtomicCandidateFacts(candidates, buckets);
    }

    private static IReadOnlyDictionary<GraphBindingKey, NLCPGNode> BuildGraphBindingIndex(
      IEnumerable<NLCPGNode> graphNodes)
    {
        var bindings = new Dictionary<GraphBindingKey, NLCPGNode>();
        foreach (var node in graphNodes)
        {
            if (node.IsImplicit ||
                string.IsNullOrWhiteSpace(node.FilePath) ||
                node.SpanStart is null ||
                node.SpanEnd is null)
            {
                continue;
            }

            var key = new GraphBindingKey(node.FilePath, node.SpanStart.Value, node.SpanEnd.Value);
            if (!bindings.TryGetValue(key, out var current) ||
                GetBindingPriority(node) < GetBindingPriority(current))
            {
                bindings[key] = node;
            }
        }

        return bindings;
    }

    private static int GetBindingPriority(NLCPGNode node)
    {
        return node.Kind switch
        {
            NLCPGNodeKind.Method => 0,
            NLCPGNodeKind.MethodParameter => 1,
            NLCPGNodeKind.CallSite => 2,
            NLCPGNodeKind.MemberAccess => 3,
            NLCPGNodeKind.Reference => 4,
            NLCPGNodeKind.Operation => 5,
            NLCPGNodeKind.OpInvocation => 6,
            NLCPGNodeKind.OpBinary => 7,
            NLCPGNodeKind.OpAssignment => 8,
            NLCPGNodeKind.OpLocalReference => 9,
            NLCPGNodeKind.OpParameterReference => 10,
            NLCPGNodeKind.OpFieldReference => 11,
            NLCPGNodeKind.OpPropertyReference => 12,
            NLCPGNodeKind.SyntaxNode => 13,
            _ => 14
        };
    }

    private readonly record struct GraphBindingKey(string FilePath, int SpanStart, int SpanEnd);

    private readonly record struct TargetMatchKey(SyntaxNode SyntaxNode, string TargetNames);

    private readonly record struct SliceQueryKey(
      NodeId SinkNodeId,
      string AllowedEdgeKinds,
      int MaxHops,
      int MaxPaths,
      int MaxDefinitions,
      int MaxCallDepth)
    {
        public static SliceQueryKey Create(NodeId sinkNodeId, NLCPGSliceQueryOptions options)
        {
            return new SliceQueryKey(
              sinkNodeId,
              string.Join(",", options.AllowedEdgeKinds.OrderBy(kind => kind)),
              options.MaxHops,
              options.MaxPaths,
              options.MaxDefinitions,
              options.MaxCallDepth);
        }
    }

    private sealed record MarkRegionFacts(
      SyntaxNode RegionNode,
      Microsoft.CodeAnalysis.Text.TextSpan Span,
      int NodeCount,
      int ExpressionCount,
      int StatementCount)
    {
        public static MarkRegionFacts FromRegionNode(SyntaxNode regionNode)
        {
            var nodeCount = 0;
            var expressionCount = 0;
            var statementCount = 0;
            foreach (var node in regionNode.DescendantNodesAndSelf())
            {
                nodeCount++;
                expressionCount += node is ExpressionSyntax ? 1 : 0;
                statementCount += node is StatementSyntax ? 1 : 0;
            }

            return new MarkRegionFacts(
              regionNode,
              regionNode.Span,
              nodeCount,
              expressionCount,
              statementCount);
        }

        public MarkCodeRegion Create(SyntaxNode anchorNode)
        {
            return new MarkCodeRegion(
              anchorNode,
              RegionNode,
              Span,
              NodeCount,
              ExpressionCount,
              StatementCount);
        }
    }

    private sealed record AtomicCandidateFacts(
      IReadOnlyList<ExpressionSyntax> Candidates,
      IReadOnlyDictionary<Microsoft.CodeAnalysis.CSharp.SyntaxKind, IReadOnlyList<ExpressionSyntax>>
        CandidatesByKind);

}

public sealed class TargetNameDescriptor
{
    private TargetNameDescriptor(IReadOnlyList<string> displayNames, string cacheKey)
    {
        DisplayNames = displayNames;
        Lookup = new HashSet<string>(displayNames, StringComparer.Ordinal);
        CacheKey = cacheKey;
    }

    public IReadOnlyList<string> DisplayNames { get; }

    public IReadOnlySet<string> Lookup { get; }

    public string CacheKey { get; }

    // 解析逗号分隔的目标名字符串，并生成稳定缓存键和精确匹配集合。
    public static TargetNameDescriptor Create(string value)
    {
        var displayNames = value
          .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .Distinct(StringComparer.Ordinal)
          .ToArray();
        var cacheKey = string.Join("\u001f", displayNames.OrderBy(name => name, StringComparer.Ordinal));
        return new TargetNameDescriptor(displayNames, cacheKey);
    }
}
