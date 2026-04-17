using Analysis.Core;
using Analysis.Semantic;

namespace Analysis.Language;

/// <summary>
/// 表示一组 CPG 节点上的链式遍历。
///
/// 这个类型是 C# 版 Joern DSL 的核心：
/// - 保存当前节点集合；
/// - 提供按属性、节点类型、边类型继续遍历的能力；
/// - 保持只读，不修改底层 CPG。
/// </summary>
public sealed class Traversal
{
    private readonly CpgGraph graph;
    private readonly IReadOnlyList<CpgNode> nodes;

    /// <summary>
    /// 使用图和节点集合初始化遍历对象。
    /// </summary>
    /// <param name="graph">所属 CPG。</param>
    /// <param name="nodes">当前节点集合。</param>
    public Traversal(CpgGraph graph, IEnumerable<CpgNode> nodes)
    {
        this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
        ArgumentNullException.ThrowIfNull(nodes);
        this.nodes = nodes.DistinctBy(node => node.Id).ToArray();
    }

    /// <summary>
    /// 获取所属 CPG。
    /// </summary>
    public CpgGraph Graph => graph;

    /// <summary>
    /// 只保留指定类型的节点。
    /// </summary>
    /// <param name="kind">目标节点类型。</param>
    /// <returns>新的遍历对象。</returns>
    public Traversal OfKind(CpgNodeKind kind)
    {
        return new Traversal(graph, nodes.Where(node => node.Kind == kind));
    }

    /// <summary>
    /// 按节点名称精确匹配。
    /// </summary>
    /// <param name="name">目标名称。</param>
    /// <returns>新的遍历对象。</returns>
    public Traversal Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Property("Name", name);
    }

    /// <summary>
    /// 按完整名精确匹配。
    /// </summary>
    /// <param name="fullName">目标完整名。</param>
    /// <returns>新的遍历对象。</returns>
    public Traversal FullName(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        return Property("FullName", fullName);
    }

    /// <summary>
    /// 按任意字符串属性精确匹配。
    /// </summary>
    /// <param name="propertyName">属性名。</param>
    /// <param name="expectedValue">期望值。</param>
    /// <returns>新的遍历对象。</returns>
    public Traversal Property(string propertyName, string expectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(expectedValue);
        return new Traversal(graph, nodes.Where(node => node.HasPropertyValue(propertyName, expectedValue)));
    }

    /// <summary>
    /// 按自定义谓词筛选节点。
    /// </summary>
    /// <param name="predicate">筛选谓词。</param>
    /// <returns>新的遍历对象。</returns>
    public Traversal Where(Func<CpgNode, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new Traversal(graph, nodes.Where(predicate));
    }

    /// <summary>
    /// 沿指定出边访问终点。
    /// </summary>
    /// <param name="kind">边类型。</param>
    /// <returns>新的遍历对象。</returns>
    public Traversal Out(CpgEdgeKind kind)
    {
        return new Traversal(
            graph,
            nodes.SelectMany(node => graph.GetOutgoingEdges(node.Id, kind))
                .Select(edge => graph.GetNode(edge.TargetId)));
    }

    /// <summary>
    /// 沿指定入边访问起点。
    /// </summary>
    /// <param name="kind">边类型。</param>
    /// <returns>新的遍历对象。</returns>
    public Traversal In(CpgEdgeKind kind)
    {
        return new Traversal(
            graph,
            nodes.SelectMany(node => graph.GetIncomingEdges(node.Id, kind))
                .Select(edge => graph.GetNode(edge.SourceId)));
    }

    /// <summary>
    /// 访问 AST 子节点。
    /// </summary>
    /// <returns>新的遍历对象。</returns>
    public Traversal Ast()
    {
        return Out(CpgEdgeKind.Ast);
    }

    /// <summary>
    /// 访问 AST 父节点。
    /// </summary>
    /// <returns>新的遍历对象。</returns>
    public Traversal AstParent()
    {
        return In(CpgEdgeKind.Ast);
    }

    /// <summary>
    /// 访问当前节点 AST 子树中的调用节点。
    /// </summary>
    /// <returns>新的遍历对象。</returns>
    public Traversal Calls()
    {
        return new Traversal(graph, Descendants(nodes).Where(node => node.Kind == CpgNodeKind.Call));
    }

    /// <summary>
    /// 访问当前节点解析出的被调方法。
    /// </summary>
    /// <returns>新的遍历对象。</returns>
    public Traversal Callees()
    {
        return Out(CpgEdgeKind.Call).OfKind(CpgNodeKind.Method);
    }

    /// <summary>
    /// 访问当前节点的控制流后继。
    /// </summary>
    /// <returns>新的遍历对象。</returns>
    public Traversal CfgNext()
    {
        return Out(CpgEdgeKind.Cfg);
    }

    /// <summary>
    /// 访问当前节点的控制流前驱。
    /// </summary>
    /// <returns>新的遍历对象。</returns>
    public Traversal CfgPrev()
    {
        return In(CpgEdgeKind.Cfg);
    }

    /// <summary>
    /// 访问当前节点的数据流后继。
    /// </summary>
    /// <returns>新的遍历对象。</returns>
    public Traversal DdgOut()
    {
        return new Traversal(
            graph,
            nodes.SelectMany(node => graph.GetOutgoingEdges(node.Id, CpgEdgeKind.ReachingDef)
                    .Concat(graph.GetOutgoingEdges(node.Id, CpgEdgeKind.ParameterLink)))
                .Select(edge => graph.GetNode(edge.TargetId)));
    }

    /// <summary>
    /// 访问当前节点的数据流前驱。
    /// </summary>
    /// <returns>新的遍历对象。</returns>
    public Traversal DdgIn()
    {
        return new Traversal(
            graph,
            nodes.SelectMany(node => graph.GetIncomingEdges(node.Id, CpgEdgeKind.ReachingDef)
                    .Concat(graph.GetIncomingEdges(node.Id, CpgEdgeKind.ParameterLink)))
                .Select(edge => graph.GetNode(edge.SourceId)));
    }

    /// <summary>
    /// 访问当前调用节点的参数节点。
    /// </summary>
    /// <param name="argumentIndex">可选参数序号。为空时返回全部参数。</param>
    /// <returns>新的遍历对象。</returns>
    public Traversal Arguments(int? argumentIndex = null)
    {
        IEnumerable<CpgNode> arguments = nodes
            .Where(node => node.Kind == CpgNodeKind.Call)
            .SelectMany(node => graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Ast))
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(node => argumentIndex is null ||
                           node.TryGetProperty<int>("ArgumentIndex", out int actualIndex) &&
                           actualIndex == argumentIndex.Value);
        return new Traversal(graph, arguments);
    }

    /// <summary>
    /// 访问当前方法的顶层语句。
    /// </summary>
    /// <returns>新的遍历对象。</returns>
    public Traversal Statements()
    {
        IEnumerable<CpgNode> statements = nodes
            .Where(node => node.Kind == CpgNodeKind.Method)
            .SelectMany(node => graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Ast))
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(IsStatementNode);
        return new Traversal(graph, statements);
    }

    /// <summary>
    /// 访问 AST 子树中的所有节点。
    /// </summary>
    /// <returns>新的遍历对象。</returns>
    public Traversal AstDescendants()
    {
        return new Traversal(graph, Descendants(nodes));
    }

    /// <summary>
    /// 物化当前节点集合。
    /// </summary>
    /// <returns>节点列表。</returns>
    public IReadOnlyList<CpgNode> ToList()
    {
        return nodes;
    }

    /// <summary>
    /// 返回第一个节点，没有时返回空。
    /// </summary>
    /// <returns>第一个节点或空。</returns>
    public CpgNode? FirstOrDefault()
    {
        return nodes.FirstOrDefault();
    }

    private IEnumerable<CpgNode> Descendants(IEnumerable<CpgNode> roots)
    {
        HashSet<long> visited = new();
        Stack<CpgNode> stack = new(roots.Reverse());

        while (stack.Count > 0)
        {
            CpgNode current = stack.Pop();
            foreach (CpgNode child in graph.GetOutgoingEdges(current.Id, CpgEdgeKind.Ast)
                         .Select(edge => graph.GetNode(edge.TargetId))
                         .Reverse())
            {
                if (visited.Add(child.Id))
                {
                    yield return child;
                    stack.Push(child);
                }
            }
        }
    }

    private static bool IsStatementNode(CpgNode node)
    {
        return node.Kind is CpgNodeKind.Call
            or CpgNodeKind.ControlStructure
            or CpgNodeKind.Local
            or CpgNodeKind.Identifier
            or CpgNodeKind.Literal;
    }
}
