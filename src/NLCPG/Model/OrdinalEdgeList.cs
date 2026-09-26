namespace NLCPG.Model;

// 通过 ordinal 延迟投影 canonical 边存储，避免索引 bucket 复制边值。
internal sealed class OrdinalEdgeList : IReadOnlyList<NLCPGEdge>
{
    private readonly CanonicalEdgeStore _store;
    private readonly int[] _ordinals;
    private readonly int _offset;
    private readonly int _count;

    internal OrdinalEdgeList(CanonicalEdgeStore store, int[] ordinals, int offset, int count)
    {
        _store = store;
        _ordinals = ordinals;
        _offset = offset;
        _count = count;
    }

    public int Count => _count;

    public NLCPGEdge this[int index] => _store.Project(_ordinals[_offset + index]);

    public IEnumerator<NLCPGEdge> GetEnumerator()
    {
        for (var index = 0; index < _count; index += 1)
        {
            yield return _store.Project(_ordinals[_offset + index]);
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

// canonical 序中连续区间的投影视图，等价于原先的 ArraySegment<NLCPGEdge> 切片，
// 但不持有边值数组。对应 outgoing 方向（同一 source 的边在 canonical 序中天然连续）。
internal sealed class CanonicalEdgeList : IReadOnlyList<NLCPGEdge>
{
    private readonly CanonicalEdgeStore _store;
    private readonly int _offset;
    private readonly int _count;

    internal CanonicalEdgeList(CanonicalEdgeStore store, int offset, int count)
    {
        _store = store;
        _offset = offset;
        _count = count;
    }

    public int Count => _count;

    public NLCPGEdge this[int index] => _store.Project(_offset + index);

    public IEnumerator<NLCPGEdge> GetEnumerator()
    {
        for (var index = 0; index < _count; index += 1)
        {
            yield return _store.Project(_offset + index);
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
