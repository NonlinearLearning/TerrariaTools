namespace NLCPG.Persistence;

public sealed class CpgFrozenShardReader : ICpgShardReader
{
    private readonly ICpgShardStore _store;

    // 绑定底层分片存储读取器。
    public CpgFrozenShardReader(ICpgShardStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    // 按目录租约打开分片，并确认分片头仍与租约匹配。
    public async ValueTask<CpgFrozenShard> OpenAsync(CpgShardLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var shard = await _store.TryReadAsync(lease.Location, lease.Lookup, cancellationToken);
        return shard ?? throw new InvalidDataException("The CPG shard does not match its catalog lease.");
    }
}
