using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 收敛图节点的通用属性写入规则。
/// </summary>
public static class GraphNodeConventions
{
    /// <summary>
    /// 追加下一个 CFG 节点标识。
    /// </summary>
    public static void AppendNextCfgNodeId(CpgNode source, long targetId)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<long> nextIds = source.TryGetProperty<IReadOnlyCollection<long>>(
            "NextCfgNodeIds",
            out IReadOnlyCollection<long>? existing)
            ? existing.ToList()
            : new List<long>();

        if (!nextIds.Contains(targetId))
        {
            nextIds.Add(targetId);
            source.SetProperty("NextCfgNodeIds", nextIds);
        }
    }

    /// <summary>
    /// 写入行列位置。
    /// </summary>
    public static void SetLocation(CpgNode node, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetProperty("Line", line);
        node.SetProperty("Column", column);
    }
}
