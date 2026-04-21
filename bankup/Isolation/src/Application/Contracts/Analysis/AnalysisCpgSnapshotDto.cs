using Application.Contracts;

namespace Application.Contracts.Analysis;

/// <summary>
/// CPG 快照 DTO。
/// </summary>
public sealed class AnalysisCpgSnapshotDto
{
    public Guid Id { get; init; }

    public Guid WorkspaceContextId { get; init; }

    public ContractMinimumAnalysisTarget MinimumTarget { get; init; }

    public string EntrySymbol { get; init; } = string.Empty;

    public int Depth { get; init; }

    public IReadOnlyCollection<MinimumNodeDto> Nodes { get; init; } = Array.Empty<MinimumNodeDto>();
}
