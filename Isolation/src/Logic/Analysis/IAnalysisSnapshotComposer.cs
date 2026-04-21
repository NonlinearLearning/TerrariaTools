using Domain.Analysis;
using Domain.Workspaces;

namespace Logic.Analysis;

/// <summary>
/// 定义分析快照构造能力。
/// </summary>
public interface IAnalysisSnapshotComposer
{
    /// <summary>
    /// 构造 CPG 快照。
    /// </summary>
    AnalysisCpgSnapshot BuildCpgSnapshot(
        WorkspaceContext workspaceContext,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth);

    /// <summary>
    /// 构造组合层快照。
    /// </summary>
    AnalysisCompositeLayerSnapshot BuildCompositeSnapshot(
        WorkspaceContext workspaceContext,
        string compositionName,
        int depth,
        IReadOnlyCollection<string> layerNames);
}
