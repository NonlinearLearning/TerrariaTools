using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Semantic;

namespace Logic.Analysis.Engine.Language.Nodemethods;

/// <summary>
/// 对应 Joern semanticcpg/language/nodemethods/MethodMethods.scala。
///
/// 该文件提供 C# 查询 DSL 的一个命名入口，避免调用方直接操作字符串属性和边集合。
/// </summary>
public static class MethodMethods
{
    /// <summary>
    /// 从当前遍历中选择该文件负责的节点集合。
    /// </summary>
    /// <param name="traversal">当前遍历。</param>
    /// <returns>筛选后的遍历。</returns>
    public static Traversal Select(Traversal traversal)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        return traversal.OfKind(CpgNodeKind.Method);
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
    /// 获取方法顶层表达式，跳过局部变量声明。
    /// </summary>
    public static IReadOnlyList<CpgNode> TopLevelExpressions(CpgGraph graph, CpgNode method)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(method);
        CpgNode? block = Body(graph, method);
        if (block is null)
        {
            return Array.Empty<CpgNode>();
        }

        return graph.GetOutgoingEdges(block.Id, CpgEdgeKind.Ast)
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(node => node.Kind != CpgNodeKind.Local && node.Kind != CpgNodeKind.Method)
            .OrderBy(node => node.TryGetProperty<int>("Order", out int order) ? order : int.MaxValue)
            .ThenBy(node => node.Id)
            .ToArray();
    }

    /// <summary>
    /// 计算方法源码行数。
    /// </summary>
    public static int NumberOfLines(CpgNode method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.TryGetProperty<int>("Line", out int line) &&
               method.TryGetProperty<int>("LineEnd", out int lineEnd)
            ? lineEnd - line + 1
            : 0;
    }

    /// <summary>
    /// 获取方法体块。
    /// </summary>
    public static CpgNode? Body(CpgGraph graph, CpgNode method)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(method);
        return graph.GetOutgoingEdges(method.Id, CpgEdgeKind.Ast)
            .Select(edge => graph.GetNode(edge.TargetId))
            .FirstOrDefault(node => node.Kind == CpgNodeKind.Block);
    }

    /// <summary>
    /// 判断方法是否有可变参数。
    /// </summary>
    public static bool IsVariadic(CpgGraph graph, CpgNode method)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(method);
        return graph.GetOutgoingEdges(method.Id, CpgEdgeKind.Ast)
            .Select(edge => graph.GetNode(edge.TargetId))
            .Any(node => node.Kind == CpgNodeKind.MethodParameterIn &&
                         node.TryGetProperty<bool>("IsVariadic", out bool isVariadic) &&
                         isVariadic);
    }
}
