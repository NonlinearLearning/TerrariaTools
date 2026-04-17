using Analysis.Core;
using Analysis.Semantic;

namespace Analysis.Language.Nodemethods;

/// <summary>
/// 对应 Joern semanticcpg/language/nodemethods/CallMethods.scala。
///
/// 该文件提供 C# 查询 DSL 的一个命名入口，避免调用方直接操作字符串属性和边集合。
/// </summary>
public static class CallMethods
{
    /// <summary>
    /// 从当前遍历中选择该文件负责的节点集合。
    /// </summary>
    /// <param name="traversal">当前遍历。</param>
    /// <returns>筛选后的遍历。</returns>
    public static Traversal Select(Traversal traversal)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        return traversal.OfKind(CpgNodeKind.Call);
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
    /// 当前调用是否是静态分派。
    /// </summary>
    public static bool IsStatic(CpgNode node) => HasDispatchType(node, "STATIC_DISPATCH");

    /// <summary>
    /// 当前调用是否是动态分派。
    /// </summary>
    public static bool IsDynamic(CpgNode node) => HasDispatchType(node, "DYNAMIC_DISPATCH");

    /// <summary>
    /// 当前调用是否是内联调用。
    /// </summary>
    public static bool IsInline(CpgNode node) => HasDispatchType(node, "INLINED");

    /// <summary>
    /// 获取调用 receiver。
    /// </summary>
    public static IReadOnlyList<CpgNode> Receiver(CpgGraph graph, CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        return graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Receiver)
            .Select(edge => graph.GetNode(edge.TargetId))
            .ToArray();
    }

    /// <summary>
    /// 获取调用参数。
    /// </summary>
    public static IReadOnlyList<CpgNode> Arguments(CpgGraph graph, CpgNode node, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        return graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Argument)
            .Concat(graph.GetOutgoingEdges(node.Id, CpgEdgeKind.Ast))
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(argument => index is null ||
                               argument.TryGetProperty<int>("ArgumentIndex", out int actualIndex) &&
                               actualIndex == index.Value)
            .DistinctBy(argument => argument.Id)
            .OrderBy(argument => argument.TryGetProperty<int>("ArgumentIndex", out int actualIndex) ? actualIndex : int.MaxValue)
            .ToArray();
    }

    private static bool HasDispatchType(CpgNode node, string dispatchType)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.TryGetProperty<string>("DispatchType", out string? actual) &&
               string.Equals(actual, dispatchType, StringComparison.Ordinal);
    }
}
