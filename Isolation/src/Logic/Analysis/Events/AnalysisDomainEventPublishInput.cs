using Domain.Analysis;
using Domain.Workspaces;

namespace Logic.Analysis.Events;

/// <summary>
/// 表示分析阶段领域事件发布输入。
/// </summary>
public sealed class AnalysisDomainEventPublishInput
{
    public Guid RunCorrelationId { get; init; }

    public WorkspaceContext WorkspaceContext { get; init; } = null!;

    public AnalysisCpgSnapshot? CpgSnapshot { get; init; }

    public AnalysisCompositeLayerSnapshot? CompositeSnapshot { get; init; }

    public string EntrySymbol { get; init; } = string.Empty;

    public int Depth { get; init; }
}
