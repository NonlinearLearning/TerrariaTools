using System.Reflection;
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

  private static int MutableNodeCount(NLCPGGraph graph)
  {
    return graph.Nodes.Count;
  }

  private static IEnumerable<NLCPGNode> MutableNodes(NLCPGGraph graph)
  {
    return graph.Nodes;
  }
}
