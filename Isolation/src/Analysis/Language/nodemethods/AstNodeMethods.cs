using Analysis.Core;
using Analysis.Semantic;
using Analysis.Semantic.Utils;

namespace Analysis.Language.Nodemethods;

/// <summary>
/// 对应 Joern semanticcpg/language/nodemethods/AstNodeMethods.scala。
///
/// 该文件提供 C# 查询 DSL 的一个命名入口，避免调用方直接操作字符串属性和边集合。
/// </summary>
public static class AstNodeMethods
{
    /// <summary>
    /// 从当前遍历中选择该文件负责的节点集合。
    /// </summary>
    /// <param name="traversal">当前遍历。</param>
    /// <returns>筛选后的遍历。</returns>
    public static Traversal Select(Traversal traversal)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        return traversal;
    }

    /// <summary>
    /// 读取节点名称。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <returns>节点名称。</returns>
    public static string Name(CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.PropertyAsString("Name");
    }

    /// <summary>
    /// 读取节点源码文本。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <returns>源码文本。</returns>
    public static string Code(CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.PropertyAsString("Code");
    }

    /// <summary>
    /// 计算 AST 子树深度。
    /// </summary>
    public static int Depth(CpgGraph graph, CpgNode node, Func<CpgNode, bool>? predicate = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        predicate ??= _ => true;
        int ownDepth = predicate(node) ? 1 : 0;
        int childDepth = AstChildren(graph, node)
            .Select(child => Depth(graph, child, predicate))
            .DefaultIfEmpty(0)
            .Max();
        return ownDepth + childDepth;
    }

    /// <summary>
    /// 获取 AST 父节点。
    /// </summary>
    public static CpgNode? AstParent(CpgGraph graph, CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        return graph.GetIncomingEdges(node.Id, CpgEdgeKind.Ast)
            .Select(edge => graph.GetNode(edge.SourceId))
            .SingleOrDefault();
    }

    /// <summary>
    /// 获取按 `Order` 排序的 AST 子节点。
    /// </summary>
    public static IReadOnlyList<CpgNode> AstChildren(CpgGraph graph, CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        return graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Ast)
            .Select(edge => graph.GetNode(edge.TargetId))
            .OrderBy(child => child.TryGetProperty<int>("Order", out int order) ? order : int.MaxValue)
            .ThenBy(child => child.Id)
            .ToArray();
    }

    /// <summary>
    /// 获取表达式所属语句。
    /// </summary>
    public static CpgNode Statement(CpgGraph graph, CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);

        if (node.Kind is CpgNodeKind.Identifier or CpgNodeKind.Literal or CpgNodeKind.MethodRef)
        {
            CpgNode? parent = AstParent(graph, node);
            return parent is null ? node : Statement(graph, parent);
        }

        if (node.Kind == CpgNodeKind.Call &&
            node.TryGetProperty<string>("Name", out string? name) &&
            MemberAccess.IsGenericMemberAccessName(name))
        {
            CpgNode? parent = AstParent(graph, node);
            return parent is null ? node : Statement(graph, parent);
        }

        if (node.Kind == CpgNodeKind.Block)
        {
            return AstChildren(graph, node)
                .Where(IsExpression)
                .LastOrDefault() ?? node;
        }

        return node;
    }

    private static bool IsExpression(CpgNode node)
    {
        return node.Kind is CpgNodeKind.Call
            or CpgNodeKind.Identifier
            or CpgNodeKind.Literal
            or CpgNodeKind.Local
            or CpgNodeKind.ControlStructure
            or CpgNodeKind.MethodReturn;
    }
}
