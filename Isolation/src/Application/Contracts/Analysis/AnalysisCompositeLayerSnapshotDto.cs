namespace Application.Contracts.Analysis;

/// <summary>
/// 组成层快照 DTO。
/// </summary>
public sealed class AnalysisCompositeLayerSnapshotDto
{
    public Guid Id { get; init; }

    public string CompositionName { get; init; } = string.Empty;

    public int Depth { get; init; }

    public IReadOnlyCollection<string> LayerNames { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<MinimumNodeDto> Nodes { get; init; } = Array.Empty<MinimumNodeDto>();
}
