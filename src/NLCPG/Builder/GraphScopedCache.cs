using NLCPG.Model;

namespace NLCPG.Builder;

/// <summary>
/// **按图分键**的 builder 级缓存。
/// </summary>
/// <remarks>
/// <para>
/// 存在的唯一理由：<c>NLCPGBuilder</c> 的节点缓存历史上**与图无关**——
/// <c>GetOrCreateOperationNode(IOperation, NLCPGGraph)</c> 先查缓存再碰传入的 <paramref name="graph"/>，
/// 于是「某 operation 在 A 图建过节点」会让「后续为 B 图的调用返回 A 图的节点」，
/// 使两张本应不相交的图被**交叉链接**。多文件构建（每文件一张独立图）下这是静默污染。
/// </para>
/// <para>
/// 外层字典用 <see cref="ReferenceEqualityComparer"/>：<see cref="NLCPGGraph"/> 是引用身份对象，
/// 且图可能重写 <c>Equals</c>（值语义）——按引用分键才是「同一张图」的准确含义。
/// </para>
/// <para>
/// 单图构建时外层字典恒只有一个条目，内层查找与改造前**逐字等价**，
/// 故本类型不改变任何单文件行为（含缓存命中计数）。
/// </para>
/// </remarks>
internal sealed class GraphScopedCache<TKey, TValue>
  where TKey : notnull
{
    private readonly Dictionary<NLCPGGraph, Dictionary<TKey, TValue>> _byGraph =
      new(ReferenceEqualityComparer.Instance);
    private readonly IEqualityComparer<TKey> _keyComparer;

    internal GraphScopedCache(IEqualityComparer<TKey>? keyComparer = null)
    {
        _keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
    }

    internal bool TryGetValue(NLCPGGraph graph, TKey key, out TValue value)
    {
        if (_byGraph.TryGetValue(graph, out var bucket) &&
            bucket.TryGetValue(key, out value!))
        {
            return true;
        }

        value = default!;
        return false;
    }

    internal void Set(NLCPGGraph graph, TKey key, TValue value)
    {
        if (!_byGraph.TryGetValue(graph, out var bucket))
        {
            bucket = new Dictionary<TKey, TValue>(_keyComparer);
            _byGraph[graph] = bucket;
        }

        bucket[key] = value;
    }

    /// <summary>
    /// 移除某张图的全部条目。
    /// </summary>
    /// <remarks>
    /// 供「图被丢弃/重建」的路径使用。<b>注意</b>：常规的每次构建开头是整体
    /// <see cref="Clear"/>（因为 builder 可复用且每张图的节点都作废）。
    /// </remarks>
    internal void Remove(NLCPGGraph graph)
    {
        _byGraph.Remove(graph);
    }

    internal void Clear()
    {
        _byGraph.Clear();
    }

    /// <summary>仅用于可证伪性断言：当前承载条目的图数量。</summary>
    internal int GraphCount => _byGraph.Count;
}
