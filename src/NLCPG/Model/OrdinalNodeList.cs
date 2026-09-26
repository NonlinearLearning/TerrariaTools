namespace NLCPG.Model;

// 通过 ordinal 延迟读取 canonical node 数组，避免索引 bucket 复制节点值。
internal sealed class OrdinalNodeList : IReadOnlyList<NLCPGNode>
{
    private readonly NLCPGNode[] _nodes;
    private readonly int[] _ordinals;

    internal OrdinalNodeList(NLCPGNode[] nodes, int[] ordinals)
    {
        _nodes = nodes;
        _ordinals = ordinals;
    }

    public int Count => _ordinals.Length;

    public NLCPGNode this[int index] => _nodes[_ordinals[index]];

    public IEnumerator<NLCPGNode> GetEnumerator()
    {
        foreach (var ordinal in _ordinals)
        {
            yield return _nodes[ordinal];
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
