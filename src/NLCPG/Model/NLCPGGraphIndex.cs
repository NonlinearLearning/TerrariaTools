using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using NLCPG.Contracts;

namespace NLCPG.Model;

/// 保存图冻结后可供只读查询使用的确定性边索引。
internal sealed class NLCPGGraphIndex
{
    // 以 CSR 形式保存某一端点方向的边序，并通过 canonical edge 序数间接取值。
    // ordinals 为 null 表示 bucket 内容在 canonical 序中本身连续（outgoing 方向），可直接切片。
    //
    // 边值不再常驻：读取时经 CanonicalEdgeStore 按序数现场投影（零堆分配）。
    // 原先此处持有 NLCPGEdge[]（72 B/边）与 OrdinalEdgeList 的同一份数组引用。
    private sealed class CsrEdgeTable
    {
        private readonly CanonicalEdgeStore _store;
        private readonly int[]? _ordinals;
        private readonly int[] _offsets;
        private readonly Dictionary<NodeId, int> _nodeOrdinals;
        private readonly bool _kindSorted;

        internal CsrEdgeTable(
            CanonicalEdgeStore store,
            int[]? ordinals,
            int[] offsets,
            Dictionary<NodeId, int> nodeOrdinals,
            bool kindSorted)
        {
            _store = store;
            _ordinals = ordinals;
            _offsets = offsets;
            _nodeOrdinals = nodeOrdinals;
            _kindSorted = kindSorted;
        }

        internal IReadOnlyList<NLCPGEdge> Get(NodeId nodeId)
        {
            return TryGetBucket(nodeId, out var offset, out var count)
                ? Slice(offset, count)
                : Array.Empty<NLCPGEdge>();
        }

        // 仅当 kindSorted 为 true 时可用：bucket 内的边已按 Kind 升序排列，二分即可定位子区间。
        internal IReadOnlyList<NLCPGEdge> Get(NodeId nodeId, NLCPGEdgeKind kind)
        {
            if (!_kindSorted || !TryGetBucket(nodeId, out var offset, out var count))
            {
                return Array.Empty<NLCPGEdge>();
            }

            var end = offset + count;
            var kindOrdinal = (int)kind;
            var start = LowerBound(offset, end, kindOrdinal);
            var stop = LowerBound(start, end, kindOrdinal + 1);
            return stop > start ? Slice(start, stop - start) : Array.Empty<NLCPGEdge>();
        }

        private int LowerBound(int from, int to, int kindOrdinal)
        {
            var low = from;
            var high = to;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if ((int)EdgeAt(middle).Kind < kindOrdinal)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private NLCPGEdge EdgeAt(int index)
        {
            return _ordinals is null ? _store.Project(index) : _store.Project(_ordinals[index]);
        }

        private IReadOnlyList<NLCPGEdge> Slice(int offset, int count)
        {
            return _ordinals is null
                ? new CanonicalEdgeList(_store, offset, count)
                : new OrdinalEdgeList(_store, _ordinals, offset, count);
        }

        private bool TryGetBucket(NodeId nodeId, out int offset, out int count)
        {
            offset = 0;
            count = 0;
            if (!_nodeOrdinals.TryGetValue(nodeId, out var nodeOrdinal) ||
                (uint)nodeOrdinal >= (uint)(_offsets.Length - 1))
            {
                return false;
            }

            offset = _offsets[nodeOrdinal];
            count = _offsets[nodeOrdinal + 1] - offset;
            return count > 0;
        }
    }

    // 边元数据三元组 (StructuredLabel, ContextId, CallSiteContext)，用作排序与去重键。
    // 【第 14 轮】由 private 改为 internal：CanonicalEdgeStore 原先自带一份完全同形的私有副本，
    // 现共用本类型，避免两处定义漂移。
    internal readonly record struct EdgeMetadataKey(
      NLCPGEdgeLabel? Label,
      NLCPGContextId? ContextId,
      NLCPGCallSiteContext? CallSiteContext);

    // 阶段 A：按引用身份比较元数据三元组。Equals 与 GetHashCode 都只看引用与整数，
    // 既满足哈希契约，又让每条边只需 3 次 O(1) 比较、零字符串工作。
    private sealed class EdgeMetadataIdentityComparer : IEqualityComparer<EdgeMetadataKey>
    {
        internal static readonly EdgeMetadataIdentityComparer Instance = new();

        public bool Equals(EdgeMetadataKey x, EdgeMetadataKey y)
        {
            if (!ReferenceEquals(x.Label, y.Label))
            {
                return false;
            }

            var xCallSite = x.CallSiteContext;
            var yCallSite = y.CallSiteContext;

            // 调用点存在时，ContextId 是调用点的纯函数（NLCPGEdge 构造期已强制校验
            // ContextId == CallSiteContext.ToContextId()），故比较 ContextId 引用不提供任何
            // 额外区分能力；而 ToContextId() 每条边都重新插值出一个新字符串，按引用比较会把
            // 本可共享的身份全部拆散（实测身份数 3,954,145 → 4,241）。故该分支只在调用点
            // 缺失、ContextId 成为唯一元数据来源时才比较它。
            if (!xCallSite.HasValue)
            {
                var xContext = x.ContextId?.Value;
                var yContext = y.ContextId?.Value;
                if (!ReferenceEquals(xContext, yContext))
                {
                    return false;
                }
            }

            if (xCallSite.HasValue != yCallSite.HasValue)
            {
                return false;
            }

            if (!xCallSite.HasValue)
            {
                return true;
            }

            var left = xCallSite.GetValueOrDefault();
            var right = yCallSite.GetValueOrDefault();
            return ReferenceEquals(left.FilePath, right.FilePath) &&
              left.SpanStart == right.SpanStart &&
              left.SpanEnd == right.SpanEnd &&
              ReferenceEquals(left.DisplayName, right.DisplayName);
        }

        public int GetHashCode(EdgeMetadataKey key)
        {
            var hash = new HashCode();
            hash.Add(RuntimeHelpers.GetHashCode(key.Label!));
            if (key.CallSiteContext.HasValue)
            {
                // 与 Equals 对应：调用点存在时不哈希 ContextId（它是调用点的纯函数）。
                var callSite = key.CallSiteContext.Value;
                hash.Add(1);
                hash.Add(RuntimeHelpers.GetHashCode(callSite.FilePath));
                hash.Add(callSite.SpanStart);
                hash.Add(callSite.SpanEnd);
                hash.Add(RuntimeHelpers.GetHashCode(callSite.DisplayName));
                return hash.ToHashCode();
            }

            var contextHash = key.ContextId.HasValue
              ? RuntimeHelpers.GetHashCode(key.ContextId.Value.Value)
              : 0;
            hash.Add(contextHash);
            return hash.ToHashCode();
        }
    }

    // 阶段 B：按值比较，值键与原文第 4..9 键完全相同。
    // 只作用于阶段 A 的候选集（数量 = 不同元数据实例数，远小于边数），
    // 故这里对 StableKey 求哈希的代价可忽略。StableKey 是计算属性，
    // 因此按标签实例记忆化，避免重复插值。
    private sealed class EdgeMetadataValueComparer : IEqualityComparer<EdgeMetadataKey>
    {
        private readonly Dictionary<NLCPGEdgeLabel, string> _stableKeys;

        internal EdgeMetadataValueComparer(Dictionary<NLCPGEdgeLabel, string> stableKeys)
        {
            _stableKeys = stableKeys;
        }

        public bool Equals(EdgeMetadataKey x, EdgeMetadataKey y)
        {
            if (!string.Equals(
                  StableKeyOf(x.Label, _stableKeys),
                  StableKeyOf(y.Label, _stableKeys),
                  StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(x.ContextId?.Value, y.ContextId?.Value, StringComparison.Ordinal))
            {
                return false;
            }

            var xCallSite = x.CallSiteContext;
            var yCallSite = y.CallSiteContext;
            if (xCallSite.HasValue != yCallSite.HasValue)
            {
                return false;
            }

            if (!xCallSite.HasValue)
            {
                return true;
            }

            var left = xCallSite.GetValueOrDefault();
            var right = yCallSite.GetValueOrDefault();
            return string.Equals(left.FilePath, right.FilePath, StringComparison.Ordinal) &&
                left.SpanStart == right.SpanStart &&
                left.SpanEnd == right.SpanEnd &&
                string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
        }

        public int GetHashCode(EdgeMetadataKey key)
        {
            var hash = new HashCode();
            hash.Add(StableKeyOf(key.Label, _stableKeys), StringComparer.Ordinal);
            hash.Add(key.ContextId?.Value, StringComparer.Ordinal);
            if (key.CallSiteContext.HasValue)
            {
                var callSite = key.CallSiteContext.Value;
                hash.Add(callSite.FilePath, StringComparer.Ordinal);
                hash.Add(callSite.SpanStart);
                hash.Add(callSite.SpanEnd);
                hash.Add(callSite.DisplayName, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }

    // 按引用身份比较的相等比较器：Equals 与 GetHashCode 都基于引用，满足哈希契约。
    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
      where T : class
    {
        internal static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? x, T? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }

    // 按 NodeId 升序比较节点，供"随节点数组一起排序的输入序下标"使用。
    //
    // 语义必须与原先的 `nodes.OrderBy(node => node.NodeId)` 完全一致（Comparer<NodeId?>.Default）：
    // null NodeId 排在任意非空之前。故这里用 Nullable.Compare 而不是直接读 NodeId.Value——
    // 后者会让"存在 null NodeId"的输入在排序阶段就抛错，而原实现是在随后的
    // nodeOrdinals.Add 处抛出同一个 InvalidOperationException，异常时机与出处不同。
    private sealed class NodeIdOrderComparer : IComparer<NLCPGNode>
    {
        internal static readonly NodeIdOrderComparer Instance = new();

        public int Compare(NLCPGNode x, NLCPGNode y)
        {
            return Nullable.Compare(x.NodeId, y.NodeId);
        }
    }

    // 单次 Create 范围内的计数排序 scratch。
    //
    // 四趟基数排序与两次双键分桶宽度各不相同，但都只需要"宽度 + 1 的前缀和缓冲"这一种形态，
    // 故共用一个按需扩容的数组。原实现每趟各分配一个 int[keyWidth + 1]：在 NPC.cs 规模下
    // nodeCount 级宽度（1,722,521）会被重复分配多次，而该缓冲用完即弃。
    //
    // 生命周期：只存在于 Create 的调用栈内，不进入任何字段、也不进全局池，故不会长期占住
    // nodeCount 级容量；同时因为只有一份，也不存在"每趟各持一份"的并发/峰值叠加。
    private sealed class CountingScratch
    {
        private int[] _positions = Array.Empty<int>();

        // 返回长度 >= width + 1 的数组，且 [0, width] 区间已清零。
        // 容量不足以容纳 width + 1 时才扩容（此时新数组本身即为零，无需再清）。
        internal int[] Rent(int width)
        {
            var required = width + 1;
            if (_positions.Length < required)
            {
                _positions = new int[required];
            }
            else
            {
                // 只清"本次活动区间"：容量可能来自上一趟更大的 keyWidth，
                // 脏尾部一旦被前缀和读到就会得出错误的桶边界。
                Array.Clear(_positions, 0, required);
            }

            return _positions;
        }
    }

    private NLCPGGraphIndex(
      NLCPGNode[] orderedNodes,
      int[] nodeInsertionOrder,
      CanonicalEdgeStore edgeStore,
      int[] insertionOrder,
      Dictionary<NodeId, int> nodeOrdinals,
      CsrEdgeTable outgoing,
      CsrEdgeTable incoming,
      CsrEdgeTable incomingByKind,
      int[] edgesByKindOrdinals,
      int[] edgesByKindOffsets,
      Dictionary<NLCPGNodeKind, int[]> nodesByKind,
      Dictionary<uint, int[]> nodesByFilePath,
      Func<string> snapshotVersionFactory)
    {
        OrderedNodes = orderedNodes;
        CanonicalNodes = orderedNodes;
        NodeInsertionOrder = nodeInsertionOrder;
        EdgeStore = edgeStore;
        InsertionOrder = insertionOrder;
        NodeOrdinals = nodeOrdinals;
        Outgoing = outgoing;
        Incoming = incoming;
        IncomingByKind = incomingByKind;
        EdgesByKindOrdinals = edgesByKindOrdinals;
        EdgesByKindOffsets = edgesByKindOffsets;
        NodesByKind = nodesByKind;
        NodesByFilePath = nodesByFilePath;
        // 只登记工厂，不在此处求值：求值被推迟到首次访问 SnapshotVersion。
        // 工厂闭包捕获的 orderedNodes/store 都是构造后不再改写的实例，
        // 故延迟求值与就地求值逐字节等价。
        _snapshotVersion = new Lazy<string>(snapshotVersionFactory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal IReadOnlyList<NLCPGNode> OrderedNodes { get; }

    private NLCPGNode[] CanonicalNodes { get; }

    // 输入序 -> canonical 序。输入序即 Create 传入的节点枚举序（也即冻结前 _nodesByOrdinal 的
    // 首次入图序 / CreateFrozen 调用方的传入序），故 graph.Nodes 的枚举序与语义完全不变。
    private int[] NodeInsertionOrder { get; }

    // 冻结后 graph.Nodes 的载体：与 OrderedNodes 共用同一节点数组，只多一份 4 B/节点的排列，
    // 从而取代原先那份常驻的 Dictionary<NodeId, NLCPGNode>（其 entry 内联 NLCPGNode 值）。
    internal IReadOnlyList<NLCPGNode> InputOrderedNodes =>
      _inputOrderedNodes ??= new OrdinalNodeList(CanonicalNodes, NodeInsertionOrder);

    private IReadOnlyList<NLCPGNode>? _inputOrderedNodes;

    internal CanonicalEdgeStore EdgeStore { get; }

    // 插入序 -> canonical 序。插入序即冻结时输入枚举序（等价于原先常驻 HashSet 的枚举序），
    // 故 graph.Edges 的枚举序与语义完全不变，但常驻成本由 80 B/槽降为 4 B/边。
    private int[] InsertionOrder { get; }

    // canonical 序的只读边视图。原先返回常驻的 NLCPGEdge[]（72 B/边），
    // 现在返回投影视图，投影出的边值与旧数组逐字段相同。
    internal IReadOnlyList<NLCPGEdge> OrderedEdges => new CanonicalEdgeList(EdgeStore, 0, EdgeStore.Count);

    // 插入序的只读边视图。替代原先常驻的 HashSet<NLCPGEdge>（80 B/槽），
    // 供 NLCPGGraph.Edges 使用；枚举序与旧 HashSet 的插入序一致。
    // 视图本身无状态（只读 store + 只读序数表），故缓存一份，避免每次访问都新建。
    internal IReadOnlyList<NLCPGEdge> InsertionOrderedEdges =>
      _insertionOrderedEdges ??= new OrdinalEdgeList(EdgeStore, InsertionOrder, 0, InsertionOrder.Length);

    private IReadOnlyList<NLCPGEdge>? _insertionOrderedEdges;

    private Dictionary<NodeId, int> NodeOrdinals { get; }

    private CsrEdgeTable Outgoing { get; }

    private CsrEdgeTable Incoming { get; }

    private CsrEdgeTable IncomingByKind { get; }

    private int[] EdgesByKindOrdinals { get; }

    private int[] EdgesByKindOffsets { get; }

    internal IReadOnlyDictionary<NLCPGNodeKind, int[]> NodesByKind { get; }

    internal IReadOnlyDictionary<uint, int[]> NodesByFilePath { get; }

    // 惰性记忆化：快照版本只依赖 OrderedNodes 与 EdgeStore，二者都是本索引的常驻不可变字段
    // （CanonicalEdgeStore 全字段 private readonly，冻结后不再改写），故首次访问时再算
    // 与在 Create 内算**结果必然相同**。
    //
    // 动机：并非所有调用方都读快照版本。项目级 JSON 导出对每个文件建图后只读 Nodes/Edges，
    // 从不访问 GraphSnapshotVersion；而该函数对每个节点写 8 个字段、每条边写 9 个字段，
    // 实测 NPC.cs 单文件即使经过缓冲化改造仍耗约 7.2 s CPU（见下方 SnapshotHasher 注释）。
    // 惰性化使「不读它的调用方」永不付出该成本，且不改变任何既有返回值。
    //
    // ExecutionAndPublication：首次访问可能发生在锁内，需保证多线程下只算一次且各线程
    // 看到同一个实例。选最保守的模式，不以「调用方单线程」为前提。
    private readonly Lazy<string> _snapshotVersion;

    internal string SnapshotVersion => _snapshotVersion.Value;

    // 按确定性顺序冻结节点和边，并生成连续数组查询索引；快照版本登记为惰性工厂（见 SnapshotVersion）。
    //
    // 【单份节点载荷】requirement：冻结后完整节点只存一份。本方法取得一份【独占】的
    // NLCPGNode[]（就地按 NodeId 排序），并在 Create 返回后由索引持有；调用方不得再
    // 使用或改写该数组。
    //
    // 两个生产调用方（NLCPGGraph.CreateFrozen / FreezeQueryIndex）传进来的都是**自己刚刚
    // 独占新建**的数组，唯一目的在于把所有权移交给索引。若这里再做一次 `nodes.ToArray()`，
    // 就会在冻结峰值上多出一份完整节点载荷副本（nodeCount × 104 B），并在拷贝后把调用方
    // 那份立刻变成垃圾 —— 这既抵消了"单份持有"的收益，也让峰值高于改前。
    // 故这里用 `as NLCPGNode[]` 直接接管数组实例（与下方 edgeArray 同样的写法）；
    // 只有调用方传的是非数组序列（例如测试或未来的惰性投影）时才复制一次。
    internal static NLCPGGraphIndex Create(IEnumerable<NLCPGNode> nodes, IEnumerable<NLCPGEdge> edges)
    {
        var edgeArray = edges as NLCPGEdge[] ?? edges.ToArray();

        // 输入只枚举一次。数组输入即接管原实例；非数组输入才由 ToArray 复制一份
        // 供本索引独占（此后就地排序，故绝不能别名调用方的数组）。
        var orderedNodes = nodes as NLCPGNode[] ?? nodes.ToArray();

        // inputOrdinals 先充当"输入序下标"（此时其值即数组当前位置），随后与 orderedNodes
        // 同步被置换；置换完成后 inputOrdinals[canonical] = 该 canonical 位置节点的输入下标。
        // 只额外需要一个 4 B/节点的临时 int[]，不复制节点值本身。
        var inputOrdinals = new int[orderedNodes.Length];
        for (var index = 0; index < inputOrdinals.Length; index += 1)
        {
            inputOrdinals[index] = index;
        }

        // 节点 canonical 化：NodeId 稠密时走就地环置换，否则回退比较排序。
        //
        // 稠密判据：NodeId 全部非空、且取值全部落在 [1, N]、最大值恰为 N。满足时"按 NodeId
        // 排序"就退化成一次置换，可原地完成、无需任何比较。确定性 NodeId 由
        // DeterministicNodeIdTable 按 index + 1 连续指派，生产图上恒稠密。
        //
        // ⚠ 该判据【不能】证明两两不同：N 个值都落在 [1, N] 并不能排除重复（如 N=3 的 {1,1,3}）。
        // 故唯一性不靠扫描判断，而由下面的交换预算兜底（见其说明）。
        //
        // 为什么必须就地：Create_TakesOwnershipOfAnArrayInputAndSortsItInPlace 断言
        // Assert.Same(传入数组, canonical) 且入参数组自身变为 NodeId 升序；
        // Create_WithArrayInput_DoesNotAllocateASecondNodePayloadOnTheFreezePath 断言本次
        // Create 的分配小于一份完整节点载荷（N × 104 B）。散射到新数组会同时打挂这两条。
        //
        // 为什么用交换预算而不是 seen[] 去重表：合法置换下每次交换都会把【当前位置的元素】
        // 永久落到最终位置，且目标下标必 > 当前位置（前序位置已固定），故交换次数上界为 N−1，
        // 预算 N 不会误触发。一旦出现重复 ID，环置换不可能收敛（数组永远无法变成 1..N），
        // 预算即截断它并回退 Array.Sort；这既保证终止，又零额外分配（seen[] 需要 N 个布尔量）。
        // 回退时两数组已被同步置换过，而 inputOrdinals 的不变式（记录当前位置元素的输入下标）
        // 仍然成立，故 Array.Sort 接着做完即正确。重复 ID 的报错时机也因此与改前一致：
        // 仍由随后的 nodeOrdinals.Add 抛出 ArgumentException，而不是在排序阶段。
        var denseNodeIds = false;
        if (orderedNodes.Length > 0)
        {
            var maxNodeId = 0u;
            var minNodeId = uint.MaxValue;
            var allPresent = true;
            for (var index = 0; index < orderedNodes.Length; index += 1)
            {
                var nodeId = orderedNodes[index].NodeId;
                if (nodeId is null)
                {
                    allPresent = false;
                    break;
                }

                var value = nodeId.Value.Value;
                if (value > maxNodeId)
                {
                    maxNodeId = value;
                }

                if (value < minNodeId)
                {
                    minNodeId = value;
                }
            }

            denseNodeIds = allPresent &&
              minNodeId >= 1 &&
              maxNodeId == (uint)orderedNodes.Length;
        }

        if (denseNodeIds)
        {
            var budget = orderedNodes.Length;
            var swaps = 0;
            for (var position = 0; position < orderedNodes.Length; position += 1)
            {
                while (orderedNodes[position].NodeId!.Value.Value != (uint)(position + 1))
                {
                    var target = (int)orderedNodes[position].NodeId!.Value.Value - 1;
                    (orderedNodes[position], orderedNodes[target]) =
                      (orderedNodes[target], orderedNodes[position]);
                    (inputOrdinals[position], inputOrdinals[target]) =
                      (inputOrdinals[target], inputOrdinals[position]);
                    swaps += 1;
                    if (swaps > budget)
                    {
                        // 出现重复 NodeId：环置换不收敛，判定非稠密并回退比较排序。
                        denseNodeIds = false;
                        break;
                    }
                }

                if (!denseNodeIds)
                {
                    break;
                }
            }

            if (!denseNodeIds)
            {
                Array.Sort(orderedNodes, inputOrdinals, NodeIdOrderComparer.Instance);
            }
        }
        else
        {
            Array.Sort(orderedNodes, inputOrdinals, NodeIdOrderComparer.Instance);
        }

        // graph.Nodes 的枚举序 = 输入序：nodeInsertionOrder[输入下标] = canonical 位置。
        // 该数组常驻（4 B/节点），是唯一新增的常驻结构，用于取代原先的节点字典。
        var nodeInsertionOrder = new int[orderedNodes.Length];
        for (var canonical = 0; canonical < nodeInsertionOrder.Length; canonical += 1)
        {
            nodeInsertionOrder[inputOrdinals[canonical]] = canonical;
        }

        var nodeOrdinals = new Dictionary<NodeId, int>(orderedNodes.Length);
        for (var ordinal = 0; ordinal < orderedNodes.Length; ordinal += 1)
        {
            nodeOrdinals.Add(orderedNodes[ordinal].NodeId!.Value, ordinal);
        }

        // 计数缓冲只在本次 Create 内复用（四趟基数排序 + 两次双键分桶 + 两次分桶），
        // 按当前最大需求扩容。原实现每趟各分配一份 int[keyWidth + 1]。
        var countingScratch = new CountingScratch();

        var kindWidth = Enum
            .GetValues<NLCPGEdgeKind>()
            .Select(kind => (int)kind)
            .DefaultIfEmpty(0)
            .Max() + 1;

        // 【本轮 C3】kind 键列已收窄为 byte[]，故 (byte)edge.Kind 的强制转换发生在【本方法内】。
        // 这里必须显式守卫，否则枚举超过 256 个取值时会在源头静默截断，而 CanonicalEdgeStore
        // 的同类守卫在更晚处才执行（届时截断已污染了计数排序的键序）。
        // 与 CanonicalEdgeStore.Create 的守卫保持同一措辞，任一处触发都给出同样的异常。
        if (kindWidth > byte.MaxValue + 1)
        {
            throw new InvalidOperationException(
              $"NLCPGEdgeKind has {kindWidth} distinct ordinals, which no longer fits a byte-wide canonical kind column.");
        }

        // 原文用 9 键 LINQ 排序。其中第 4..9 键完全由边元数据决定，而元数据实例数远小于边数，
        // 因此先把元数据按值去重并求秩，把排序降为 4 个整数键的稳定基数排序：
        // 字符串比较次数从 O(n·log n·6) 降到 0。
        //
        // 【本轮 C2】排序键与池下标列在有元数据时是同一个数组（见 BuildMetadataRanks 注释），
        // 故这里用 metadataRanks 承载排序键、metadataIds 承载池下标列，二者通常指向同一实例；
        // 全部边无元数据时 metadataIds 为 null（不分配池），而排序键仍是合法的全 0 数组。
        var metadataRanks = BuildMetadataRanks(
          edgeArray,
          out var metadataWidth,
          out var metadataIds,
          out var metadataPoolKeys);
        var sourceOrdinals = new int[edgeArray.Length];
        var targetOrdinals = new int[edgeArray.Length];
        var kindKeys = new byte[edgeArray.Length];
        for (var index = 0; index < edgeArray.Length; index += 1)
        {
            var edge = edgeArray[index];
            if (denseNodeIds)
            {
                sourceOrdinals[index] = ResolveDenseOrdinal(
                  edge.SourceNodeId, nodeOrdinals, orderedNodes.Length);
                targetOrdinals[index] = ResolveDenseOrdinal(
                  edge.TargetNodeId, nodeOrdinals, orderedNodes.Length);
            }
            else
            {
                sourceOrdinals[index] = nodeOrdinals[edge.SourceNodeId];
                targetOrdinals[index] = nodeOrdinals[edge.TargetNodeId];
            }

            kindKeys[index] = (byte)edge.Kind;
        }


        // LSD 基数排序（计数排序实现，稳定）：键的重要性顺序为
        // source > kind > target > metadataRank，故从最不重要的键开始逐趟排序。
        var current = new int[edgeArray.Length];
        var buffer = new int[edgeArray.Length];
        for (var index = 0; index < current.Length; index += 1)
        {
            current[index] = index;
        }

        CountingSortPass(current, buffer, metadataRanks, metadataWidth, countingScratch);
        (current, buffer) = (buffer, current);
        CountingSortPass(current, buffer, targetOrdinals, nodeOrdinals.Count, countingScratch);
        (current, buffer) = (buffer, current);

        // kind 键列已收窄为 byte[]（见 kindKeys 声明处），故走 byte 版计数排序。
        CountingSortPassByte(current, buffer, kindKeys, kindWidth, countingScratch);
        (current, buffer) = (buffer, current);
        CountingSortPass(current, buffer, sourceOrdinals, nodeOrdinals.Count, countingScratch);
        (current, buffer) = (buffer, current);


        // 插入序 -> canonical 序（即上面置换的逆）。原先常驻的 HashSet<NLCPGEdge> 按插入序枚举，
        // 该数组以 4 B/边复现同一枚举序，从而取代 80 B/槽的 HashSet 作为 graph.Edges 的载体。
        // 复用 buffer 这块已分配的 scratch（此后不再被读），避免再分配一个 int[edgeCount]。
        var insertionOrder = buffer;
        for (var canonicalIndex = 0; canonicalIndex < current.Length; canonicalIndex += 1)
        {
            insertionOrder[current[canonicalIndex]] = canonicalIndex;
        }

        // 按 canonical 序重排 target/kind 键，供后面的分桶复用，避免再对每条边做一次字典查找。
        // 只重排 target（int，4 B/边）与 kind（byte，1 B/边）两列，不再重排边值（原为 72 B/边）。
        var orderedTargetOrdinals = new int[edgeArray.Length];
        var orderedKindKeys = new byte[edgeArray.Length];
        for (var index = 0; index < current.Length; index += 1)
        {
            var sourceIndex = current[index];
            orderedTargetOrdinals[index] = targetOrdinals[sourceIndex];
            orderedKindKeys[index] = kindKeys[sourceIndex];
        }


        // 不再物化 orderedEdges（72 B/边）。current 即 canonical 置换，
        // 常驻的边表示改为 9~16 B/边的 SoA 存储，查询时按序数投影。
        var store = CanonicalEdgeStore.Create(
          orderedNodes,
          current,
          sourceOrdinals,
          targetOrdinals,
          kindKeys,
          kindWidth,
          metadataIds,
          metadataPoolKeys);

        // 快照版本改为惰性：这里只传工厂，真正计算推迟到首次访问 SnapshotVersion。
        // 与原先「在 Create 内立即计算」相比，返回值逐字节相同（见 SnapshotVersion 注释）。
        Func<string> snapshotVersionFactory = () => CreateSnapshotVersion(orderedNodes, store);


        // outgoing：canonical 序以 source 序数（即 SourceNodeId 的规范序）为首关键字，
        // 同一 source 的边天然连续，因此只需 offset 表，查询时直接切片。
        // 该直方图与 scatter 游标同源，一次统计即得。
        var outgoingOffsets = BuildOffsets(sourceOrdinals, nodeOrdinals.Count, countingScratch);
        var outgoing = new CsrEdgeTable(
            store,
            ordinals: null,
            outgoingOffsets,
            nodeOrdinals,
            kindSorted: true);

        // incoming：需要在 target 维度重排，只保存 ordinal 置换而非边值副本。
        // offsets 与排列来自同一次 target 直方图：orderedTargetOrdinals 只是 targetOrdinals
        // 的 canonical 置换，二者是同一 multiset，故直方图相同。
        var incomingBuild = BuildOrdinalsWithOffsets(
          orderedTargetOrdinals,
          nodeOrdinals.Count,
          countingScratch);
        // incomingByKind 的 offset 表与 incoming 完全相同（同一 target 直方图），
        // 故共用同一个数组实例（两者构造后均只读），省掉一份 1.7M 级元素表。
        var incomingOffsets = incomingBuild.Offsets;
        var incoming = new CsrEdgeTable(
            store,
            incomingBuild.Ordinals,
            incomingOffsets,
            nodeOrdinals,
            kindSorted: false);

        // incomingByKind：同一 target 内再按 Kind 稳定排序，bucket 内保持 canonical 相对顺序。
        // 双键 helper 顺带交还第一趟的 kind-only 排列，供 edgesByKind 直接使用，
        // 于是 edgesByKind 不需要再对 orderedKindKeys 做一次独立分桶。
        var twoKeyBuild = BuildOrdinalsByTwoKeys(
            orderedTargetOrdinals,
            orderedKindKeys,
            nodeOrdinals.Count,
            kindWidth,
            countingScratch);
        var incomingByKind = new CsrEdgeTable(
            store,
            twoKeyBuild.PrimaryThenSecondary,
            incomingOffsets,
            nodeOrdinals,
            kindSorted: true);

        // edgesByKind 复用第一趟的 kind-only 排列与同一趟得到的 kind offsets。
        // 语义不变式：必须是按 kind 升序、桶内保持 canonical 相对顺序的排列；
        // twoKeyBuild.SecondaryOnly 正是 identity 按 kind 的稳定分桶。
        var edgesByKindOrdinals = twoKeyBuild.SecondaryOnly;
        var edgesByKindOffsets = twoKeyBuild.SecondaryOffsets;
        var nodesByKind = BuildNodesByKind(orderedNodes);
        var nodesByFilePath = BuildNodesByFilePath(orderedNodes);
        return new NLCPGGraphIndex(
            orderedNodes,
            nodeInsertionOrder,
            store,
            insertionOrder,
            nodeOrdinals,
            outgoing,
            incoming,
            incomingByKind,
            edgesByKindOrdinals,
            edgesByKindOffsets,
            nodesByKind,
            nodesByFilePath,
            snapshotVersionFactory);
    }

    // 稠密 NodeId 下的端点 -> canonical 序数解析：canonical 位置 = NodeId − 1，零哈希。
    //
    // 异常语义必须与改前的 nodeOrdinals[edge.SourceNodeId] 一致。NLCPGEdge.SourceNodeId /
    // TargetNodeId 是非空 NodeId，故不存在 null 键分支。稠密判据已保证 1..N 恰好各出现一次
    // （N 个两两不同的 NodeId 全落在 [1,N] 且最大值 = N），因此落在 [1,N] 内的 ID 必定存在，
    // 可直接相减；落在域外的 ID 才是"未知 ID"，此时交回字典抛出与改前完全相同的
    // KeyNotFoundException，而不是让数组越界给出 IndexOutOfRangeException。
    private static int ResolveDenseOrdinal(
        NodeId nodeId,
        Dictionary<NodeId, int> nodeOrdinals,
        int nodeCount)
    {
        var value = nodeId.Value;
        if (value >= 1 && value <= (uint)nodeCount)
        {
            return (int)value - 1;
        }

        // 域外 ID：与改前一样由字典抛出 KeyNotFoundException。
        return nodeOrdinals[nodeId];
    }

    // 按 NodeId 读取 canonical 节点；未知 ID 返回 false（供 NLCPGGraph.GetNode 保留 null 语义）。
    // 直接走数组（CanonicalNodes）而非 OrderedNodes 接口索引器：GetNode 是查询热点，
    // 少一次接口分派。
    internal bool TryGetNode(NodeId nodeId, out NLCPGNode node)
    {
        if (NodeOrdinals.TryGetValue(nodeId, out var ordinal))
        {
            node = CanonicalNodes[ordinal];
            return true;
        }

        node = default;
        return false;
    }

    // 按 NodeId 读取已知节点。未知 ID 由字典下标抛出 KeyNotFoundException，
    // 与原 `_nodesByNodeId[nodeId]` 的异常类型与出处一致。
    internal NLCPGNode GetKnownNode(NodeId nodeId)
    {
        return CanonicalNodes[NodeOrdinals[nodeId]];
    }

    internal IReadOnlyList<NLCPGEdge> GetOutgoingEdges(NodeId nodeId)
    {
        return Outgoing.Get(nodeId);
    }

    internal IReadOnlyList<NLCPGEdge> GetIncomingEdges(NodeId nodeId)
    {
        return Incoming.Get(nodeId);
    }

    internal IReadOnlyList<NLCPGEdge> GetOutgoingEdges(NodeId nodeId, NLCPGEdgeKind kind)
    {
        return Outgoing.Get(nodeId, kind);
    }

    internal IReadOnlyList<NLCPGEdge> GetIncomingEdges(NodeId nodeId, NLCPGEdgeKind kind)
    {
        return IncomingByKind.Get(nodeId, kind);
    }

    internal IReadOnlyList<NLCPGEdge> GetEdges(NLCPGEdgeKind kind)
    {
        var kindOrdinal = (int)kind;
        if ((uint)kindOrdinal >= (uint)(EdgesByKindOffsets.Length - 1))
        {
            return Array.Empty<NLCPGEdge>();
        }

        var offset = EdgesByKindOffsets[kindOrdinal];
        var count = EdgesByKindOffsets[kindOrdinal + 1] - offset;
        return count == 0
            ? Array.Empty<NLCPGEdge>()
            : new OrdinalEdgeList(EdgeStore, EdgesByKindOrdinals, offset, count);
    }

    internal IReadOnlyList<NLCPGNode> GetNodesByKind(NLCPGNodeKind kind)
    {
        return NodesByKind.TryGetValue(kind, out var ordinals)
            ? new OrdinalNodeList(CanonicalNodes, ordinals)
            : Array.Empty<NLCPGNode>();
    }

    internal bool TryGetNodesByFilePath(uint filePathId, out IReadOnlyList<NLCPGNode> nodes)
    {
        if (NodesByFilePath.TryGetValue(filePathId, out var ordinals))
        {
            nodes = new OrdinalNodeList(CanonicalNodes, ordinals);
            return true;
        }

        nodes = Array.Empty<NLCPGNode>();
        return false;
    }

    // 把每条边的第 4..9 个排序键（元数据）归并为单个整数秩。
    // 先按引用身份粗去重（每条边 O(1) 比较，零字符串工作），再对候选集按值精去重；
    // 只有候选集（数量 = 不同元数据实例数）才需要字符串比较与哈希。
    // 值去重使用与原文第 4..9 键完全相同的 6 个值键，故秩相等的边在原文中也完全并列，
    // 而这些边会由基数排序的稳定性保持输入顺序。
    //
    // 【第 14 轮】额外输出"每条边的值类下标"与"每个值类的代表键"：
    // CanonicalEdgeStore 原先把同一套值去重【又做了一遍】（实测 7,756,984 次字典探测、
    // 其中 7,752,743 次命中 = 99.95% 冗余）。本方法已经算出同一个划分（实测值数 4,241，
    // 与存储侧独立得到的池大小 4,241 完全一致），故改为把结果传下去。
    //
    // 等价性：值类由 EdgeMetadataValueComparer 按【值】判定（StableKey / ContextId.Value /
    // 调用点三字段的 ordinal 比较），同类内任意代表投影出的字段值都相同，故 Project() 逐字段不变。
    // 池的下标编号方式改变不影响结果——池只被 canonicalMetadataIds 间接索引，对外不可见。
    //
    // 【本轮 C2】两处合并，收益落在"元数据覆盖率远小于 1"这一实测事实上（真实语料 71.0% 的边
    // 三元组全 null、无元数据）：
    //
    //   1) 跳过探测：三元组全 null 的边直接得到 metadataId = 0，不进入引用身份字典。
    //      原先每条边都要做一次 Dictionary<EdgeMetadataKey,int> 探测，其中 71% 是为"已知答案"付费。
    //   2) 秩即池下标：候选值类按 CompareMetadata 排序后【重新编号】，使
    //      "该边的排序键（秩）"与"该边在池中的下标"合为同一个数。原先 ranks 与
    //      valueClassOfEdge 是两份 int[edgeCount]，而 ranks 只是 valueClassOfEdge 经 rankOfValue
    //      的置换 ⇒ 现在塌缩为一份 metadataIdOfEdge，省一份 4 B/边。
    //
    // 返回值同时充当基数排序的元数据键与池下标，故 metadataIdOfEdge[edge] 的语义是：
    //   0                        => 无元数据（投影为三个 null）
    //   r ∈ [1, pool.Length]     => 池下标 r - 1 处的那条代表键
    // 这与改前"valueClass + 1"的编号【逐值相同】：改前的 0 号值类恰是全 null 类，其秩必为 0
    // （CompareMetadata 把 null StableKey 排在任意非 null 之前），故改前非 null 类的秩
    // 就是 1..K；本实现跳过全 null 边后候选表为 K 个非 null 类，按序编号 +1 亦为 1..K。
    // 因为排序键逐值相同，基数排序的稳定性保证 canonical 边序也逐条不变。
    // 返回值与 out metadataIds 的关系（这正是 C2 省下一份 4 B/边的地方）：
    //   - 有元数据时：二者是【同一个数组实例】——排序键就是池下标 + 1，故只需一份 int[edgeCount]，
    //     而改前是 ranks 与 valueClassOfEdge 两份。
    //   - 全部边无元数据时：返回值是"全 0 的排序键"（宽度 1），而 metadataIds 为 null，
    //     与改前一致地让 CanonicalEdgeStore 完全不分配元数据池。
    // 故调用方必须分别接收：前者作基数排序的键，后者作池下标列。
    private static int[] BuildMetadataRanks(
      NLCPGEdge[] edges,
      out int metadataWidth,
      out int[]? metadataIds,
      out EdgeMetadataKey[]? metadataPoolKeys)
    {
        // 快速路径：全部边在第 4..9 键上完全并列，秩必然相同，
        // 可直接跳过下面 20M 级的引用身份字典查找。该判断只在首次遇到非空元数据时跳出，
        // 故元数据存在时开销可忽略。
        //
        // 注意：早期注释声称"真实单文件语料实测元数据覆盖为 0"（引 Build\verify-algo-speed\
        // IMPLEMENTATION-EVIDENCE.md §3），该结论【已被推翻】——实测元数据边数 3,954,144，
        // 故本快速路径在真实语料上【不会】生效。
        var hasMetadata = false;
        for (var index = 0; index < edges.Length; index += 1)
        {
            var edge = edges[index];
            if (edge.StructuredLabel is not null ||
                edge.ContextId is not null ||
                edge.CallSiteContext is not null)
            {
                hasMetadata = true;
                break;
            }
        }

        if (!hasMetadata)
        {
            metadataWidth = 1;
            metadataIds = null;
            metadataPoolKeys = null;
            return new int[edges.Length];
        }

        var stableKeys = new Dictionary<NLCPGEdgeLabel, string>(
          ReferenceComparer<NLCPGEdgeLabel>.Instance);

        // metadataIdOfEdge 先临时承载"引用身份 + 1"（0 保留给无元数据），
        // 待值类秩求出后就地改写为最终 metadataId。这样不需要第二个 int[edgeCount]，
        // 也不需要在边维度上再做一次"身份 -> 值类"的中间列。
        var metadataIdOfEdge = new int[edges.Length];
        var identityIds = new Dictionary<EdgeMetadataKey, int>(
          EdgeMetadataIdentityComparer.Instance);
        var identityKeys = new List<EdgeMetadataKey>();
        for (var index = 0; index < edges.Length; index += 1)
        {
            var edge = edges[index];
            if (edge.StructuredLabel is null &&
                edge.ContextId is null &&
                edge.CallSiteContext is null)
            {
                // 无元数据：排序键即 0，且无需进入身份探测。
                continue;
            }

            var key = new EdgeMetadataKey(
              edge.StructuredLabel,
              edge.ContextId,
              edge.CallSiteContext);
            if (!identityIds.TryGetValue(key, out var identity))
            {
                identity = identityKeys.Count;
                identityIds.Add(key, identity);
                identityKeys.Add(key);
            }

            metadataIdOfEdge[index] = identity + 1;
        }

        var valueIds = new Dictionary<EdgeMetadataKey, int>(
          new EdgeMetadataValueComparer(stableKeys));
        var valueKeys = new List<EdgeMetadataKey>();
        var valueOfIdentity = new int[identityKeys.Count];
        for (var identity = 0; identity < identityKeys.Count; identity += 1)
        {
            var key = identityKeys[identity];
            if (!valueIds.TryGetValue(key, out var value))
            {
                value = valueKeys.Count;
                valueIds.Add(key, value);
                valueKeys.Add(key);
            }

            valueOfIdentity[identity] = value;
        }

        var valueStableKeys = new string?[valueKeys.Count];
        for (var index = 0; index < valueKeys.Count; index += 1)
        {
            valueStableKeys[index] = StableKeyOf(valueKeys[index].Label, stableKeys);
        }

        var order = new int[valueKeys.Count];
        for (var index = 0; index < order.Length; index += 1)
        {
            order[index] = index;
        }

        Array.Sort(order, (left, right) =>
        {
            var comparison = CompareMetadata(
              valueKeys[left],
              valueStableKeys[left],
              valueKeys[right],
              valueStableKeys[right]);
            // 值 id 与第 4..9 键一一对应，故 comparison 为 0 时 left 与 right 必相等；
            // 仍按下标补齐，使比较器始终是严格全序（Array.Sort 不稳定，需要全序才确定）。
            return comparison != 0 ? comparison : left.CompareTo(right);
        });

        // 秩 = 排序位置 + 1：即"重编号"，令排序键与池下标成为同一个数。
        // 池按秩落位，故 pool[metadataId - 1] 直接取到该秩的代表键。
        var rankPlusOneOfValue = new int[valueKeys.Count];
        metadataPoolKeys = new EdgeMetadataKey[valueKeys.Count];
        for (var position = 0; position < order.Length; position += 1)
        {
            var value = order[position];
            rankPlusOneOfValue[value] = position + 1;
            metadataPoolKeys[position] = valueKeys[value];
        }

        for (var index = 0; index < edges.Length; index += 1)
        {
            var identityPlusOne = metadataIdOfEdge[index];
            if (identityPlusOne == 0)
            {
                continue;
            }

            metadataIdOfEdge[index] =
              rankPlusOneOfValue[valueOfIdentity[identityPlusOne - 1]];
        }

        // 键域 = [0, K]，K 为非 null 值类数 ⇒ 宽度 K + 1。
        metadataWidth = valueKeys.Count + 1;
        // 有元数据时排序键与池下标列是同一个数组：这是 C2 省下的那份 4 B/边。
        metadataIds = metadataIdOfEdge;
        return metadataIdOfEdge;
    }

    // 按原文第 4..9 键的顺序比较两个去重后的元数据。null 排在任意非 null 之前，
    // 与 OrderBy(key, StringComparer.Ordinal) 及 Comparer<int?>.Default 的语义一致。
    private static int CompareMetadata(
        EdgeMetadataKey x,
        string? xStableKey,
        EdgeMetadataKey y,
        string? yStableKey)
    {
        var comparison = string.CompareOrdinal(xStableKey, yStableKey);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(x.ContextId?.Value, y.ContextId?.Value);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(x.CallSiteContext?.FilePath, y.CallSiteContext?.FilePath);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Nullable.Compare(x.CallSiteContext?.SpanStart, y.CallSiteContext?.SpanStart);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Nullable.Compare(x.CallSiteContext?.SpanEnd, y.CallSiteContext?.SpanEnd);
        return comparison != 0
          ? comparison
          : string.CompareOrdinal(x.CallSiteContext?.DisplayName, y.CallSiteContext?.DisplayName);
    }

    // StableKey 是计算属性，每次访问都会重新插值；NLCPGEdgeLabel 不可变，
    // 故按实例记忆化，把插值次数从"每边一次"降到"每实例一次"。
    private static string? StableKeyOf(
        NLCPGEdgeLabel? label,
        Dictionary<NLCPGEdgeLabel, string> stableKeys)
    {
        if (label is null)
        {
            return null;
        }

        if (stableKeys.TryGetValue(label, out var stableKey))
        {
            return stableKey;
        }

        stableKey = label.StableKey;
        stableKeys.Add(label, stableKey);
        return stableKey;
    }

    // 单趟稳定计数排序：按 keyOf 把 source 中的置换写入 destination，
    // 要求 keyOf 的取值落在 [0, keyWidth) 内。
    //
    // positions 由调用方提供的 scratch 借出，只使用 [0, keyWidth] 这段活动区间；
    // scratch 的容量可能大于本次活动区间（来自上一趟更大的 keyWidth），
    // 故前缀和必须显式限定长度，绝不能按数组长度扫到脏尾部。
    private static void CountingSortPass(
        int[] source,
        int[] destination,
        int[] keyOf,
        int keyWidth,
        CountingScratch scratch)
    {
        CountingSortPass(source, destination, keyOf, keyWidth, scratch, offsetsOut: null);
    }

    // byte 键列版的单趟稳定计数排序。
    //
    // 【本轮 C3】单独起一个方法名，而不是给 CountingSortPass 再加一个 5 参重载：
    // FrozenNodeStorageEquivalenceTests.FindCountingSortPass 用
    // GetMethods(...).SingleOrDefault(名 == "CountingSortPass" && 形参个数 == 5) 定位既有重载，
    // 并断言 parameters[2] 是 int[]。新增同名 5 参方法会让 SingleOrDefault 因多匹配抛异常，
    // 从而打挂该测试。
    //
    // 语义与 CountingSortPass 逐行相同，只是 keyOf 的元素宽度为 1 B（kind 列）。
    // offsetsOut 非空时同样在 scatter 之前把桶边界复制出去。
    private static void CountingSortPassByte(
        int[] source,
        int[] destination,
        byte[] keyOf,
        int keyWidth,
        CountingScratch scratch,
        int[]? offsetsOut)
    {
        var positions = scratch.Rent(keyWidth);
        for (var index = 0; index < source.Length; index += 1)
        {
            positions[keyOf[source[index]] + 1] += 1;
        }

        PrefixSum(positions, keyWidth + 1);
        if (offsetsOut is not null)
        {
            // 只复制活动区间：scratch 的脏尾部不得进入只读 offsets。
            Array.Copy(positions, offsetsOut, keyWidth + 1);
        }

        for (var index = 0; index < source.Length; index += 1)
        {
            var value = source[index];
            var key = keyOf[value];
            destination[positions[key]] = value;
            positions[key] += 1;
        }
    }

    private static void CountingSortPassByte(
        int[] source,
        int[] destination,
        byte[] keyOf,
        int keyWidth,
        CountingScratch scratch)
    {
        CountingSortPassByte(source, destination, keyOf, keyWidth, scratch, offsetsOut: null);
    }

    // offsetsOut 非空时，额外把"前缀和之后、scatter 之前"的计数表复制进去
    // （调用方保证其长度 >= keyWidth + 1）。复制点必须在 scatter 之前：
    // scratch 上那一份随后会被 scatter 就地递增成游标，拷贝出来的才是桶边界。
    private static void CountingSortPass(
        int[] source,
        int[] destination,
        int[] keyOf,
        int keyWidth,
        CountingScratch scratch,
        int[]? offsetsOut)
    {
        var positions = scratch.Rent(keyWidth);
        for (var index = 0; index < source.Length; index += 1)
        {
            positions[keyOf[source[index]] + 1] += 1;
        }

        PrefixSum(positions, keyWidth + 1);
        if (offsetsOut is not null)
        {
            // 只复制活动区间：scratch 的脏尾部不得进入只读 offsets。
            Array.Copy(positions, offsetsOut, keyWidth + 1);
        }

        for (var index = 0; index < source.Length; index += 1)
        {
            var value = source[index];
            var key = keyOf[value];
            destination[positions[key]] = value;
            positions[key] += 1;
        }
    }

    // 一次直方图同时给出【只读 offsets】与【稳定分桶排列】。
    //
    // 原实现把同一件事做了两遍：BuildOrdinals 统计 keys 得 scatter cursor，
    // BuildKeyOffsets 再把同一个 multiset 统计一遍得 offsets。二者只是同一 key 数组的
    // 直方图（计数与顺序无关），故合并为一次统计：Offsets 是前缀和结果的副本，构造后只读
    // 并交给 CsrEdgeTable；cursor 是 scratch 上那份被 scatter 就地消耗的计数。
    //
    // 不变式：Offsets 绝不与 cursor 别名——否则 scatter 会把只读 offsets 写成游标。
    private readonly record struct OrdinalBuildResult(int[] Ordinals, int[] Offsets);

    // 唯一的直方图入口：在 scratch 上做计数 + 前缀和，返回的数组即 scatter 可用的 cursor。    // 调用方若还需要只读 offsets，必须经 CopyOffsets 另存一份，不得直接持有本数组。
    private static int[] BuildCountsCursor(int[] keys, int keyWidth, CountingScratch scratch)
    {
        var cursor = scratch.Rent(keyWidth);
        for (var index = 0; index < keys.Length; index += 1)
        {
            cursor[keys[index] + 1] += 1;
        }

        PrefixSum(cursor, keyWidth + 1);
        return cursor;
    }

    // 把 cursor 的活动区间另存为常驻只读 offsets。
    private static int[] CopyOffsets(int[] cursor, int keyWidth)
    {
        var offsets = new int[keyWidth + 1];
        Array.Copy(cursor, offsets, keyWidth + 1);
        return offsets;
    }

    // 只要 offsets、不要排列时的入口（outgoing 方向在 canonical 序中天然按 source 连续，
    // 故只需桶边界）。仍然走同一个直方图，不做第二次统计。
    private static int[] BuildOffsets(int[] keys, int keyWidth, CountingScratch scratch)
    {
        return CopyOffsets(BuildCountsCursor(keys, keyWidth, scratch), keyWidth);
    }

    // 等价于 Enumerable.Range(0, n).OrderBy(keySelector) 的稳定分桶：
    // 同一 bucket 内保持 canonical edge 顺序，与逐条追加构建 CSR 的结果一致。
    // offsets 与排列由同一次直方图同时得出。
    private static OrdinalBuildResult BuildOrdinalsWithOffsets(
        int[] keys,
        int keyWidth,
        CountingScratch scratch)
    {
        var cursor = BuildCountsCursor(keys, keyWidth, scratch);

        // offsets 是常驻结果（进入 CsrEdgeTable 后只读），必须独占存储。
        var offsets = CopyOffsets(cursor, keyWidth);

        var ordinals = new int[keys.Length];
        for (var index = 0; index < keys.Length; index += 1)
        {
            var key = keys[index];
            ordinals[cursor[key]] = index;
            cursor[key] += 1;
        }

        return new OrdinalBuildResult(ordinals, offsets);
    }

    // 双键稳定分桶的结果载体。
    //
    // SecondaryOnly 是【第一趟】按 secondary 稳定分桶的排列，即 edgesByKind 需要的
    // kind-only 排列；PrimaryThenSecondary 是第二趟结果，即 incomingByKind 需要的排列。
    // 二者不能互相冒充：PrimaryThenSecondary 是 target→kind 的最终排列，桶内并不是
    // kind 升序，把它当成 kind-only 排列会让 edgesByKind 语义退化。
    private readonly record struct TwoKeyOrdinalBuildResult(
        int[] PrimaryThenSecondary,
        int[] SecondaryOnly,
        int[] SecondaryOffsets);

    // 等价于 Enumerable.Range(0, n).OrderBy(primary).ThenBy(secondary) 的稳定分桶，
    // 同时把第一趟的 kind-only 排列与 secondary 直方图 offsets 一并交还调用方。
    //
    // 内存：两趟各用一个 int[n] 置换缓冲（与原先完全相同）。第二趟只【读】第一趟结果
    // 所在的缓冲、写入另一个缓冲，故第一趟结果天然可复用，不需要第三块数组。
    // 常驻总量与原实现相同：原先也常驻 incomingOrdinals + incomingByKindOrdinals +
    // edgesByKindOrdinals 三份 int[n]；现在同样三份，只是其中两份恰好就是这两块缓冲。
    // secondaryKeys 是 kind 列，已随 C3 收窄为 byte[]；primaryKeys 仍是 target 序数列（int）。
    private static TwoKeyOrdinalBuildResult BuildOrdinalsByTwoKeys(
        int[] primaryKeys,
        byte[] secondaryKeys,
        int primaryWidth,
        int secondaryWidth,
        CountingScratch scratch)
    {
        var current = new int[primaryKeys.Length];
        var buffer = new int[primaryKeys.Length];
        for (var index = 0; index < current.Length; index += 1)
        {
            current[index] = index;
        }

        // 第一趟：identity --secondary--> buffer（kind-only 稳定排列），并顺带取 secondary offsets。
        var secondaryOffsets = new int[secondaryWidth + 1];
        CountingSortPassByte(
          current, buffer, secondaryKeys, secondaryWidth, scratch, secondaryOffsets);

        // 第二趟：把 kind-only 排列作为输入按 primary 稳定分桶写回 current。
        // buffer 只被读取，故它仍然完整保留第一趟结果。
        CountingSortPass(buffer, current, primaryKeys, primaryWidth, scratch, offsetsOut: null);

        return new TwoKeyOrdinalBuildResult(current, buffer, secondaryOffsets);
    }

    // 按 kind 分桶。orderedNodes 已按 NodeId 升序，故同一 kind 内的 ordinal 天然升序，
    // 与原文 GroupBy(...).Select(OrderBy(NodeId)) 的结果一致，但省去一次排序与分组分配。
    private static Dictionary<NLCPGNodeKind, int[]> BuildNodesByKind(NLCPGNode[] orderedNodes)
    {
        var kindWidth = 0;
        foreach (var node in orderedNodes)
        {
            var width = (int)node.Kind + 1;
            if (width > kindWidth)
            {
                kindWidth = width;
            }
        }

        var positions = new int[kindWidth + 1];
        foreach (var node in orderedNodes)
        {
            positions[(int)node.Kind + 1] += 1;
        }

        PrefixSum(positions, positions.Length);
        var cursor = new int[kindWidth];
        Array.Copy(positions, cursor, kindWidth);
        var flat = new int[orderedNodes.Length];
        for (var ordinal = 0; ordinal < orderedNodes.Length; ordinal += 1)
        {
            flat[cursor[(int)orderedNodes[ordinal].Kind]++] = ordinal;
        }

        var result = new Dictionary<NLCPGNodeKind, int[]>();
        for (var kind = 0; kind < kindWidth; kind += 1)
        {
            var count = positions[kind + 1] - positions[kind];
            if (count == 0)
            {
                continue;
            }

            var bucket = new int[count];
            Array.Copy(flat, positions[kind], bucket, 0, count);
            result[(NLCPGNodeKind)kind] = bucket;
        }

        return result;
    }

    // 按 FilePathId 分桶后在桶内按 (SpanStart, SpanEnd, NodeId) 排序。
    // orderedNodes 按 NodeId 升序，故 ordinal 升序等价于 NodeId 升序；
    // 比较器给出全序（ordinal 唯一），因此 List.Sort 的不稳定性不影响结果。
    private static Dictionary<uint, int[]> BuildNodesByFilePath(NLCPGNode[] orderedNodes)
    {
        var buckets = new Dictionary<uint, List<int>>();
        for (var ordinal = 0; ordinal < orderedNodes.Length; ordinal += 1)
        {
            var node = orderedNodes[ordinal];
            if (node.FilePathId == 0 || !node.SpanStart.HasValue || !node.SpanEnd.HasValue)
            {
                continue;
            }

            if (!buckets.TryGetValue(node.FilePathId, out var bucket))
            {
                bucket = new List<int>();
                buckets.Add(node.FilePathId, bucket);
            }

            bucket.Add(ordinal);
        }

        var result = new Dictionary<uint, int[]>(buckets.Count);
        foreach (var pair in buckets)
        {
            var ordinals = pair.Value;
            ordinals.Sort((left, right) =>
            {
                var comparison = orderedNodes[left]
                  .SpanStart!.Value
                  .CompareTo(orderedNodes[right].SpanStart!.Value);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = orderedNodes[left]
                  .SpanEnd!.Value
                  .CompareTo(orderedNodes[right].SpanEnd!.Value);
                return comparison != 0 ? comparison : left.CompareTo(right);
            });
            result[pair.Key] = ordinals.ToArray();
        }

        return result;
    }

    // 前缀和，只处理前 length 个槽位；length 必须显式传入，因为调用方的缓冲容量
    // 可能大于本次活动区间（共享 scratch 的脏尾部绝不能参与累加）。
    private static void PrefixSum(int[] offsets, int length)
    {
        for (var index = 1; index < length; index += 1)
        {
            offsets[index] += offsets[index - 1];
        }
    }

    private static string CreateSnapshotVersion(IReadOnlyList<NLCPGNode> orderedNodes, CanonicalEdgeStore edgeStore)
    {
        using var writer = new SnapshotHasher();
        writer.WriteInt32(orderedNodes.Count);
        foreach (var node in orderedNodes)
        {
            writer.WriteUInt32(node.NodeId?.Value ?? 0);
            writer.WriteInt32((int)node.Kind);
            writer.WriteUInt32(node.StableAnchor?.FilePathId ?? 0);
            writer.WriteInt32(node.StableAnchor?.SpanStart ?? -1);
            writer.WriteInt32(node.StableAnchor?.SpanEnd ?? -1);
            writer.WriteInt32((int)(node.StableAnchor?.Role ?? StableNodeRole.None));
            writer.WriteInt32(node.StableAnchor?.Ordinal ?? 0);
            writer.WriteUInt32(node.StableAnchor?.ExtraKeyId ?? 0);
        }

        // StableKey 是计算属性，每次访问都重新插值一个【新字符串】。边段的元数据池只有 4,241 条，
        // 而边有 7,756,984 条，故不记忆化就会做数百万次插值，且每次都产生新实例，
        // 使下游按引用缓存的 UTF-8 字节全部失效。按标签实例记忆化后插值次数降到池规模。
        var stableKeys = new Dictionary<NLCPGEdgeLabel, string>(
          ReferenceComparer<NLCPGEdgeLabel>.Instance);

        writer.WriteInt32(edgeStore.Count);
        for (var index = 0; index < edgeStore.Count; index += 1)
        {
            var edge = edgeStore.Project(index);
            writer.WriteUInt32(edge.SourceNodeId.Value);
            writer.WriteInt32((int)edge.Kind);
            writer.WriteUInt32(edge.TargetNodeId.Value);
            writer.WriteString(StableKeyOf(edge.StructuredLabel, stableKeys));
            writer.WriteString(edge.ContextId?.Value);
            writer.WriteString(edge.CallSiteContext?.FilePath);
            writer.WriteInt32(edge.CallSiteContext?.SpanStart ?? -1);
            writer.WriteInt32(edge.CallSiteContext?.SpanEnd ?? -1);
            writer.WriteString(edge.CallSiteContext?.DisplayName);
        }

        return writer.Finish();
    }

    // 快照指纹的缓冲写入器：把"每字段一次 AppendData"合并为"攒满缓冲再追加一次"。
    //
    // 等价性论证：SHA-256 是流式哈希，SHA256(x ∥ y) == SHA256(x) 后接 SHA256(y)。
    // 本类只改变"追加到哈希的切分方式"，写入 hash 的**字节序列与顺序逐一不变**，
    // 故 GraphSnapshotVersion 逐字节相同。
    //
    // 动机：原实现对每个字段值各做一次 stackalloc + AppendData。NPC.cs 实测
    // 113,636,842 次调用、每次均摊 108 ns；而 SHA-256 对 1,481.5 MiB 本身的耗时
    // 仅约 1.3 s（Release 实测语义），说明开销在**每次调用的固定成本**而非哈希计算。
    //
    // 实测（NPC.cs，同二进制交叉对照，规避机器负载漂移）：
    //   改造前 AppendData 113,636,842 次 → CPU 13,016–13,375 ms
    //   改造后 AppendData      23,692 次 → CPU  7,203 ms
    //   净省约 6.0 s CPU（1.83×），GraphSnapshotVersion 四次运行逐字节相同。
    private sealed class SnapshotHasher : IDisposable
    {
        // 64 KiB：远大于常见字段长度，又小到能常驻 L2；字段写入基本不跨缓冲。
        // 容量经独立基准对照验证：4 KiB 慢 160 ms（flush 次数 197,221 vs 12,323），
        // 1 MiB 无进一步收益。
        private const int BufferCapacity = 64 * 1024;

        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] _buffer = new byte[BufferCapacity];

        // 按【实例】缓存已编码的 UTF-8 字节。边段 38.8M 次 WriteString 背后只有约 2.7K 个
        // 不同字符串实例（元数据池仅 4,241 条），故 GetByteCount+GetBytes 的重复调用可整体消除。
        // 用引用相等做键是保守的：同实例必同内容，故缓存的编码结果与现算逐字节相同。
        private readonly Dictionary<string, byte[]> _encoded =
          new(ReferenceComparer<string>.Instance);

        private int _position;

        internal void WriteInt32(int value)
        {
            if (_position + sizeof(int) > BufferCapacity)
            {
                Flush();
            }

            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position), value);
            _position += sizeof(int);
        }

        internal void WriteUInt32(uint value)
        {
            if (_position + sizeof(uint) > BufferCapacity)
            {
                Flush();
            }

            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_position), value);
            _position += sizeof(uint);
        }

        internal void WriteString(string? value)
        {
            if (value is null)
            {
                // 与原实现一致：null 字符串只写一个 -1，不写长度前缀。
                WriteInt32(-1);
                return;
            }

            if (!_encoded.TryGetValue(value, out var bytes))
            {
                bytes = Encoding.UTF8.GetBytes(value);
                _encoded.Add(value, bytes);
            }

            var byteCount = bytes.Length;
            WriteInt32(byteCount);
            if (byteCount == 0)
            {
                return;
            }

            if (byteCount > BufferCapacity)
            {
                // 超长串放不进缓冲：直接追加缓存下来的字节，避免为整串再复制一份。
                Flush();
                _hash.AppendData(bytes);
                return;
            }

            if (_position + byteCount > BufferCapacity)
            {
                Flush();
            }

            bytes.CopyTo(_buffer.AsSpan(_position));
            _position += byteCount;
        }

        internal string Finish()
        {
            Flush();
            return Convert.ToHexString(_hash.GetHashAndReset());
        }

        private void Flush()
        {
            if (_position == 0)
            {
                return;
            }

            _hash.AppendData(_buffer.AsSpan(0, _position));
            _position = 0;
        }

        public void Dispose()
        {
            _hash.Dispose();
        }
    }
}
