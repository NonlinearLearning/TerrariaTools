namespace NLCPG.Model;

/// <summary>
/// 为稳定节点锚点分配可重复生成的连续 <see cref="NodeId"/>。
/// </summary>
public sealed class DeterministicNodeIdTable
{
    private readonly Dictionary<StableNodeAnchor, NodeId> _ids;

    private DeterministicNodeIdTable(Dictionary<StableNodeAnchor, NodeId> ids)
    {
        _ids = ids;
    }

    // 按稳定锚点的确定性排序为节点分配连续 NodeId。
    //
    // 【为什么不再用 Distinct() + 7 键 LINQ 排序】
    // 原实现是 `anchors.Distinct().OrderBy(...).ThenBy(...)×6.ToArray()`。在 NPC.cs 级规模
    // （本仓库实测 WorldGen.cs：1,591,165 锚点）上，三段成本为：
    //   排序 607.84 ms / 133.6 MiB、字典构建 268.41 ms / 234.5 MiB、Distinct 329.14 ms / 63.9 MiB
    // （合计 1,205 ms / 431.9 MiB，探针 Build\freeze-seg-probe 三段分解实测，中位数）。
    //
    // 改动有两处，各自独立可证：
    //
    // ① `Distinct()` 是恒等操作，可直接删除。
    //    证明（NLCPGGraph.AddNode，NLCPGGraph.cs:282-286）：
    //      `_nodesByOrdinal.Add(...)` 只在 `!_mutableNodesByAnchor.TryGetValue(anchor, ...)`
    //      为真时发生，故每个锚点最多入列一次；`AssignDeterministicNodeIds` 的输入正是
    //      `_nodesByOrdinal.Select(...)`（:925-931），因此其 Anchor 序列天然无重复。
    //    另外两条调用路径用 HashSet 承载锚点（CpgStableAnchorCollector._anchors、
    //    CpgFragmentReducer.materializedDescriptors.Keys），同样无重复。
    //    实测佐证：WorldGen.cs 1,591,165 锚点，Distinct 后仍 1,591,165。
    //
    // ② 排序改用 Array.Sort + 结构比较器，并在排序后做相邻去重。
    //    等价性证明：StableNodeAnchor 是 7 字段 record struct，其生成的 Equals/CompareTo
    //    基于【同一组 7 个字段】，故 `a.Equals(b)` ⟺ 7 键比较结果为 0。于是：
    //      - 排序后相等的锚点必然相邻 ⇒ "跳过与前一个相等的元素"与 Distinct 的集合语义相同；
    //      - 对互不相等的锚点，比较器给出全序，排序结果唯一 ⇒ NodeId = index + 1 的映射唯一。
    //    Array.Sort 不稳定，但比较为 0 的两个锚点彼此相等（不可区分），谁在前都不改变
    //    "唯一锚点序列"，故 NodeId 指派与原实现逐项相同。
    //    保留相邻去重（而非依赖 ① 的无重复前提）是为了让本方法对任何输入都保持
    //    原实现的集合语义；对无重复输入它只是 1 次比较/元素，相对排序开销可忽略。
    //
    // 两处合计实测（WorldGen.cs，交替采样中位数，Build\freeze-idtable-probe）：
    //   1,124.38 ms → 469.72 ms（2.39×），431.9 MiB → 277.0 MiB（−35.9%），
    //   且新旧两条路径产出的 锚点→NodeId 字典逐项完全一致（EQUIVALENCE_MISMATCHES=0）。
    public static DeterministicNodeIdTable Create(IEnumerable<StableNodeAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);

        // 复制成独占数组后就地排序：不再产生 Distinct 的中间集合，
        // 也不再产生 OrderBy 的键数组与 IOrderedEnumerable 链。
        //
        // 这里【必须复制】而不能像 NLCPGGraphIndex.Create 那样 `as NLCPGNode[]` 接管实例：
        // 本方法是公开 API，输入数组归调用方所有。原实现走 OrderBy（非破坏性），
        // 若改为对入参数组原地排序，就会给调用方留下"数组顺序被改"的隐藏副作用
        // （tests 里就有直接传数组字面量的调用）。一次 24 B/元素的复制相对排序开销很小。
        var orderedAnchors = anchors.ToArray();
        Array.Sort(orderedAnchors, StableAnchorComparer.Instance);

        var ids = new Dictionary<StableNodeAnchor, NodeId>(orderedAnchors.Length);
        for (var index = 0; index < orderedAnchors.Length; index += 1)
        {
            // 相邻去重：相等锚点排序后必然相邻，保留首个即可（与 Distinct 保留首次出现一致；
            // 相等元素完全不可区分，故保留哪一个不影响映射）。
            if (index > 0 && orderedAnchors[index].Equals(orderedAnchors[index - 1]))
            {
                continue;
            }

            ids[orderedAnchors[index]] = new NodeId((uint)ids.Count + 1);
        }

        return new DeterministicNodeIdTable(ids);
    }

    // 尝试读取给定稳定锚点对应的 NodeId。
    public bool TryGetNodeId(StableNodeAnchor anchor, out NodeId nodeId)
    {
        return _ids.TryGetValue(anchor, out nodeId);
    }

    // 返回给定稳定锚点的 NodeId；缺失时抛出异常。
    public NodeId GetRequiredId(StableNodeAnchor anchor)
    {
        return _ids.TryGetValue(anchor, out var nodeId)
          ? nodeId
          : throw new KeyNotFoundException($"Stable anchor was not preallocated: {anchor}.");
    }

    // 判断当前分配表是否包含给定稳定锚点。
    public bool Contains(StableNodeAnchor anchor)
    {
        return _ids.ContainsKey(anchor);
    }

    /// <summary>已分配节点标识的数量。</summary>
    public int Count => _ids.Count;

    // 暴露当前稳定锚点到 NodeId 的只读视图。
    public IReadOnlyDictionary<StableNodeAnchor, NodeId> Snapshot()
    {
        return _ids;
    }

    // StableNodeAnchor 的 7 字段全序比较器。
    //
    // 键序必须与原先 `OrderBy(Kind).ThenBy(FilePathId).ThenBy(SpanStart).ThenBy(SpanEnd)
    // .ThenBy(Role).ThenBy(Ordinal).ThenBy(ExtraKeyId)` 逐字一致；任何一处顺序变化都会
    // 改变 NodeId 指派，进而破坏确定性。
    // Kind / Role 是枚举：OrderBy 在枚举上按底层值比较，故这里显式转 int 以保持一致
    // （枚举的 CompareTo 走同一比较，但显式转换可避免装箱）。
    private sealed class StableAnchorComparer : IComparer<StableNodeAnchor>
    {
        internal static readonly StableAnchorComparer Instance = new();

        public int Compare(StableNodeAnchor x, StableNodeAnchor y)
        {
            var comparison = ((int)x.Kind).CompareTo((int)y.Kind);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = x.FilePathId.CompareTo(y.FilePathId);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = x.SpanStart.CompareTo(y.SpanStart);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = x.SpanEnd.CompareTo(y.SpanEnd);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = ((int)x.Role).CompareTo((int)y.Role);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = x.Ordinal.CompareTo(y.Ordinal);
            if (comparison != 0)
            {
                return comparison;
            }

            return x.ExtraKeyId.CompareTo(y.ExtraKeyId);
        }
    }
}
