using System.Reflection;
using System.Runtime.CompilerServices;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using RoslynPrototype.Tests.TestCodeSet.Cpg;
using Xunit;

namespace RoslynPrototype.ContractTests.Cpg;

// M2（跨过程边索引单份快照）的定向契约。
//
// 分工：边的【产出顺序】由 CpgInterproceduralEdgeOrderTests 冻结原始插入序，
// 跨 DOP 的【集合等价】由 CpgWorkBatchInterproceduralTests 承担，预算/窗口的
// 【容量账本】由 InterproceduralPlanCompactionTests 承担；本文件不重复它们。
//
// 观测口径：PendingEdgeBuffer 在 FreezeQueryIndex() 时被整体释放（RunInterproceduralDataFlowPass
// 已在其之前消费完），故**构建完成后**无法再枚举待冻结边。因此本文件一律在
// 【可变图】上直接构造与真实 pass 同形的边集（CallTargets + DataFlow → 方法参数/返回），
// 再调用与生产完全相同的 InterproceduralEdgeSnapshot.Create 做断言。
//
// 这里只补三件既有测试看不见的事：
//   ① 结构门槛：整个索引子系统里 PendingEdge 的【完整值副本数】恰好等于保留边数 U，
//      四个桶只保存 int 序号——旧实现（四个 List<PendingEdge>）在本门槛下必然失败。
//   ② 完整载荷快照：建池后改变图内节点，池里读到的仍是建池当时的字段。
//      必须逐字段比较：NLCPGNode.Equals / GetHashCode 在有 StableAnchor 时【只看锚点】，
//      故用 Equals 断言会让本测试退化成恒真。
//   ③ 桶成员集的边界情形：无边、无相关边、只有 CallTargets、DataFlow 双索引命中、
//      缺失方法归属。
public sealed class InterproceduralEdgeIndexSnapshotTests
{
    // ── ① 结构门槛 ────────────────────────────────────────────────────────────────

    // 核心门槛：整个快照类型里【只有一处】保存 PendingEdge 完整值——那个长度为 U 的池数组；
    // 四个桶必须是 int[]（每个成员只占 4 B 序号）。
    //
    // 旧实现（四个 Dictionary<TKey, List<PendingEdge>>）下，PendingEdge 的完整副本数是
    // L 而非 U，且 L > U（同一条 DataFlow 边会同时落在两个桶里）——本断言因此必然失败。
    [Fact]
    public void EdgeSnapshot_StoresOneFullPayloadPerRetainedEdge()
    {
        var (graph, owners) = BuildShapedGraph(callSiteCount: 4);
        var snapshot = Create(graph, owners);
        var bucketSlotCount = snapshot.BucketSlotCount();

        Assert.True(snapshot.PoolCount > 0, "输入必须真的产生保留边，否则本测试是空断言。");
        Assert.True(
          bucketSlotCount > snapshot.PoolCount,
          $"本夹具必须存在跨桶共享（L={bucketSlotCount} 应 > U={snapshot.PoolCount}），"
          + "否则“每边只存一份”这条门槛没有被真正压到。");

        var snapshotType = typeof(NLCPGBuilder).GetNestedType(
          "InterproceduralEdgeSnapshot",
          BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(snapshotType);

        var fields = snapshotType!
          .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // PendingEdge 的完整值只允许出现在【一个】实例字段上（池），且必须是数组。
        var payloadFields = fields
          .Where(field => field.FieldType.IsArray &&
            field.FieldType.GetElementType() == typeof(NLCPGGraph.PendingEdge))
          .ToArray();
        Assert.Single(payloadFields);
        Assert.EndsWith("_pool", payloadFields[0].Name, StringComparison.Ordinal);

        // 不得存在任何 List<PendingEdge> 或 Dictionary<*, List<PendingEdge>> 字段。
        Assert.DoesNotContain(fields, field =>
          field.FieldType.IsGenericType &&
          field.FieldType.GetGenericArguments().Any(argument =>
            argument == typeof(NLCPGGraph.PendingEdge) ||
            (argument.IsGenericType && argument.GetGenericArguments().Contains(typeof(NLCPGGraph.PendingEdge)))));

        // 四个桶的容器类型必须把成员表示为 int[]，而不是 PendingEdge。
        var bucketFields = fields
          .Where(field => field.FieldType.IsGenericType &&
            field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
          .ToArray();
        Assert.Equal(4, bucketFields.Length);
        Assert.All(bucketFields, field =>
          Assert.Equal(
            typeof(int[]),
            field.FieldType.GetGenericArguments()[1]));
    }

    // 池元素数恰为 U：逐条枚举待冻结边，按保留条件（CallTargets 全部 + DataFlow 全部）
    // 独立算出 U，与池长度比对。这是"精确分配"的直接证据——不多也不少。
    // 同时验证：池元素数 == 保留边数，且桶内每个序号都是合法的池下标。
    [Fact]
    public void EdgeSnapshot_PoolLength_EqualsRetainedEdgeCount()
    {
        var (graph, owners) = BuildShapedGraph(callSiteCount: 5);

        var expected = graph.EnumeratePendingEdgesLazily()
          .Count(edge => edge.Kind is NLCPGEdgeKind.CallTargets or NLCPGEdgeKind.DataFlow);
        var snapshot = Create(graph, owners);
        var bucketSlotCount = snapshot.BucketSlotCount();

        Assert.True(expected > 0);
        Assert.Equal(expected, snapshot.PoolCount);
        // 池是精确分配：Capacity 就是 Length（数组没有"多留一倍"的余地）。
        Assert.Equal(expected, snapshot.Pool.Count);
        // L > U ⇒ 确有共享；且每个桶槽位都指向池内合法下标。
        Assert.True(bucketSlotCount > snapshot.PoolCount);
        Assert.All(collectOrdinals(), ordinal =>
          Assert.InRange(ordinal, 0, snapshot.PoolCount - 1));

        IEnumerable<int> collectOrdinals()
        {
            foreach (var callSite in graph.Nodes.Where(node => node.Kind == NLCPGNodeKind.CallSite))
            {
                foreach (var ordinal in snapshot.CallTargetsForSource(callSite))
                {
                    yield return ordinal;
                }
            }

            foreach (var node in graph.Nodes)
            {
                foreach (var ordinal in snapshot.DataFlowForTarget(node))
                {
                    yield return ordinal;
                }
            }

            foreach (var key in owners.Values.Distinct(StringComparer.Ordinal))
            {
                if (snapshot.TryGetArgumentsForMethod(key, out var arguments))
                {
                    foreach (var ordinal in arguments)
                    {
                        yield return ordinal;
                    }
                }

                if (snapshot.TryGetReturnsForMethod(key, out var returns))
                {
                    foreach (var ordinal in returns)
                    {
                        yield return ordinal;
                    }
                }
            }
        }
    }

    // ── ② 完整载荷快照 ────────────────────────────────────────────────────────────

    // §5.3：建池后改变图内节点，再读取池，必须仍得到【原字段】。
    //
    // 构造成立的前提是"图内节点确实被改了"：先入一个只有 Name 的节点，再用同一个
    // StableAnchor 入一个带 FullName/Signature/TypeFullName/FilePath/Span 的同锚点节点，
    // AddNode 内部的 MergeNode 会把新字段补进图中的那一份（实测生效，见下面的前置断言）。
    //
    // ⚠ 逐字段比较是必须的：NLCPGNode.Equals 在两端都有 StableAnchor 时【只比锚点】，
    //   锚点相同则恒等，故用 Equals 断言会让本测试变成恒真。
    [Fact]
    public void EdgeSnapshot_AfterGraphNodeIsMerged_StillReturnsOriginalPayload()
    {
        var graph = new NLCPGGraph();
        var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "target"));
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
        graph.AddEdge(source, target, NLCPGEdgeKind.DataFlow);

        var snapshot = NLCPGBuilder.InterproceduralEdgeSnapshot.Create(
          graph.EnumeratePendingEdgesLazily(),
          new Dictionary<NLCPGNode, string>());

        Assert.Equal(1, snapshot.PoolCount);
        var captured = snapshot.Edge(0);
        var capturedTargetFields = DescribeNode(captured.TargetNode);

        // 用同一锚点重新入图，把更多字段合并进图内节点。
        var anchor = target.StableAnchor!.Value;
        graph.AddNode(
          new NLCPGNodeDraft(
            NLCPGNodeKind.Operation,
            Name: "target",
            FullName: "Merged.Target",
            Signature: "int Merged.Target()",
            TypeFullName: "Merged",
            FilePath: "merged.cs",
            SpanStart: 111,
            SpanEnd: 222),
          stableAnchor: anchor);

        // 前置断言：图内节点【确实】变了，否则下面的池断言没有意义。
        var mergedTarget = graph.Nodes.Single(node =>
          node.StableAnchor!.Value.Equals(anchor));
        Assert.NotEqual(capturedTargetFields, DescribeNode(mergedTarget));
        // 锚点相同 ⇒ Equals 判等，这正是"不能用 Equals 断言"的原因。
        Assert.True(mergedTarget.Equals(captured.TargetNode));

        // 池读到的仍是建池当时的完整字段快照。
        Assert.Equal(capturedTargetFields, DescribeNode(snapshot.Edge(0).TargetNode));
        Assert.Equal(captured, snapshot.Edge(0));
    }

    // ── ③ 桶成员集的边界情形 ──────────────────────────────────────────────────────

    // 无边：池为 0，四个桶都为空，缺失键返回共享空数组（不分配）。
    [Fact]
    public void EdgeSnapshot_NoEdges_YieldsEmptyPoolAndBuckets()
    {
        var graph = new NLCPGGraph();
        var node = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "lonely"));

        var snapshot = NLCPGBuilder.InterproceduralEdgeSnapshot.Create(
          graph.EnumeratePendingEdgesLazily(),
          new Dictionary<NLCPGNode, string>());

        Assert.Equal(0, snapshot.PoolCount);
        Assert.Empty(snapshot.Pool);
        Assert.Empty(snapshot.CallTargetsForSource(node));
        Assert.Empty(snapshot.DataFlowForTarget(node));
        Assert.False(snapshot.TryGetArgumentsForMethod("M", out _));
        Assert.False(snapshot.TryGetReturnsForMethod("M", out _));
        // 共享空数组：缺失键不分配新数组。
        Assert.Same(
          snapshot.DataFlowForTarget(node),
          snapshot.CallTargetsForSource(node));
    }

    // 无相关边：只有非 CallTargets/DataFlow 的边时，池仍为 0（只收集这两类）。
    [Fact]
    public void EdgeSnapshot_OnlyIrrelevantKinds_YieldsEmptyPool()
    {
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "s"));
        var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "t"));
        graph.AddEdge(source, target, NLCPGEdgeKind.CfgNext);
        Assert.Equal(1, graph.CurrentEdgeCount);

        var snapshot = NLCPGBuilder.InterproceduralEdgeSnapshot.Create(
          graph.EnumeratePendingEdgesLazily(),
          new Dictionary<NLCPGNode, string>());

        Assert.Equal(0, snapshot.PoolCount);
    }

    // 只有 CallTargets：只进第一类桶，方法桶恒空，池长度 1。
    [Fact]
    public void EdgeSnapshot_CallTargetsOnly_FillsOnlySourceBucket()
    {
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.CallSite, Name: "call"));
        var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, Name: "callee"));
        graph.AddEdge(source, target, NLCPGEdgeKind.CallTargets);

        var snapshot = NLCPGBuilder.InterproceduralEdgeSnapshot.Create(
          graph.EnumeratePendingEdgesLazily(),
          new Dictionary<NLCPGNode, string>());

        Assert.Equal(1, snapshot.PoolCount);
        Assert.Equal(new[] { 0 }, snapshot.CallTargetsForSource(source));
        Assert.Empty(snapshot.DataFlowForTarget(target));
        Assert.False(snapshot.TryGetArgumentsForMethod("M", out _));
        Assert.False(snapshot.TryGetReturnsForMethod("M", out _));
    }

    // DataFlow 双索引命中：目标为方法参数且能解析方法归属时，同一个池序号必须
    // 同时出现在【按目标桶】与【参数方法桶】里——这就是 L > U 的来源，也是共享的证明。
    [Fact]
    public void EdgeSnapshot_DataFlowHit_TwoBucketsShareSameOrdinal()
    {
        var graph = new NLCPGGraph();
        var parameter = graph.AddNode(new NLCPGNodeDraft(
          NLCPGNodeKind.MethodParameter,
          Name: "value"));
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
        graph.AddEdge(source, parameter, NLCPGEdgeKind.DataFlow);

        var snapshot = NLCPGBuilder.InterproceduralEdgeSnapshot.Create(
          graph.EnumeratePendingEdgesLazily(),
          new Dictionary<NLCPGNode, string> { [parameter] = "Sample.M(int)" });

        Assert.Equal(1, snapshot.PoolCount);
        var byTarget = snapshot.DataFlowForTarget(parameter);
        Assert.True(snapshot.TryGetArgumentsForMethod("Sample.M(int)", out var byMethod));

        Assert.Equal(new[] { 0 }, byTarget);
        // 两个桶各自是独立的 int[]，但保存的是【同一个池序号】：
        // 完整载荷在池里只有一份，两个桶都指向它——这就是共享。
        Assert.NotSame(byTarget, byMethod);
        Assert.Equal(byTarget, byMethod);
        Assert.Equal(1, snapshot.PoolCount);
    }

    // 返回边走【返回方法桶】，不走参数方法桶。
    [Fact]
    public void EdgeSnapshot_MethodReturnEdge_FillsReturnBucket()
    {
        var graph = new NLCPGGraph();
        var methodReturn = graph.AddNode(new NLCPGNodeDraft(
          NLCPGNodeKind.MethodReturn,
          Name: "result"));
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
        graph.AddEdge(source, methodReturn, NLCPGEdgeKind.DataFlow);

        var snapshot = NLCPGBuilder.InterproceduralEdgeSnapshot.Create(
          graph.EnumeratePendingEdgesLazily(),
          new Dictionary<NLCPGNode, string> { [methodReturn] = "Sample.M(int)" });

        Assert.Equal(1, snapshot.PoolCount);
        Assert.Equal(new[] { 0 }, snapshot.DataFlowForTarget(methodReturn));
        Assert.True(snapshot.TryGetReturnsForMethod("Sample.M(int)", out var byMethod));
        Assert.Equal(new[] { 0 }, byMethod);
        Assert.False(snapshot.TryGetArgumentsForMethod("Sample.M(int)", out _));
    }

    // 缺失方法归属：目标虽是方法参数，但不在归属表里 ⇒ 只进按目标桶，方法桶为空。
    [Fact]
    public void EdgeSnapshot_MissingMethodOwnership_SkipsMethodBuckets()
    {
        var graph = new NLCPGGraph();
        var parameter = graph.AddNode(new NLCPGNodeDraft(
          NLCPGNodeKind.MethodParameter,
          Name: "value"));
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
        graph.AddEdge(source, parameter, NLCPGEdgeKind.DataFlow);

        var snapshot = NLCPGBuilder.InterproceduralEdgeSnapshot.Create(
          graph.EnumeratePendingEdgesLazily(),
          new Dictionary<NLCPGNode, string>());

        Assert.Equal(1, snapshot.PoolCount);
        Assert.Equal(new[] { 0 }, snapshot.DataFlowForTarget(parameter));
        Assert.False(snapshot.TryGetArgumentsForMethod("Sample.M(int)", out _));
        Assert.False(snapshot.TryGetReturnsForMethod("Sample.M(int)", out _));
    }

    // 桶内序号必须保持【原插入序】：同键多条边按加入顺序编号，不排序也不去重。
    [Fact]
    public void EdgeSnapshot_SameKeyMultipleEdges_PreserveInsertionOrder()
    {
        var graph = new NLCPGGraph();
        var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.CallSite, Name: "call"));
        var first = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, Name: "first"));
        var second = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, Name: "second"));
        var third = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, Name: "third"));

        graph.AddEdge(source, first, NLCPGEdgeKind.CallTargets);
        graph.AddEdge(source, second, NLCPGEdgeKind.CallTargets);
        graph.AddEdge(source, third, NLCPGEdgeKind.CallTargets);

        var snapshot = NLCPGBuilder.InterproceduralEdgeSnapshot.Create(
          graph.EnumeratePendingEdgesLazily(),
          new Dictionary<NLCPGNode, string>());

        var ordinals = snapshot.CallTargetsForSource(source);
        Assert.Equal(new[] { 0, 1, 2 }, ordinals);
        // 序号回读出来的目标节点顺序与加边顺序一致。
        Assert.Equal(first.StableAnchor, snapshot.Edge(ordinals[0]).TargetNode.StableAnchor);
        Assert.Equal(second.StableAnchor, snapshot.Edge(ordinals[1]).TargetNode.StableAnchor);
        Assert.Equal(third.StableAnchor, snapshot.Edge(ordinals[2]).TargetNode.StableAnchor);
    }

    // 端到端：与真实 pass 同形的图上，每个桶的成员都能经池回读到一条自洽的边
    // （序号不越界、不串桶、端点与桶的键一致）。
    [Fact]
    public void EdgeSnapshot_BucketsResolveBackToPool()
    {
        var (graph, owners) = BuildShapedGraph(callSiteCount: 6);
        var snapshot = Create(graph, owners);

        Assert.True(snapshot.PoolCount > 0);

        // 池中每条边的种类只可能是被收集的两类。
        Assert.All(snapshot.Pool, edge =>
          Assert.True(edge.Kind is NLCPGEdgeKind.CallTargets or NLCPGEdgeKind.DataFlow));

        // 每个调用点的 CallTargets 桶经池回读，端点与边种类必须自洽。
        var callSites = graph.Nodes
          .Where(node => node.Kind == NLCPGNodeKind.CallSite)
          .ToArray();
        Assert.NotEmpty(callSites);
        var resolved = 0;
        foreach (var callSite in callSites)
        {
            foreach (var ordinal in snapshot.CallTargetsForSource(callSite))
            {
                var edge = snapshot.Edge(ordinal);
                Assert.Equal(NLCPGEdgeKind.CallTargets, edge.Kind);
                Assert.Equal(callSite.StableAnchor, edge.SourceNode.StableAnchor);
                resolved += 1;
            }
        }

        Assert.True(resolved > 0, "至少要有一个调用点带 CallTargets 成员，否则回读断言是空的。");

        // 按目标桶：每条成员的 TargetNode 必须等于该桶的键，且种类为 DataFlow。
        var dataFlowResolved = 0;
        foreach (var node in graph.Nodes)
        {
            foreach (var ordinal in snapshot.DataFlowForTarget(node))
            {
                var edge = snapshot.Edge(ordinal);
                Assert.Equal(NLCPGEdgeKind.DataFlow, edge.Kind);
                Assert.Equal(node.StableAnchor, edge.TargetNode.StableAnchor);
                dataFlowResolved += 1;
            }
        }

        Assert.True(dataFlowResolved > 0, "至少要有一个按目标桶成员，否则回读断言是空的。");

        // 方法桶：成员边的目标种类必须与方法桶的种类匹配（参数桶 ↔ MethodParameter）。
        foreach (var pair in owners)
        {
            if (snapshot.TryGetArgumentsForMethod(pair.Value, out var arguments))
            {
                Assert.All(arguments, ordinal =>
                  Assert.Equal(NLCPGNodeKind.MethodParameter, snapshot.Edge(ordinal).TargetNode.Kind));
            }

            if (snapshot.TryGetReturnsForMethod(pair.Value, out var returns))
            {
                Assert.All(returns, ordinal =>
                  Assert.Equal(NLCPGNodeKind.MethodReturn, snapshot.Edge(ordinal).TargetNode.Kind));
            }
        }
    }

    // ── ③ 净保留字节门槛（计划 §7 的 ≥15%）────────────────────────────────────────

    // 索引子系统的**净保留字节**对照：旧实现保留 `W × Σ旧容量`，新实现保留
    // `W × U + 4 × L + 24 B × 桶数组个数`（int 槽位 + int[] 对象头）。
    //
    // Σ旧容量按 List<T>.Add 的扩容史精确复算（首次 4，其后翻倍），不是用 Count 近似——
    // 用 Count 会把旧实现的尾部空槽算漏，从而低估收益。
    //
    // 本断言在【旧实现】下必然失败：那时 PendingEdge 的完整副本是 L 份而不是 U 份，
    // 且 Pool 这个概念根本不存在。
    [Fact]
    public void EdgeSnapshot_NetRetainedBytes_DropAtLeastFifteenPercent()
    {
        var (graph, owners) = BuildShapedGraph(callSiteCount: 8);
        var snapshot = Create(graph, owners);

        var width = Unsafe.SizeOf<NLCPGGraph.PendingEdge>();
        var poolCount = snapshot.PoolCount;
        Assert.True(poolCount > 0, "字节门槛测试必须有非空池，否则是空断言。");

        var lengths = snapshot.BucketLengths().ToArray();
        var membership = lengths.Sum();
        Assert.True(
          membership > poolCount,
          $"本夹具必须有跨桶共享（L={membership} 应 > U={poolCount}），否则收益只来自精确分配。");

        // 旧实现：四个 List<PendingEdge>，各自按 4→8→16… 倍增。
        long oldCapacity = 0;
        foreach (var count in lengths)
        {
            if (count == 0)
            {
                // 空桶在旧实现里根本不会被创建。
                continue;
            }

            var capacity = 4;
            while (capacity < count)
            {
                capacity *= 2;
            }

            oldCapacity += capacity;
        }

        // int[] 对象头 16 B + 长度字段，按 8 B 对齐后每个数组固定 24 B。
        const int ArrayHeaderBytes = 24;
        var oldBytes = oldCapacity * (long)width;
        var newBytes = (long)poolCount * width
          + (long)membership * sizeof(int)
          + (long)lengths.Length * ArrayHeaderBytes;

        var dropPercent = (oldBytes - newBytes) * 100.0 / oldBytes;

        Assert.True(
          dropPercent >= 15.0,
          $"索引子系统净保留字节降幅必须 ≥15%（计划 §7）："
          + $"旧 {oldBytes:N0} B → 新 {newBytes:N0} B = {dropPercent:F2}%。"
          + $"（W={width} B、U={poolCount}、L={membership}、Σ旧容量={oldCapacity}、桶数={lengths.Length}）");
    }

    // 无共享边对照。计划 §4.3 / §7 要求**公开记录**这条边界，不得隐藏。
    //
    // 这条断言的方向是**反的**：它不证明收益，而是钉住"当 L == U 时本项近乎打平、略亏"这一事实。
    // 若有人把池+int 层说成"天然节省"，这条会指出：按精确容量口径，多出来的正是
    // `L × 4 B` 序号槽与每桶 24 B 的 int[] 对象头，L==U 时旧实现本来也不需要任何容量 slack。
    [Fact]
    public void EdgeSnapshot_NoSharingControl_DisclosesIntLayerOverhead()
    {
        var (graph, owners) = BuildNoSharingGraph(callSiteCount: 400);
        var snapshot = Create(graph, owners);

        var width = Unsafe.SizeOf<NLCPGGraph.PendingEdge>();
        var lengths = snapshot.BucketLengths().ToArray();
        var membership = lengths.Sum();
        Assert.Equal(snapshot.PoolCount, membership);   // L == U：这是"无共享"的定义

        // 旧侧按【精确容量】重算：四个列表各自恰好 Count，即旧实现本来也能达到的最优形态。
        const int ArrayHeaderBytes = 24;
        var preciseOldBytes = (long)membership * width;
        var newBytes = (long)snapshot.PoolCount * width
          + (long)membership * sizeof(int)
          + (long)lengths.Length * ArrayHeaderBytes;

        Assert.True(
          newBytes > preciseOldBytes,
          $"L==U 时池+int 层应当净增（多出序号槽与数组头），实测 旧={preciseOldBytes:N0} B、"
          + $"新={newBytes:N0} B。若此断言失败，说明夹具不是无共享形态。");

        var overheadPercent = (newBytes - preciseOldBytes) * 100.0 / preciseOldBytes;

        // 只钉住"近乎打平"的量级，不钉死具体数值（对象头/桶数会随夹具规模变化）。
        Assert.InRange(overheadPercent, 0.0, 20.0);

        // 同时如实记录：按实际的 List 扩容口径，无共享形态的降幅仍可能很大——
        // 但那来自消除容量 slack，**不是**来自共享。两个口径必须一起读。
        var doublingOldBytes = 0L;
        foreach (var count in lengths)
        {
            if (count == 0)
            {
                continue;
            }

            var capacity = 4;
            while (capacity < count)
            {
                capacity *= 2;
            }

            doublingOldBytes += capacity * (long)width;
        }

        var doublingDrop = (doublingOldBytes - newBytes) * 100.0 / doublingOldBytes;
        Assert.True(
          doublingDrop > 50.0,
          $"按 List 扩容口径，无共享对照的降幅也应当很大（来自容量 slack）：实测 {doublingDrop:F2}%。"
          + "这条与上面的净增结论**同时成立**，正是不得把收益归因给共享的原因。");
    }

    // 无共享形态：每个调用点一条 CallTargets、每条返回一条 DataFlow，端点两两不同，
    // 故没有任何边同时命中两个桶（L == U）。
    private static (NLCPGGraph Graph, Dictionary<NLCPGNode, string> Owners) BuildNoSharingGraph(
      int callSiteCount)
    {
        var graph = new NLCPGGraph();
        var owners = new Dictionary<NLCPGNode, string>();

        for (var index = 0; index < callSiteCount; index += 1)
        {
            var callSite = graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.CallSite,
              Name: $"Call{index}",
              FullName: $"NoShare.Caller.Call{index}",
              FilePath: "noshare.cs"));
            var callee = graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.Method,
              Name: $"Callee{index}",
              FullName: $"NoShare.Callee{index}",
              FilePath: "noshare.cs"));
            var methodReturn = graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.MethodReturn,
              Name: "return",
              FullName: $"NoShare.Callee{index}.return",
              FilePath: "noshare.cs"));

            // 两个桶的键各自唯一，故每条边的目标只进一个桶。
            owners[methodReturn] = $"NoShare.Callee{index}(int)";
            graph.AddEdge(callSite, callee, NLCPGEdgeKind.CallTargets);
            graph.AddEdge(methodReturn, callSite, NLCPGEdgeKind.DataFlow);
        }

        return (graph, owners);
    }

    // ── ③ ⑥ 的 BridgeKind 导出护栏（执行文档 §7 判据 3）────────────────────────────
    //
    // ⑥（延迟物化）把计划的 BridgeKind 从载体里删掉，改为由池内边的端点种类导出
    // （NLCPGBuilder.BridgeKindOf）。这是本项【唯一】的隐式耦合：导出的 `_` 兜底分支
    // 假定"当前只有三个产生者"。若将来新增第 4 个产生者，它会【静默给出错误结果】，
    // 而功能测试未必看得见（错的是桥标签，不是边集）。
    //
    // 本测试把"可以导出"这个【假设】变成可观测事实：对三个段各造一条边，
    // 断言导出值等于该段应有的 BridgeKind，并断言三段的端点种类【互斥】
    // （这正是导出函数无歧义的依据）。
    [Fact]
    public void BridgeKindOf_DerivesEveryBridgeKindFromPoolEndpointKind()
    {
        // 段 1：DataFlow → 被调方法参数，端点种类 MethodParameter。
        var argumentEdge = MakeEdge(
          NLCPGNodeKind.Operation,
          NLCPGNodeKind.MethodParameter);
        Assert.Equal(
          NLCPGInterproceduralBridgeKind.ArgumentToParameter,
          NLCPGBuilder.BridgeKindOf(argumentEdge));

        // 段 2：DataFlow → 调用点，源是方法返回，端点种类 CallSite（走 `_` 兜底）。
        var callResultEdge = MakeEdge(
          NLCPGNodeKind.MethodReturn,
          NLCPGNodeKind.CallSite);
        Assert.Equal(
          NLCPGInterproceduralBridgeKind.MethodReturnToCallResult,
          NLCPGBuilder.BridgeKindOf(callResultEdge));

        // 段 3：DataFlow → 方法返回，端点种类 MethodReturn。
        var returnEdge = MakeEdge(
          NLCPGNodeKind.Operation,
          NLCPGNodeKind.MethodReturn);
        Assert.Equal(
          NLCPGInterproceduralBridgeKind.ReturnToMethodReturn,
          NLCPGBuilder.BridgeKindOf(returnEdge));

        // 判据无歧义的结构性前提：三段的【目标】端点种类互不相同。
        // 若将来某两段共用一种目标种类，导出函数就无法区分它们 —— 本断言会先失败。
        var targetKinds = new[]
        {
            argumentEdge.TargetNode.Kind,
            callResultEdge.TargetNode.Kind,
            returnEdge.TargetNode.Kind,
        };
        Assert.Equal(targetKinds.Length, targetKinds.Distinct().Count());

        // 三段的导出值也必须两两不同，否则上面的逐段断言可能"碰巧"通过。
        var derived = targetKinds
          .Select(kind => NLCPGBuilder.BridgeKindOf(MakeEdge(NLCPGNodeKind.Operation, kind)))
          .ToArray();
        Assert.Equal(derived.Length, derived.Distinct().Count());

        static NLCPGGraph.PendingEdge MakeEdge(NLCPGNodeKind sourceKind, NLCPGNodeKind targetKind)
        {
            var source = new NLCPGNodeDraft(
              sourceKind,
              Name: "source",
              FullName: "BridgeKindProbe.source",
              FilePath: "bridgekind.cs",
              SpanStart: 0,
              SpanEnd: 1);
            var target = new NLCPGNodeDraft(
              targetKind,
              Name: "target",
              FullName: "BridgeKindProbe.target",
              FilePath: "bridgekind.cs",
              SpanStart: 2,
              SpanEnd: 3);

            // PendingEdge 是嵌套类型：走可变图取两条真实边，再改写目标种类不便，
            // 故直接构造。构造器是 record 的位置参数，可由测试直接 new。
            return new NLCPGGraph.PendingEdge(
              ToNode(source),
              ToNode(target),
              NLCPGEdgeKind.DataFlow,
              StructuredLabel: null,
              ContextId: null,
              CallSiteContext: null);
        }

        // NLCPGNodeDraft → NLCPGNode：借一张临时图完成物化，避免手工拼稳定锚点。
        static NLCPGNode ToNode(NLCPGNodeDraft draft)
        {
            return new NLCPGGraph().AddNode(draft);
        }
    }

    // ── 辅助 ──────────────────────────────────────────────────────────────────────

    // 构造与真实 pass 同形的边集：callSiteCount 个调用点，每个调用点
    //   ① 一条 CallTargets → 方法入口；
    //   ② 一条 DataFlow ← 被调方法参数（进按目标桶 + 参数方法桶）；
    //   ③ 一条 DataFlow ← 被调方法返回（进按目标桶 + 返回方法桶）。
    // 这样 L = 3×callSiteCount + 2×callSiteCount（两条双命中边各多一次）> U，
    // 与真实语料上的 L/U = 1.182 同形。
    private static (NLCPGGraph Graph, Dictionary<NLCPGNode, string> Owners) BuildShapedGraph(
      int callSiteCount)
    {
        var graph = new NLCPGGraph();
        var owners = new Dictionary<NLCPGNode, string>();

        for (var index = 0; index < callSiteCount; index += 1)
        {
            var callSite = graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.CallSite,
              Name: $"Call{index}",
              FullName: $"Sample.Caller.Call{index}",
              FilePath: "shaped.cs",
              SpanStart: index * 10,
              SpanEnd: index * 10 + 5));
            var callee = graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.Method,
              Name: $"Callee{index}",
              FullName: $"Sample.Callee{index}",
              FilePath: "shaped.cs"));
            var parameter = graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.MethodParameter,
              Name: "value",
              FullName: $"Sample.Callee{index}.value",
              FilePath: "shaped.cs"));
            var methodReturn = graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.MethodReturn,
              Name: "return",
              FullName: $"Sample.Callee{index}.return",
              FilePath: "shaped.cs"));
            var argumentSource = graph.AddNode(new NLCPGNodeDraft(
              NLCPGNodeKind.Operation,
              Name: $"arg{index}",
              FullName: $"Sample.Caller.arg{index}",
              FilePath: "shaped.cs"));

            var methodKey = $"Sample.Callee{index}(int)";
            owners[parameter] = methodKey;
            owners[methodReturn] = methodKey;

            graph.AddEdge(callSite, callee, NLCPGEdgeKind.CallTargets);
            graph.AddEdge(argumentSource, parameter, NLCPGEdgeKind.DataFlow);
            graph.AddEdge(methodReturn, callSite, NLCPGEdgeKind.DataFlow);
        }

        return (graph, owners);
    }

    private static NLCPGBuilder.InterproceduralEdgeSnapshot Create(
      NLCPGGraph graph,
      IReadOnlyDictionary<NLCPGNode, string> owners)
    {
        return NLCPGBuilder.InterproceduralEdgeSnapshot.Create(
          graph.EnumeratePendingEdgesLazily(),
          owners);
    }

    // 逐字段描述一个节点。刻意不使用 Equals：NLCPGNode 在两端都有 StableAnchor 时
    // 只比较锚点，字段差异会被完全掩盖。
    private static string DescribeNode(NLCPGNode node)
    {
        return string.Join(
          "|",
          node.Kind,
          node.NameId,
          node.FullNameId,
          node.SignatureId,
          node.DispatchKind,
          node.TypeFullNameId,
          node.FilePathId,
          node.SpanStart,
          node.SpanEnd,
          node.IsImplicit);
    }
}
