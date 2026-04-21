using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes.DataFlow;

/// <summary>
/// 校验 `ReachingDef` 边是否应该参与数据流查询。
///
/// 对应 Joern `EdgeValidator.scala`。完整 Joern 会结合外部语义规则和调用参数方向。
/// 当前 C# 版用节点属性表达同一类决策，避免把查询引擎和具体前端类型绑死。
/// </summary>
public sealed class EdgeValidator
{
    /// <summary>
    /// 判断从父节点到子节点的数据流边是否有效。
    /// </summary>
    public bool IsValidEdge(CpgNode childNode, CpgNode parentNode)
    {
        ArgumentNullException.ThrowIfNull(childNode);
        ArgumentNullException.ThrowIfNull(parentNode);

        if (IsCallReturnValue(parentNode) && !ExplicitlyFlowsToReturnValue(parentNode))
        {
            return false;
        }

        if (IsExpression(childNode) && !IsUsed(childNode) && !IsDefined(childNode))
        {
            return false;
        }

        if (IsExpression(childNode) && IsExpression(parentNode) && SameCallSite(childNode, parentNode))
        {
            return !IsUsed(parentNode) || !IsDefined(childNode) || HasDefinedFlowTo(parentNode, childNode);
        }

        return true;
    }

    private static bool IsExpression(CpgNode node)
    {
        return node.Kind is CpgNodeKind.Call
            or CpgNodeKind.Identifier
            or CpgNodeKind.Literal
            or CpgNodeKind.Local
            or CpgNodeKind.Member;
    }

    private static bool IsUsed(CpgNode node)
    {
        return !node.TryGetProperty<bool>("IsUsed", out bool value) || value;
    }

    private static bool IsDefined(CpgNode node)
    {
        return node.TryGetProperty<bool>("IsDefined", out bool value) && value;
    }

    private static bool IsCallReturnValue(CpgNode node)
    {
        return node.Kind == CpgNodeKind.Call &&
               node.TryGetProperty<bool>("IsCallReturnValue", out bool value) &&
               value;
    }

    private static bool ExplicitlyFlowsToReturnValue(CpgNode node)
    {
        return node.TryGetProperty<bool>("FlowsToReturnValue", out bool value) && value;
    }

    private static bool SameCallSite(CpgNode left, CpgNode right)
    {
        return left.TryGetProperty<long>("CallSiteId", out long leftCallSiteId) &&
               right.TryGetProperty<long>("CallSiteId", out long rightCallSiteId) &&
               leftCallSiteId == rightCallSiteId;
    }

    private static bool HasDefinedFlowTo(CpgNode parentNode, CpgNode childNode)
    {
        if (!parentNode.TryGetProperty<IReadOnlyCollection<long>>("DefinedFlowToNodeIds", out IReadOnlyCollection<long>? targetIds))
        {
            return true;
        }

        return targetIds.Contains(childNode.Id);
    }
}
