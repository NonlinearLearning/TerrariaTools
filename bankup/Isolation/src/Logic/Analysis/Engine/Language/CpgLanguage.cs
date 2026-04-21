using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Language;

/// <summary>
/// 提供 CPG 查询 DSL 的入口。
/// </summary>
public static class CpgLanguage
{
    /// <summary>
    /// 从整张图开始遍历。
    /// </summary>
    /// <param name="graph">目标 CPG。</param>
    /// <returns>遍历对象。</returns>
    public static Traversal Start(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new Traversal(graph, graph.Nodes);
    }

    /// <summary>
    /// 从方法节点开始遍历。
    /// </summary>
    public static Traversal Methods(this CpgGraph graph) => Start(graph).OfKind(CpgNodeKind.Method);

    /// <summary>
    /// 从调用节点开始遍历。
    /// </summary>
    public static Traversal Calls(this CpgGraph graph) => Start(graph).OfKind(CpgNodeKind.Call);

    /// <summary>
    /// 从标识符节点开始遍历。
    /// </summary>
    public static Traversal Identifiers(this CpgGraph graph) => Start(graph).OfKind(CpgNodeKind.Identifier);

    /// <summary>
    /// 从局部变量节点开始遍历。
    /// </summary>
    public static Traversal Locals(this CpgGraph graph) => Start(graph).OfKind(CpgNodeKind.Local);

    /// <summary>
    /// 从类型声明节点开始遍历。
    /// </summary>
    public static Traversal TypeDecls(this CpgGraph graph) => Start(graph).OfKind(CpgNodeKind.TypeDecl);
}
