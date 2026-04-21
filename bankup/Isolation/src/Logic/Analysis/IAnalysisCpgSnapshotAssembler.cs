using Domain.Analysis;
using Domain.Analysis.Engine.Core;

namespace Logic.Analysis;

/// <summary>
/// 定义 CPG 图到领域快照的组装能力。
/// </summary>
public interface IAnalysisCpgSnapshotAssembler
{
    /// <summary>
    /// 将分析图组装为领域 CPG 快照。
    /// </summary>
    AnalysisCpgSnapshot Assemble(
        CpgGraph graph,
        AnalysisInputDescriptor inputDescriptor,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth);
}
