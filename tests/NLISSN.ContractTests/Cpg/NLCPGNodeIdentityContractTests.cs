using System.Reflection;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.Tests;

// G1：固化「节点身份折叠」契约（阶段 2/3 的前置门禁）。
//
// 阶段 2/3 计划把待降键字典的键从 104 B 的 `NLCPGNode` 降到 28 B 的 `StableNodeAnchor`。
// 该降键只有在「图内节点必带锚点」且「锚点相等即节点相等」时才语义等价。
// 本文件锁定这两个前提，并显式记录 `StableNodeAnchor` 不覆盖哪些 `NLCPGNode` 字段
// ——那是既有的身份语义（由 `NLCPGNode.Equals` 决定），不是降键引入的新风险。
public sealed class NLCPGNodeIdentityContractTests
{
  // 白名单前提：任何经 AddNode 进入可变图的节点都必须带 StableAnchor。
  [Fact]
  public void AddNode_EveryMaterializedNode_CarriesStableAnchor()
  {
    var graph = new NLCPGGraph();
    var returned = new[]
    {
      graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "alpha")),
      graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Method, Name: "beta")),
      graph.AddNode(new NLCPGNodeDraft(
        NLCPGNodeKind.SyntaxNode,
        Name: "gamma",
        FilePath: "identity.cs",
        SpanStart: 0,
        SpanEnd: 4)),
    };

    Assert.All(returned, node => Assert.NotNull(node.StableAnchor));

    // 先断言非空，否则 Assert.All 会在空集合上静默通过（假绿）。
    var materialized = MutableNodes(graph).ToArray();
    Assert.Equal(3, materialized.Length);
    Assert.All(
      materialized,
      node => Assert.True(
        node.StableAnchor.HasValue,
        "降键前提被破坏：图中存在没有 StableAnchor 的节点。"));
  }

  // StableNodeAnchor 的字段集是降键契约的一部分；漏掉任一字段都会改变节点身份。
  [Fact]
  public void StableNodeAnchor_FieldSet_ExcludesThreeNodeFields()
  {
    var names = typeof(StableNodeAnchor)
      .GetProperties(BindingFlags.Instance | BindingFlags.Public)
      .Select(property => property.Name)
      .ToHashSet(StringComparer.Ordinal);

    Assert.Equal(
      new[] { "ExtraKeyId", "FilePathId", "Kind", "Ordinal", "Role", "SpanEnd", "SpanStart" },
      names.OrderBy(name => name, StringComparer.Ordinal));

    // 这三个 NLCPGNode 字段不在锚点内 ⇒ 它们不参与「有锚点节点」的身份比较。
    Assert.DoesNotContain("DispatchKind", names);
    Assert.DoesNotContain("TypeFullNameId", names);
    Assert.DoesNotContain("IsImplicit", names);
  }

  // 任一方带锚点就只比锚点：这是 AddNode 去重与降键等价性的共同基础。
  [Fact]
  public void NodeEquals_WhenEitherSideCarriesAnchor_ComparesAnchorsOnly()
  {
    var anchor = new StableNodeAnchor(
      NLCPGNodeKind.Operation,
      FilePathId: 3,
      SpanStart: 1,
      SpanEnd: 9,
      StableNodeRole.Operation,
      Ordinal: 0,
      ExtraKeyId: 42);

    var anchored = new NLCPGNode(NLCPGNodeKind.Operation, StableAnchor: anchor);
    var sameAnchorDifferentText = new NLCPGNode(
      NLCPGNodeKind.Operation,
      NameId: 999,
      StableAnchor: anchor);
    var noAnchor = new NLCPGNode(NLCPGNodeKind.Operation, NameId: 999);

    Assert.True(anchored.Equals(sameAnchorDifferentText));

    // 无锚点节点与有锚点节点永不相等（否则降键会误合并）。
    Assert.False(anchored.Equals(noAnchor));
    Assert.False(noAnchor.Equals(anchored));
  }

  // 推翻「Ordinal 恒为 0 ⇒ 任意碰撞」的假设：ExtraKeyId 仍是有效区分字段。
  [Fact]
  public void CreateFallback_DistinctNameIds_ProduceDistinctAnchors()
  {
    var first = StableNodeAnchor.CreateFallback(
      new NLCPGNode(NLCPGNodeKind.Operation, NameId: 11, FilePathId: 7, SpanStart: 0, SpanEnd: 5),
      StableNodeRole.Operation);
    var second = StableNodeAnchor.CreateFallback(
      new NLCPGNode(NLCPGNodeKind.Operation, NameId: 12, FilePathId: 7, SpanStart: 0, SpanEnd: 5),
      StableNodeRole.Operation);

    // Ordinal 确实被硬编码为 0 —— 记录该事实，但它是安全的。
    Assert.Equal(0, first.Ordinal);
    Assert.Equal(0, second.Ordinal);

    Assert.Equal(11u, first.ExtraKeyId);
    Assert.Equal(12u, second.ExtraKeyId);
    Assert.NotEqual(first, second);
  }

  // 集成层面复核：不同名字的节点不得被折叠成一个。
  [Fact]
  public void AddNode_DistinctNames_DoNotCollapseIntoOneNode()
  {
    var graph = new NLCPGGraph();
    graph.AddNode(new NLCPGNodeDraft(
      NLCPGNodeKind.Operation,
      Name: "left",
      FilePath: "identity.cs",
      SpanStart: 0,
      SpanEnd: 5));
    graph.AddNode(new NLCPGNodeDraft(
      NLCPGNodeKind.Operation,
      Name: "right",
      FilePath: "identity.cs",
      SpanStart: 0,
      SpanEnd: 5));

    Assert.Equal(2, MutableNodeCount(graph));
  }

  // ExtraKeyId 的解析优先级：FullNameId → SignatureId → NameId。
  [Fact]
  public void CreateFallback_ExtraKeyId_FollowsFullNameSignatureNamePriority()
  {
    var allThree = StableNodeAnchor.CreateFallback(
      new NLCPGNode(NLCPGNodeKind.Method, NameId: 300, FullNameId: 100, SignatureId: 200),
      StableNodeRole.Method);
    var withoutFullName = StableNodeAnchor.CreateFallback(
      new NLCPGNode(NLCPGNodeKind.Method, NameId: 300, SignatureId: 200),
      StableNodeRole.Method);
    var nameOnly = StableNodeAnchor.CreateFallback(
      new NLCPGNode(NLCPGNodeKind.Method, NameId: 300),
      StableNodeRole.Method);

    Assert.Equal(100u, allThree.ExtraKeyId);
    Assert.Equal(200u, withoutFullName.ExtraKeyId);
    Assert.Equal(300u, nameOnly.ExtraKeyId);
  }

  // 这条记录的是**既有语义**（非降键引入）：锚点不覆盖 IsImplicit，
  // 因此仅 IsImplicit 不同的两个草稿会被合并，并按 MergeNode 取「或」。
  [Fact]
  public void AddNode_DifferingOnlyInIsImplicit_FoldsToSingleMergedNode()
  {
    var graph = new NLCPGGraph();
    var explicitNode = graph.AddNode(new NLCPGNodeDraft(
      NLCPGNodeKind.Operation,
      Name: "same",
      FilePath: "identity.cs",
      SpanStart: 0,
      SpanEnd: 5,
      IsImplicit: false));
    var implicitNode = graph.AddNode(new NLCPGNodeDraft(
      NLCPGNodeKind.Operation,
      Name: "same",
      FilePath: "identity.cs",
      SpanStart: 0,
      SpanEnd: 5,
      IsImplicit: true));

    Assert.Equal(1, MutableNodeCount(graph));
    Assert.Equal(explicitNode.StableAnchor, implicitNode.StableAnchor);

    // MergeNode 对 IsImplicit 取「或」⇒ 合并结果为 true。
    Assert.True(implicitNode.IsImplicit);
  }

  // 对照：差异落在锚点覆盖的字段上时，绝不折叠。
  [Fact]
  public void AddNode_AnchorFieldDifferences_KeepNodesDistinct()
  {
    var graph = new NLCPGGraph();
    graph.AddNode(new NLCPGNodeDraft(
      NLCPGNodeKind.Operation,
      Name: "same",
      FilePath: "identity.cs",
      SpanStart: 0,
      SpanEnd: 5));
    graph.AddNode(new NLCPGNodeDraft(
      NLCPGNodeKind.Operation,
      Name: "same",
      FilePath: "identity.cs",
      SpanStart: 6,
      SpanEnd: 11));

    Assert.Equal(2, MutableNodeCount(graph));
  }

  // ── B2：AddNode 命中锚点时的「冗余写回短路」───────────────────────────────
  //
  // 背景：`AddNode` 命中锚点后无条件 `MergeNode` + 写回 `_nodesByOrdinal`。
  // 发布段每条跨过程边都会走到这里，端点取自已物化的冻结边快照池 ⇒ 锚点必然命中。
  // 故加了一条「merged 与 existing 逐字段相同则跳过写回」的短路。
  //
  // ⚠️ 该短路的**失效方向不对称**，故下面两条必须成对：
  //   · 该跳而未跳（N6）只是白干，无正确性风险；
  //   · **[不该跳而跳了]会静默丢字段**——图被改坏且不抛异常。
  // 后者才是本项的真实风险，故 N6-b（补全必须落盘）比 N6-a 更重要。
  //
  // ⚠️ 断言一律**逐字段**比较，**不得**用 `NLCPGNode.Equals`：
  // 两端都带 StableAnchor 时它只比锚点（NLCPGNode.cs:20-26），恒真 ⇒ 假绿。
  // 那正是本次短路【不能】复用它做判据的同一理由。

  // N6-a：同锚点重放同一节点 ⇒ 图内节点逐字段不变，且短路确实被走到。
  [Fact]
  public void AddNode_RepeatedIdenticalNode_LeavesNodeFieldIdentical()
  {
    var graph = new NLCPGGraph();
    var anchor = new StableNodeAnchor(
      NLCPGNodeKind.Method,
      FilePathId: 77,
      SpanStart: 10,
      SpanEnd: 40,
      StableNodeRole.Method,
      Ordinal: 0,
      ExtraKeyId: 99);

    var draft = new NLCPGNodeDraft(
      NLCPGNodeKind.Method,
      Name: "Repeated",
      FullName: "Sample.Repeated",
      FilePath: "repeated.cs",
      SpanStart: 10,
      SpanEnd: 40);

    graph.AddNode(draft, stableAnchor: anchor);
    var fieldsAfterFirst = DescribeNode(Assert.Single(MutableNodes(graph)));
    var skipsBefore = graph.RedundantMergeSkipCount;

    // 重放：锚点相同、内容相同。
    graph.AddNode(draft, stableAnchor: anchor);

    Assert.Equal(1, MutableNodeCount(graph));
    Assert.Equal(fieldsAfterFirst, DescribeNode(Assert.Single(MutableNodes(graph))));

    // 排除"短路是死代码"：它必须真的被执行过。
    Assert.True(
      graph.RedundantMergeSkipCount > skipsBefore,
      "重放同锚点同内容节点应命中冗余写回短路，但跳过计数没有增长。");
  }

  // N6-b（**关键**）：候选补全了 existing 尚缺的字段 ⇒ 合并结果**必须**写回。
  //
  // 这是那次短路的危险方向：若判据写错（例如误用 NLCPGNode.Equals，
  // 或漏比了某个字段），补全会无声丢失，而 N6-a 仍然全绿。
  [Fact]
  public void AddNode_SameAnchorWithAdditionalFields_PersistsEnrichment()
  {
    var graph = new NLCPGGraph();
    var anchor = new StableNodeAnchor(
      NLCPGNodeKind.Method,
      FilePathId: 77,
      SpanStart: 10,
      SpanEnd: 40,
      StableNodeRole.Method,
      Ordinal: 0,
      ExtraKeyId: 99);

    // 先入图：只有 Name，其余可合并字段为空/0。
    graph.AddNode(
      new NLCPGNodeDraft(NLCPGNodeKind.Method, Name: "Only", FilePath: "enrich.cs", SpanStart: 10, SpanEnd: 40),
      stableAnchor: anchor);

    // 再入图：同锚点，但带来 FullName 与 Signature。
    var enriched = graph.AddNode(
      new NLCPGNodeDraft(
        NLCPGNodeKind.Method,
        FullName: "Sample.Enriched",
        Signature: "void Enriched()",
        FilePath: "enrich.cs",
        SpanStart: 10,
        SpanEnd: 40,
        IsImplicit: true),
      stableAnchor: anchor);

    Assert.Equal(1, MutableNodeCount(graph));

    // 补全必须同时体现在【返回值】与【图内存放的节点】上。
    var stored = Assert.Single(MutableNodes(graph));
    Assert.Equal(DescribeNode(enriched), DescribeNode(stored));

    Assert.NotNull(graph.ResolveFullName(stored));
    Assert.Equal("Sample.Enriched", graph.ResolveFullName(stored));
    Assert.Equal("void Enriched()", graph.ResolveSignature(stored));

    // 原始 Name 不得被候选的空 NameId 冲掉（MergeNode 的"非空才覆盖"语义）。
    Assert.Equal("Only", graph.ResolveName(stored));

    // IsImplicit 取「或」——既有语义（见 AddNode_DifferingOnlyInIsImplicit_FoldsToSingleMergedNode）。
    Assert.True(stored.IsImplicit);
  }

  // N6-c：多次交错重放（含补全）后，图内节点仍等于最后应得的状态。
  // 守住"短路有状态"这一类错误——例如判据依赖了某次已改动的快照。
  [Fact]
  public void AddNode_InterleavedIdenticalAndEnrichingReplays_ConvergeToEnrichedState()
  {
    var graph = new NLCPGGraph();
    var anchor = new StableNodeAnchor(
      NLCPGNodeKind.Method,
      FilePathId: 77,
      SpanStart: 10,
      SpanEnd: 40,
      StableNodeRole.Method,
      Ordinal: 0,
      ExtraKeyId: 99);

    var bare = new NLCPGNodeDraft(
      NLCPGNodeKind.Method, Name: "Bare", FilePath: "conv.cs", SpanStart: 10, SpanEnd: 40);

    graph.AddNode(bare, stableAnchor: anchor);
    graph.AddNode(bare, stableAnchor: anchor);
    graph.AddNode(
      new NLCPGNodeDraft(
        NLCPGNodeKind.Method, FullName: "Sample.Converged", FilePath: "conv.cs", SpanStart: 10, SpanEnd: 40),
      stableAnchor: anchor);
    graph.AddNode(bare, stableAnchor: anchor);
    graph.AddNode(bare, stableAnchor: anchor);

    Assert.Equal(1, MutableNodeCount(graph));
    var stored = Assert.Single(MutableNodes(graph));
    Assert.Equal("Sample.Converged", graph.ResolveFullName(stored));
    Assert.Equal("Bare", graph.ResolveName(stored));
  }

  // N6-d（收益机制在【真实发布路径】上成立）：构造一份带跨过程桥的最小源码，
  // 断言短路计数 > 0。
  //
  // 为什么必须单独有这一条（N6-a..c 都不覆盖它）：
  // N6-a..c 全部用手写 AddNode 序列，证明的是"判据写对了"。但 B2 的**收益机制**
  // 是"发布段每条桥边都会命中同锚点且无变化的重放"——那是**关于真实语料的断言**，
  // 手写序列无法证明。若真实路径从不命中，短路就是死代码而 N6-a..c 仍然全绿。
  //
  // ⚠️ 这条断言的是**机制存在**，不是收益大小。计数 > 0 只说明"短路确实被执行过"；
  // 省下多少分配/多少纳秒，本文件不主张（本机墙钟无判别力，见计划 §7.1）。
  [Fact]
  public void BuildFromSource_InterproceduralPublish_ActuallyHitsRedundantMergeShortCircuit()
  {
    const string source = """
      public sealed class BridgeSample
      {
        public int Produce(int value)
        {
          return value + 1;
        }

        public int Consume(int value)
        {
          return Produce(value);
        }
      }
      """;

    var graph = new NLCPGBuilder(NLCPGBuilderOptions.CreateDefault() with
    {
      MaxDegreeOfParallelism = 1,
      RequestedCapabilities = new[] { NLCPGCapability.All },
    }).BuildFromSource(source, "bridge-hotpath.cs");

    // 先钉住夹具真的产出了图与边，否则下面的计数可能因"根本没跑"而假绿。
    Assert.NotEmpty(graph.Nodes);
    Assert.NotEmpty(graph.Edges);

    Assert.True(
      graph.RedundantMergeSkipCount > 0,
      "跨过程发布路径应命中冗余写回短路（否则 B2 的收益机制在真实语料上不成立）。"
      + $"实际计数 = {graph.RedundantMergeSkipCount}。");

    // 本轮实测（本夹具、DOP=1）：edges=245、nodes=101、RedundantMergeSkipCount=546，
    // 约 2.2 次/边——与 §1.2 的"每条边 2 次 AddNode（source+target）"逐一对应。
    //
    // 断言刻意只取 `> 0` 而非那个比值：若将来落地 B1（按序数直通、完全绕过 AddNode），
    // 本计数会自然降到 0，届时这条会红并迫使改动者显式更新护栏——这是期望行为，
    // 而不是把断言锁死在某个实测比值上、让它对无关改动过敏。
  }

  private static int MutableNodeCount(NLCPGGraph graph)
  {
    return graph.Nodes.Count;
  }

  private static IEnumerable<NLCPGNode> MutableNodes(NLCPGGraph graph)
  {
    return graph.Nodes;
  }

  // 逐字段快照。刻意手写全部 12 个字段而非用 record 的 ToString/Equals：
  // 前者会走被重写的 Equals（锚点短路），后者在字段增删时不会失败。
  private static string DescribeNode(NLCPGNode node)
  {
    return string.Join(
      '|',
      node.Kind,
      node.NameId,
      node.FullNameId,
      node.SignatureId,
      node.DispatchKind?.ToString() ?? "-",
      node.TypeFullNameId,
      node.FilePathId,
      node.SpanStart?.ToString() ?? "-",
      node.SpanEnd?.ToString() ?? "-",
      node.IsImplicit,
      node.NodeId?.Value.ToString() ?? "-",
      node.StableAnchor?.ToString() ?? "-");
  }
}
