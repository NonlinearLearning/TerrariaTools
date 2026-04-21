using Domain.Workspaces;

namespace Domain.Analysis;

/// <summary>
/// 定义分析快照工厂。
/// </summary>
public interface IAnalysisSnapshotFactory
{
    /// <summary>
    /// 构建 CPG 快照。
    /// </summary>
    AnalysisCpgSnapshot BuildCpgSnapshot(
        WorkspaceContext workspaceContext,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth);

    /// <summary>
    /// 构建组成层快照。
    /// </summary>
    AnalysisCompositeLayerSnapshot BuildCompositeSnapshot(
        WorkspaceContext workspaceContext,
        string compositionName,
        int depth,
        IReadOnlyCollection<string> layerNames);
}
