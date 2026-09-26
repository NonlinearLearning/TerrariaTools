using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using NLCPG.Builder.Concurrency;
using NLCPG.Builder.Streaming;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests;

public sealed class LocalCpgFragmentContractTests
{
    private readonly ITestOutputHelper _output;

    public LocalCpgFragmentContractTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Fragment_StoresStableDescriptorsWithoutFinalNodeIds()
    {
        var nodeAnchor = CreateAnchor(10, 20, StableNodeRole.SyntaxNode);
        var node = CreateDescriptor(nodeAnchor);
        var boundary = new CpgBoundaryReference(
            CreateAnchor(30, 40, StableNodeRole.Symbol),
            "external:Target",
            CpgBoundaryReferenceKind.External,
            IsAvailable: false);
        var edge = new CpgEdgeCandidate(
            nodeAnchor,
            boundary.Anchor,
            NLCPGEdgeKind.OpResolvesToSymbol,
            StructuredLabel: null,
            ContextId: null,
            CallSiteContext: null);
        var metrics = new CpgFragmentMetrics(
            CollectionElapsedMilliseconds: 2,
            LocalSolveElapsedMilliseconds: 3,
            EstimatedNodeCount: 1,
            EstimatedEdgeCount: 1,
            ActualNodeCount: 1,
            ActualEdgeCount: 1,
            FragmentBytes: 128);

        var fragment = new LocalCpgFragment(
            batchId: 7,
            sourceFilePath: "sample.cs",
            stableOrder: 2,
            nodes: new[] { node },
            edges: new[] { edge },
            methodSummaries: new[] { new CpgMethodSummary("M:Target", 30, 40) },
            boundaryReferences: new[] { boundary },
            metrics,
            diagnostics: new[] { new CpgDiagnostic("CPG001", "target unavailable") });

        Assert.Equal(7, fragment.BatchId);
        Assert.Equal(nodeAnchor, fragment.Nodes[0].Anchor);
        Assert.Equal(edge, fragment.Edges[0]);
        Assert.Equal(1, fragment.Metrics.ActualNodeCount);
        Assert.Equal(1, fragment.Metrics.ActualEdgeCount);
        Assert.False(fragment.BoundaryReferences[0].IsAvailable);
        Assert.DoesNotContain(typeof(LocalCpgFragment).GetProperties(), property =>
            property.PropertyType.FullName?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Fragment_OrderingUsesStableBatchOrder()
    {
        var first = CreateFragment(batchId: 2, stableOrder: 1);
        var second = CreateFragment(batchId: 1, stableOrder: 0);

        var ordered = new[] { first, second }.OrderBy(fragment => fragment.StableOrder).ToArray();

        Assert.Equal(new[] { second.BatchId, first.BatchId }, ordered.Select(fragment => fragment.BatchId));
    }

    [Fact]
    public void Fragment_RejectsEdgeEndpointThatIsNeitherLocalNorBoundary()
    {
        var source = CreateAnchor(10, 20, StableNodeRole.SyntaxNode);
        var missingTarget = CreateAnchor(30, 40, StableNodeRole.Symbol);
        var edge = new CpgEdgeCandidate(
            source,
            missingTarget,
            NLCPGEdgeKind.OpResolvesToSymbol,
            StructuredLabel: null,
            ContextId: null,
            CallSiteContext: null);

        Assert.Throws<ArgumentException>(() => new LocalCpgFragment(
            batchId: 1,
            sourceFilePath: "sample.cs",
            stableOrder: 0,
            nodes: new[] { CreateDescriptor(source) },
            edges: new[] { edge },
            methodSummaries: Array.Empty<CpgMethodSummary>(),
            boundaryReferences: Array.Empty<CpgBoundaryReference>(),
            CpgFragmentMetrics.Empty,
            diagnostics: Array.Empty<CpgDiagnostic>()));
    }

    /// <summary>
    /// 公开构造器必须继续防御性复制：调用方在构造后改写自己传入的数组，
    /// fragment 的观察结果不得变化。五组集合逐一覆盖。
    /// </summary>
    [Fact]
    public void Constructor_WhenCallerMutatesPassedArrays_FragmentObservationIsUnchanged()
    {
        var firstAnchor = CreateAnchor(10, 20, StableNodeRole.SyntaxNode);
        var secondAnchor = CreateAnchor(30, 40, StableNodeRole.SyntaxNode);
        var boundaryAnchor = CreateAnchor(50, 60, StableNodeRole.Symbol);
        var nodes = new[] { CreateDescriptor(firstAnchor), CreateDescriptor(secondAnchor) };
        var edges = new[] { CreateEdge(firstAnchor, secondAnchor) };
        var summaries = new[] { new CpgMethodSummary("M:First", 1, 2) };
        var boundaries = new[] { CreateBoundary(boundaryAnchor, "external:Target") };
        var diagnostics = new[] { new CpgDiagnostic("CPG001", "first") };

        var fragment = new LocalCpgFragment(
            1, "sample.cs", 0, nodes, edges, summaries, boundaries, CpgFragmentMetrics.Empty, diagnostics);

        // 构造之后再改原数组：fragment 必须持有自己的副本。
        nodes[0] = CreateDescriptor(CreateAnchor(70, 80, StableNodeRole.SyntaxNode));
        nodes[1] = default;
        edges[0] = CreateEdge(secondAnchor, firstAnchor);
        summaries[0] = new CpgMethodSummary("M:Other", 9, 9);
        boundaries[0] = CreateBoundary(CreateAnchor(90, 95, StableNodeRole.Symbol), "external:Other");
        diagnostics[0] = new CpgDiagnostic("CPG999", "mutated");

        Assert.Equal(2, fragment.Nodes.Count);
        Assert.Equal(firstAnchor, fragment.Nodes[0].Anchor);
        Assert.Equal(secondAnchor, fragment.Nodes[1].Anchor);
        Assert.Equal(CreateEdge(firstAnchor, secondAnchor), fragment.Edges[0]);
        Assert.Equal(new CpgMethodSummary("M:First", 1, 2), fragment.MethodSummaries[0]);
        Assert.Equal("external:Target", fragment.BoundaryReferences[0].Reference);
        Assert.Equal("CPG001", fragment.Diagnostics[0].Code);
    }

    /// <summary>传入可变 List 时同样必须复制，而不是持有调用方的列表。</summary>
    [Fact]
    public void Constructor_WhenCallerMutatesPassedList_FragmentObservationIsUnchanged()
    {
        var firstAnchor = CreateAnchor(10, 20, StableNodeRole.SyntaxNode);
        var nodes = new List<CpgNodeDescriptor> { CreateDescriptor(firstAnchor) };
        var diagnostics = new List<CpgDiagnostic> { new("CPG001", "first") };

        var fragment = new LocalCpgFragment(
            1,
            "sample.cs",
            0,
            nodes,
            new List<CpgEdgeCandidate>(),
            new List<CpgMethodSummary>(),
            new List<CpgBoundaryReference>(),
            CpgFragmentMetrics.Empty,
            diagnostics);

        nodes.Clear();
        nodes.Add(CreateDescriptor(CreateAnchor(30, 40, StableNodeRole.SyntaxNode)));
        diagnostics.Clear();

        Assert.Single(fragment.Nodes);
        Assert.Equal(firstAnchor, fragment.Nodes[0].Anchor);
        Assert.Single(fragment.Diagnostics);
    }

    /// <summary>
    /// 公开属性不是可写数组：两条入口给出的都是只读包装，IList 写入被拒绝。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FragmentProperties_AreReadOnlyViewsThatRejectWrites(bool owned)
    {
        var firstAnchor = CreateAnchor(10, 20, StableNodeRole.SyntaxNode);
        var boundaryAnchor = CreateAnchor(50, 60, StableNodeRole.Symbol);
        var nodes = new[] { CreateDescriptor(firstAnchor) };
        var edges = new[] { CreateBoundaryEdge(firstAnchor, boundaryAnchor) };
        var summaries = new[] { new CpgMethodSummary("M:First", 1, 2) };
        var boundaries = new[] { CreateBoundary(boundaryAnchor, "external:Target") };
        var diagnostics = new[] { new CpgDiagnostic("CPG001", "first") };

        var fragment = owned
          ? LocalCpgFragment.CreateOwned(
            1, "sample.cs", 0, nodes, edges, summaries, boundaries, CpgFragmentMetrics.Empty, diagnostics)
          : new LocalCpgFragment(
            1, "sample.cs", 0, nodes, edges, summaries, boundaries, CpgFragmentMetrics.Empty, diagnostics);

        Assert.False(fragment.Nodes is CpgNodeDescriptor[], "公开属性不得把可写数组暴露出去。");
        Assert.False(fragment.Edges is CpgEdgeCandidate[], "公开属性不得把可写数组暴露出去。");
        Assert.IsType<ReadOnlyCollection<CpgNodeDescriptor>>(fragment.Nodes);
        Assert.IsType<ReadOnlyCollection<CpgEdgeCandidate>>(fragment.Edges);
        Assert.IsType<ReadOnlyCollection<CpgMethodSummary>>(fragment.MethodSummaries);
        Assert.IsType<ReadOnlyCollection<CpgBoundaryReference>>(fragment.BoundaryReferences);
        Assert.IsType<ReadOnlyCollection<CpgDiagnostic>>(fragment.Diagnostics);

        Assert.Throws<NotSupportedException>(() =>
            ((IList<CpgNodeDescriptor>)fragment.Nodes)[0] =
                CreateDescriptor(CreateAnchor(90, 95, StableNodeRole.SyntaxNode)));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<CpgEdgeCandidate>)fragment.Edges)[0] = CreateEdge(firstAnchor, firstAnchor));
        Assert.Throws<NotSupportedException>(() => ((IList<CpgDiagnostic>)fragment.Diagnostics).Clear());
    }

    /// <summary>
    /// 结构性所有权证据（不依赖分配计数器）：owned 路径持有的正是生产者交出的那个数组，
    /// 公开路径持有的是自己的副本。这条断言把「少一次复制」与「防御性复制仍在」同时钉住。
    /// </summary>
    [Fact]
    public void CreateOwned_WhenGivenProducerArrays_KeepsThoseExactArraysInsteadOfCopying()
    {
        var firstAnchor = CreateAnchor(10, 20, StableNodeRole.SyntaxNode);
        var secondAnchor = CreateAnchor(30, 40, StableNodeRole.SyntaxNode);
        var boundaryAnchor = CreateAnchor(50, 60, StableNodeRole.Symbol);

        var ownedNodes = OwnedNodes(firstAnchor, secondAnchor);
        var ownedEdges = OwnedEdges(firstAnchor, secondAnchor);
        var ownedSummaries = OwnedSummaries();
        var ownedBoundaries = OwnedBoundaries(boundaryAnchor);
        var ownedDiagnostics = OwnedDiagnostics();

        var owned = LocalCpgFragment.CreateOwned(
            1, "owned.cs", 0, ownedNodes, ownedEdges, ownedSummaries, ownedBoundaries,
            CpgFragmentMetrics.Empty, ownedDiagnostics);

        // owned：包装里就是生产者那一个数组实例，没有第二份副本。
        Assert.Same(ownedNodes, BackingArray<CpgNodeDescriptor>(owned.Nodes));
        Assert.Same(ownedEdges, BackingArray<CpgEdgeCandidate>(owned.Edges));
        Assert.Same(ownedSummaries, BackingArray<CpgMethodSummary>(owned.MethodSummaries));
        Assert.Same(ownedBoundaries, BackingArray<CpgBoundaryReference>(owned.BoundaryReferences));
        Assert.Same(ownedDiagnostics, BackingArray<CpgDiagnostic>(owned.Diagnostics));

        // public：传入同一批数组，包装里必须是别的实例（防御性复制未被削弱）。
        var publicNodes = OwnedNodes(firstAnchor, secondAnchor);
        var publicEdges = OwnedEdges(firstAnchor, secondAnchor);
        var publicSummaries = OwnedSummaries();
        var publicBoundaries = OwnedBoundaries(boundaryAnchor);
        var publicDiagnostics = OwnedDiagnostics();

        var viaPublic = new LocalCpgFragment(
            1, "public.cs", 0, publicNodes, publicEdges, publicSummaries, publicBoundaries,
            CpgFragmentMetrics.Empty, publicDiagnostics);

        Assert.NotSame(publicNodes, BackingArray<CpgNodeDescriptor>(viaPublic.Nodes));
        Assert.NotSame(publicEdges, BackingArray<CpgEdgeCandidate>(viaPublic.Edges));
        Assert.NotSame(publicSummaries, BackingArray<CpgMethodSummary>(viaPublic.MethodSummaries));
        Assert.NotSame(publicBoundaries, BackingArray<CpgBoundaryReference>(viaPublic.BoundaryReferences));
        Assert.NotSame(publicDiagnostics, BackingArray<CpgDiagnostic>(viaPublic.Diagnostics));
    }

    /// <summary>
    /// 取出 <see cref="ReadOnlyCollection{T}"/> 内部的承载数组，用于引用同一性断言。
    /// 只读包装必然由数组构造，故该字段存在；取出后仅用于 <c>Same</c>/<c>NotSame</c>。
    /// </summary>
    private static T[] BackingArray<T>(IReadOnlyList<T> readOnlyView)
    {
        var collection = Assert.IsType<ReadOnlyCollection<T>>(readOnlyView);
        var itemsField = typeof(ReadOnlyCollection<T>).GetField(
          "list",
          System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(itemsField);
        return Assert.IsType<T[]>(itemsField!.GetValue(collection));
    }

    /// <summary>
    /// CreateOwned 与公开构造器必须产出逐字段、逐顺序相同的 fragment：
    /// 同一份内容分别经两条入口构造，全部属性与枚举序比较相等。
    /// </summary>
    [Fact]
    public void CreateOwned_WhenGivenEquivalentCollections_MatchesPublicConstructor()
    {
        var firstAnchor = CreateAnchor(10, 20, StableNodeRole.SyntaxNode);
        var secondAnchor = CreateAnchor(30, 40, StableNodeRole.SyntaxNode);
        var boundaryAnchor = CreateAnchor(50, 60, StableNodeRole.Symbol);
        var metrics = new CpgFragmentMetrics(1, 2, 3, 4, 5, 6, 7, "truncated");

        var expected = new LocalCpgFragment(
            11,
            "sample.cs",
            3,
            new List<CpgNodeDescriptor>(OwnedNodes(firstAnchor, secondAnchor)),
            new List<CpgEdgeCandidate>(OwnedEdges(firstAnchor, secondAnchor)),
            new List<CpgMethodSummary>(OwnedSummaries()),
            new List<CpgBoundaryReference>(OwnedBoundaries(boundaryAnchor)),
            metrics,
            new List<CpgDiagnostic>(OwnedDiagnostics()));

        var actual = LocalCpgFragment.CreateOwned(
            11,
            "sample.cs",
            3,
            OwnedNodes(firstAnchor, secondAnchor),
            OwnedEdges(firstAnchor, secondAnchor),
            OwnedSummaries(),
            OwnedBoundaries(boundaryAnchor),
            metrics,
            OwnedDiagnostics());

        Assert.Equal(expected.BatchId, actual.BatchId);
        Assert.Equal(expected.SourceFilePath, actual.SourceFilePath);
        Assert.Equal(expected.StableOrder, actual.StableOrder);
        Assert.Equal(expected.Nodes.ToArray(), actual.Nodes.ToArray());
        Assert.Equal(expected.Edges.ToArray(), actual.Edges.ToArray());
        Assert.Equal(expected.MethodSummaries.ToArray(), actual.MethodSummaries.ToArray());
        Assert.Equal(expected.BoundaryReferences.ToArray(), actual.BoundaryReferences.ToArray());
        Assert.Equal(expected.Metrics, actual.Metrics);
        Assert.Equal(expected.Diagnostics.ToArray(), actual.Diagnostics.ToArray());
    }

    /// <summary>
    /// 两条入口必须报同一种异常、同一个参数名：校验逻辑只有一份。
    /// </summary>
    [Theory]
    [InlineData("negative-batch-id")]
    [InlineData("negative-stable-order")]
    [InlineData("blank-source-file-path")]
    [InlineData("null-nodes")]
    [InlineData("null-edges")]
    [InlineData("null-summaries")]
    [InlineData("null-boundaries")]
    [InlineData("null-metrics")]
    [InlineData("null-diagnostics")]
    [InlineData("unresolved-edge-endpoint")]
    public void CreateOwned_WhenInputIsInvalid_ReportsSameExceptionAsPublicConstructor(string scenario)
    {
        var publicFailure = Record.Exception(() => BuildInvalid(scenario, owned: false));
        var ownedFailure = Record.Exception(() => BuildInvalid(scenario, owned: true));

        Assert.NotNull(publicFailure);
        Assert.NotNull(ownedFailure);
        Assert.Equal(publicFailure.GetType(), ownedFailure.GetType());
        Assert.Equal(
            (publicFailure as ArgumentException)?.ParamName,
            (ownedFailure as ArgumentException)?.ParamName);
    }

    /// <summary>
    /// 接管失败不得改动生产者数组：异常抛出后数组内容与所有权仍在调用方手里。
    /// </summary>
    [Fact]
    public void CreateOwned_WhenValidationFails_LeavesProducerArraysUnchanged()
    {
        var source = CreateAnchor(10, 20, StableNodeRole.SyntaxNode);
        var unresolvedTarget = CreateAnchor(30, 40, StableNodeRole.Symbol);
        var nodes = new[] { CreateDescriptor(source) };
        var edges = new[] { CreateEdge(source, unresolvedTarget) };

        var failure = Record.Exception(() => LocalCpgFragment.CreateOwned(
            1,
            "sample.cs",
            0,
            nodes,
            edges,
            Array.Empty<CpgMethodSummary>(),
            Array.Empty<CpgBoundaryReference>(),
            CpgFragmentMetrics.Empty,
            Array.Empty<CpgDiagnostic>()));

        Assert.IsType<ArgumentException>(failure);
        Assert.Equal("edges", ((ArgumentException)failure!).ParamName);
        Assert.Single(nodes);
        Assert.Single(edges);
        Assert.Equal(source, nodes[0].Anchor);
        Assert.Equal(unresolvedTarget, edges[0].TargetAnchor);
    }

    /// <summary>
    /// 接管路径必须省掉第二份节点/边载荷数组：同一份内容分别经两条入口构造，
    /// owned 路径的线程累计分配应恰好少掉公开路径省不掉的那两次 <c>ToArray</c>。
    /// </summary>
    /// <remarks>
    /// 三条非载荷集合传共享的 <see cref="Array.Empty{T}"/>（其 <c>ToArray</c> 实测零分配），
    /// 于是两条入口的差异只剩 nodes/edges。期望值不是猜的常数，而是**同运行时实测**的
    /// 两次 <c>ToArray</c> 成本，故本用例同时锁住「省下的正是载荷副本」与「没省到别的」。
    /// </remarks>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(64, 48)]
    [InlineData(2048, 1536)]
    public void CreateOwned_ComparedToPublicConstructor_AllocatesExactlyOneLessPayloadCopy(
      int nodeCount,
      int edgeCount)
    {
        // 载荷数组预先创建：测量窗口内只有构造本身，不把建数组的成本算进去。
        var nodes = new CpgNodeDescriptor[nodeCount];
        for (var index = 0; index < nodeCount; index++)
        {
            nodes[index] = CreateDescriptor(CreateAnchor(index * 2, index * 2 + 1, StableNodeRole.SyntaxNode));
        }

        // 无本地节点时不存在合法边端点，故该理论数据必须成对为零。
        Assert.True(
          nodeCount > 0 || edgeCount == 0,
          $"无节点却要求 {edgeCount} 条边：不存在合法端点，该组合无意义。");

        var edges = new CpgEdgeCandidate[edgeCount];
        for (var index = 0; index < edgeCount; index++)
        {
            // 端点全部取自本地节点（自环也合法）：这样不必传 boundary 引用，
            // 两条入口的差异就严格只剩 nodes/edges 两份数组。
            edges[index] = CreateEdge(
              nodes[index % nodeCount].Anchor,
              nodes[(index + 1) % nodeCount].Anchor);
        }

        // 非载荷集合传共享空数组，使两条入口的差异只剩 nodes/edges。
        var summaries = Array.Empty<CpgMethodSummary>();
        var boundaries = Array.Empty<CpgBoundaryReference>();
        var diagnostics = Array.Empty<CpgDiagnostic>();

        // 预热两条路径，排除首次 JIT 与静态初始化。
        _ = Construct(nodes, edges, summaries, boundaries, diagnostics, owned: false);
        _ = Construct(nodes, edges, summaries, boundaries, diagnostics, owned: true);

        var publicBytes = MeasureThreadAllocation(
          () => _ = Construct(nodes, edges, summaries, boundaries, diagnostics, owned: false));
        var ownedBytes = MeasureThreadAllocation(
          () => _ = Construct(nodes, edges, summaries, boundaries, diagnostics, owned: true));
        var savedBytes = publicBytes - ownedBytes;

        // 公开路径省不掉的两次复制：用同一运行时的同一入口实测，而不是按宽度推导。
        var copiedNodeBytes =
          MeasureThreadAllocation(() => _ = ((IReadOnlyList<CpgNodeDescriptor>)nodes).ToArray());
        var copiedEdgeBytes =
          MeasureThreadAllocation(() => _ = ((IReadOnlyList<CpgEdgeCandidate>)edges).ToArray());
        var publicCopyBytes = copiedNodeBytes + copiedEdgeBytes;

        var descriptorWidth = Unsafe.SizeOf<CpgNodeDescriptor>();
        var candidateWidth = Unsafe.SizeOf<CpgEdgeCandidate>();
        var payloadBytes =
          (long)nodeCount * descriptorWidth + (long)edgeCount * candidateWidth;

        _output.WriteLine(
          $"nodes={nodeCount}; edges={edgeCount}; descriptor-width={descriptorWidth}; " +
          $"candidate-width={candidateWidth}; payload={payloadBytes}; " +
          $"copied-nodes={copiedNodeBytes}; copied-edges={copiedEdgeBytes}; " +
          $"public-copy-bytes={publicCopyBytes}; public-allocated={publicBytes}; " +
          $"owned-allocated={ownedBytes}; saved={savedBytes}");

        // 判据一：省下的正好是那两次载荷复制，没有多也没有少。
        Assert.Equal(publicCopyBytes, savedBytes);

        // 判据二：省下的必须覆盖整个节点/边载荷本体，而不只是数组头。
        Assert.True(
          savedBytes >= payloadBytes,
          $"owned 路径未省下完整载荷副本：saved={savedBytes} payload={payloadBytes}");

        // 判据三：复制成本随载荷线性增长，且空载荷复制实测零分配。
        if (nodeCount > 0)
        {
          Assert.True(
            copiedNodeBytes >= (long)nodeCount * descriptorWidth,
            $"节点数组复制未承载完整载荷：copied={copiedNodeBytes} " +
            $"expected>= {(long)nodeCount * descriptorWidth}");
        }
        else
        {
          Assert.Equal(0, copiedNodeBytes);
        }
    }

    private static LocalCpgFragment Construct(
      CpgNodeDescriptor[] nodes,
      CpgEdgeCandidate[] edges,
      CpgMethodSummary[] summaries,
      CpgBoundaryReference[] boundaries,
      CpgDiagnostic[] diagnostics,
      bool owned)
    {
        return owned
          ? LocalCpgFragment.CreateOwned(
            1, "alloc.cs", 0, nodes, edges, summaries, boundaries, CpgFragmentMetrics.Empty, diagnostics)
          : new LocalCpgFragment(
            1, "alloc.cs", 0, nodes, edges, summaries, boundaries, CpgFragmentMetrics.Empty, diagnostics);
    }

    private static long MeasureThreadAllocation(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static LocalCpgFragment BuildInvalid(string scenario, bool owned)
    {
        var source = CreateAnchor(10, 20, StableNodeRole.SyntaxNode);
        var nodes = new[] { CreateDescriptor(source) };
        var edges = Array.Empty<CpgEdgeCandidate>();
        var summaries = Array.Empty<CpgMethodSummary>();
        var boundaries = Array.Empty<CpgBoundaryReference>();
        var diagnostics = Array.Empty<CpgDiagnostic>();
        var metrics = CpgFragmentMetrics.Empty;
        long batchId = 1;
        var sourceFilePath = "sample.cs";
        var stableOrder = 0;

        switch (scenario)
        {
            case "negative-batch-id":
                batchId = -1;
                break;
            case "negative-stable-order":
                stableOrder = -1;
                break;
            case "blank-source-file-path":
                sourceFilePath = "   ";
                break;
            case "null-nodes":
                nodes = null!;
                break;
            case "null-edges":
                edges = null!;
                break;
            case "null-summaries":
                summaries = null!;
                break;
            case "null-boundaries":
                boundaries = null!;
                break;
            case "null-metrics":
                metrics = null!;
                break;
            case "null-diagnostics":
                diagnostics = null!;
                break;
            case "unresolved-edge-endpoint":
                edges = new[] { CreateEdge(source, CreateAnchor(30, 40, StableNodeRole.Symbol)) };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "未知场景。");
        }

        return owned
          ? LocalCpgFragment.CreateOwned(
            batchId, sourceFilePath, stableOrder, nodes, edges, summaries, boundaries, metrics, diagnostics)
          : new LocalCpgFragment(
            batchId, sourceFilePath, stableOrder, nodes, edges, summaries, boundaries, metrics, diagnostics);
    }

    private static CpgNodeDescriptor[] OwnedNodes(StableNodeAnchor first, StableNodeAnchor second)
    {
        return new[] { CreateDescriptor(first), CreateDescriptor(second) };
    }

    private static CpgEdgeCandidate[] OwnedEdges(StableNodeAnchor first, StableNodeAnchor second)
    {
        return new[] { CreateEdge(first, second) };
    }

    private static CpgMethodSummary[] OwnedSummaries()
    {
        return new[] { new CpgMethodSummary("M:First", 1, 2) };
    }

    private static CpgBoundaryReference[] OwnedBoundaries(StableNodeAnchor anchor)
    {
        return new[] { CreateBoundary(anchor, "external:Target") };
    }

    private static CpgDiagnostic[] OwnedDiagnostics()
    {
        return new[] { new CpgDiagnostic("CPG001", "first") };
    }

    private static CpgBoundaryReference CreateBoundary(StableNodeAnchor anchor, string reference)
    {
        return new CpgBoundaryReference(anchor, reference, CpgBoundaryReferenceKind.External, IsAvailable: false);
    }

    private static CpgEdgeCandidate CreateEdge(StableNodeAnchor source, StableNodeAnchor target)
    {
        return new CpgEdgeCandidate(
            source,
            target,
            NLCPGEdgeKind.OpResolvesToSymbol,
            StructuredLabel: null,
            ContextId: null,
            CallSiteContext: null);
    }

    private static CpgEdgeCandidate CreateBoundaryEdge(StableNodeAnchor source, StableNodeAnchor target)
    {
        return CreateEdge(source, target);
    }

    private static LocalCpgFragment CreateFragment(long batchId, int stableOrder)
    {
        var anchor = CreateAnchor(stableOrder, stableOrder + 1, StableNodeRole.SyntaxNode);
        return new LocalCpgFragment(
            batchId,
            "sample.cs",
            stableOrder,
            new[] { CreateDescriptor(anchor) },
            Array.Empty<CpgEdgeCandidate>(),
            Array.Empty<CpgMethodSummary>(),
            Array.Empty<CpgBoundaryReference>(),
            CpgFragmentMetrics.Empty,
            Array.Empty<CpgDiagnostic>());
    }

    private static CpgNodeDescriptor CreateDescriptor(StableNodeAnchor anchor)
    {
        return new CpgNodeDescriptor(
            anchor,
            NLCPGNodeKind.SyntaxNode,
            NameId: 0,
            FullNameId: 0,
            SignatureId: 0,
            DispatchKind: null,
            TypeFullNameId: 0,
            FilePathId: 0,
            SpanStart: anchor.SpanStart,
            SpanEnd: anchor.SpanEnd,
            IsImplicit: false);
    }

    private static StableNodeAnchor CreateAnchor(int start, int end, StableNodeRole role)
    {
        return new StableNodeAnchor(
            NLCPGNodeKind.SyntaxNode,
            FilePathId: 1,
            start,
            end,
            role,
            Ordinal: start,
            ExtraKeyId: 0);
    }
}
