using Domain.Analysis;

namespace Infrastructure.Persistence;

/// <summary>
/// 内存版分析快照仓储。
/// </summary>
public sealed class InMemoryAnalysisSnapshotRepository : IAnalysisSnapshotRepository
{
    private readonly Dictionary<Guid, AnalysisCpgSnapshot> cpgSnapshots = new();
    private readonly Dictionary<Guid, AnalysisCompositeLayerSnapshot> compositeSnapshots = new();

    /// <inheritdoc />
    public Task AddCpgSnapshotAsync(AnalysisCpgSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cpgSnapshots[snapshot.Id] = snapshot;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<AnalysisCpgSnapshot?> GetCpgSnapshotAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cpgSnapshots.TryGetValue(id, out AnalysisCpgSnapshot? snapshot);
        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task AddCompositeSnapshotAsync(AnalysisCompositeLayerSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        compositeSnapshots[snapshot.Id] = snapshot;
        return Task.CompletedTask;
    }
}
