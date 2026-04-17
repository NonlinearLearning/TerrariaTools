using Analysis.Core;
using Analysis.Query;

namespace Analysis.Language.DataFlow.Nodemethods;

/// <summary>
/// 数据流专用表达式节点方法。
///
/// 对应 Joern `dataflowengineoss/language/nodemethods/ExpressionMethods.scala`。
/// </summary>
public static class ExpressionMethods
{
    /// <summary>
    /// 从表达式节点提取访问路径。
    /// </summary>
    public static AccessPathUsage AccessPath(CpgGraph graph, CpgNode expressionNode)
    {
        return AccessPathUsage.FromNode(graph, expressionNode);
    }
}
