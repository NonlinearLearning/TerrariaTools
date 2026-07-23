namespace MinimalRoslynCpg.Persistence;

public sealed class CpgFrozenShardReader : ICpgShardReader
{
  private readonly ICpgShardStore _store;

  public CpgFrozenShardReader(ICpgShardStore store)
  {
    _store = store ?? throw new ArgumentNullException(nameof(store));
  }

  public async ValueTask<CpgFrozenShard> OpenAsync(CpgShardLease lease, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(lease);
    var shard = await _store.TryReadAsync(lease.Location, lease.Lookup, cancellationToken);
    return shard ?? throw new InvalidDataException("The CPG shard does not match its catalog lease.");
  }
}
