using Analysis.Core;
using Analysis.Semantic;

namespace Analysis.Language.Dotextension;

/// <summary>
/// 对应 Joern semanticcpg/language/dotextension/Shared.scala。
///
/// 该文件提供 C# 查询 DSL 的一个命名入口，避免调用方直接操作字符串属性和边集合。
/// </summary>
public static class Shared
{
    /// <summary>
    /// 从当前遍历中选择该文件负责的节点集合。
    /// </summary>
    /// <param name="traversal">当前遍历。</param>
    /// <returns>筛选后的遍历。</returns>
    public static Traversal Select(Traversal traversal)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        return CpgNodeKind.Unknown == CpgNodeKind.Unknown ? traversal : traversal.OfKind(CpgNodeKind.Unknown);
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
}
