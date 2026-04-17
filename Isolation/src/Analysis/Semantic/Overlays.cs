using Analysis.Core;

namespace Analysis.Semantic;

/// <summary>
/// 管理已经应用到 CPG 上的语义叠加层名称。
///
/// 这个类型对齐 Joern `semanticcpg/Overlays.scala`：
/// - `AppendOverlayName` 追加 overlay 名称；
/// - `RemoveLastOverlayName` 删除最后一个 overlay 名称；
/// - `AppliedOverlays` 读取当前 overlay 列表。
/// </summary>
public static class Overlays
{
    private const string OverlaysPropertyName = "Overlays";

    /// <summary>
    /// 追加一个 overlay 名称。
    /// </summary>
    /// <param name="graph">目标 CPG。</param>
    /// <param name="overlayName">overlay 名称。</param>
    public static void AppendOverlayName(CpgGraph graph, string overlayName)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayName);

        CpgNode? metaDataNode = GetMetaDataNode(graph);
        if (metaDataNode is null)
        {
            return;
        }

        List<string> overlays = AppliedOverlays(graph).ToList();
        overlays.Add(overlayName);
        metaDataNode.SetProperty(OverlaysPropertyName, overlays);
    }

    /// <summary>
    /// 删除最后一个 overlay 名称。
    /// </summary>
    /// <param name="graph">目标 CPG。</param>
    public static void RemoveLastOverlayName(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        CpgNode? metaDataNode = GetMetaDataNode(graph);
        if (metaDataNode is null)
        {
            return;
        }

        List<string> overlays = AppliedOverlays(graph).ToList();
        if (overlays.Count > 0)
        {
            overlays.RemoveAt(overlays.Count - 1);
        }

        metaDataNode.SetProperty(OverlaysPropertyName, overlays);
    }

    /// <summary>
    /// 读取当前已经应用的 overlay 名称。
    /// </summary>
    /// <param name="graph">目标 CPG。</param>
    /// <returns>overlay 名称集合。</returns>
    public static IReadOnlyList<string> AppliedOverlays(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        CpgNode? metaDataNode = GetMetaDataNode(graph);
        if (metaDataNode is null)
        {
            return Array.Empty<string>();
        }

        if (metaDataNode.TryGetProperty<IReadOnlyList<string>>(OverlaysPropertyName, out IReadOnlyList<string>? overlays))
        {
            return overlays ?? Array.Empty<string>();
        }

        if (metaDataNode.TryGetProperty<List<string>>(OverlaysPropertyName, out List<string>? overlayList))
        {
            return overlayList ?? new List<string>();
        }

        return Array.Empty<string>();
    }

    private static CpgNode? GetMetaDataNode(CpgGraph graph)
    {
        return graph.GetNodes(CpgNodeKind.MetaData).FirstOrDefault();
    }
}
