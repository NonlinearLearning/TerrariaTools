using Domain.Analysis.Engine.Core;
using Domain.Analysis.Engine.Semantic;
using Domain.Analysis.Engine.Semantic.Utils;

namespace Logic.Analysis.Engine.Language.NodeMethods;

/// <summary>
/// 对应 Joern semanticcpg/language/nodemethods/ExpressionMethods.scala。
///
/// 该文件提供 C# 查询 DSL 的一个命名入口，避免调用方直接操作字符串属性和边集合。
/// </summary>
public static class ExpressionMethods
{
    /// <summary>
    /// 从当前遍历中选择该文件负责的节点集合。
    /// </summary>
    /// <param name="traversal">当前遍历。</param>
    /// <returns>筛选后的遍历。</returns>
    public static Traversal Select(Traversal traversal)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        return traversal.Where(node => node.Kind is CpgNodeKind.Call or CpgNodeKind.Identifier or CpgNodeKind.Literal or CpgNodeKind.Local or CpgNodeKind.MethodRef);
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
    /// 获取表达式父表达式，跳过成员访问包装调用。
    /// </summary>
    public static CpgNode? ParentExpression(CpgGraph graph, CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        CpgNode? parent = AstNodeMethods.AstParent(graph, node);
        while (parent is not null &&
               parent.Kind == CpgNodeKind.Call &&
               parent.TryGetProperty<string>("Name", out string? name) &&
               MemberAccess.IsGenericMemberAccessName(name))
        {
            parent = AstNodeMethods.AstParent(graph, parent);
        }

        return parent is not null && IsExpression(parent) ? parent : null;
    }

    /// <summary>
    /// 获取表达式所属调用。
    /// </summary>
    public static CpgNode? InCall(CpgGraph graph, CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        return graph.GetIncomingEdges(node.Id, CpgEdgeKind.Argument)
            .Select(edge => graph.GetNode(edge.SourceId))
            .FirstOrDefault(source => source.Kind == CpgNodeKind.Call);
    }

    /// <summary>
    /// 获取表达式对应的形参。
    /// </summary>
    public static IReadOnlyList<CpgNode> Parameters(CpgGraph graph, CpgNode node)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        return graph.GetOutgoingEdges(node.Id, CpgEdgeKind.ParameterLink)
            .Select(edge => graph.GetNode(edge.TargetId))
            .Where(target => target.Kind == CpgNodeKind.MethodParameterIn)
            .ToArray();
    }

    private static bool IsExpression(CpgNode node)
    {
        return node.Kind is CpgNodeKind.Call
            or CpgNodeKind.Identifier
            or CpgNodeKind.Literal
            or CpgNodeKind.Local
            or CpgNodeKind.MethodRef
            or CpgNodeKind.ControlStructure;
    }
}
