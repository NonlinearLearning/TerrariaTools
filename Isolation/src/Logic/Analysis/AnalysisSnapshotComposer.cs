using Domain.Analysis;
using Domain.Workspaces;

namespace Logic.Analysis;

/// <summary>
/// 分析快照构造器。
/// </summary>
public sealed class AnalysisSnapshotComposer : IAnalysisSnapshotComposer
{
    private readonly IAnalysisSnapshotFactory analysisSnapshotFactory;

    /// <summary>
    /// 初始化分析快照构造器。
    /// </summary>
    /// <param name="analysisSnapshotFactory">分析快照工厂。</param>
    public AnalysisSnapshotComposer(IAnalysisSnapshotFactory analysisSnapshotFactory)
    {
        this.analysisSnapshotFactory = analysisSnapshotFactory;
    }


    public AnalysisCpgSnapshot BuildCpgSnapshot(
        WorkspaceContext workspaceContext,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth)
    {
        return analysisSnapshotFactory.BuildCpgSnapshot(
            workspaceContext,
            minimumTarget,
            entrySymbol,
            depth);
    }


    public AnalysisCompositeLayerSnapshot BuildCompositeSnapshot(
        WorkspaceContext workspaceContext,
        string compositionName,
        int depth,
        IReadOnlyCollection<string> layerNames)
    {
        return analysisSnapshotFactory.BuildCompositeSnapshot(
            workspaceContext,
            compositionName,
            depth,
            layerNames);
    }
}
