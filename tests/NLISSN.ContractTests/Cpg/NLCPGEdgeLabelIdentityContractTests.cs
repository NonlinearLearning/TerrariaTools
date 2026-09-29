using NLCPG.Contracts;
using NLCPG.Model;
using Xunit;

namespace RoslynPrototype.Tests;

// A 项：跨过程桥标签的单例化契约。
//
// 背景：`NLCPGBuilder` 的发布循环对**每条**跨过程桥边调用
// `NLCPGEdgeLabel.ForInterproceduralBridge(BridgeKindOf(edge))`，而该值域只有 3 个取值。
// 原先工厂是无条件 `new`，故每条边都付一次分配；这些实例除每个元数据条目保留的首个
// 之外全部即弃（pending 元数据池按值去重），属纯冗余分配。
//
// 本文件锁定单例化的两条性质。**注意**：这两条只证明"共享成立"（A 的实现正确），
// **不证明** A 带来收益——若上游本就把标签去过重，则本条收益为零。
// 收益需在产物侧数分配次数，不在本文件范围。
public sealed class NLCPGEdgeLabelIdentityContractTests
{
  // N4：同一种桥种类，任意次调用必须返回**同一实例**。
  // 这是单例化的全部内容；用 ReferenceEquals 而非相等性，后者在改动前也恒真（假绿）。
  [Theory]
  [InlineData(NLCPGInterproceduralBridgeKind.ArgumentToParameter)]
  [InlineData(NLCPGInterproceduralBridgeKind.ReturnToMethodReturn)]
  [InlineData(NLCPGInterproceduralBridgeKind.MethodReturnToCallResult)]
  public void ForInterproceduralBridge_SameKind_ReturnsSameInstance(
    NLCPGInterproceduralBridgeKind kind)
  {
    var first = NLCPGEdgeLabel.ForInterproceduralBridge(kind);
    var second = NLCPGEdgeLabel.ForInterproceduralBridge(kind);

    Assert.Same(first, second);
  }

  // N4 的直接形态：工厂返回的实例**就是**那个静态单例字段本身。
  // 上面那条只证明"两次调用返回同一对象"（也兼容"每次返回同一个新对象"这类退化），
  // 这条把"确实命中了静态字段"钉死，从而把"缓存被绕过"与"缓存生效"分开。
  [Fact]
  public void ForInterproceduralBridge_ReturnsTheStaticSingletonFields()
  {
    Assert.Same(
      NLCPGEdgeLabel.ArgumentToParameterBridge,
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.ArgumentToParameter));
    Assert.Same(
      NLCPGEdgeLabel.ReturnToMethodReturnBridge,
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.ReturnToMethodReturn));
    Assert.Same(
      NLCPGEdgeLabel.MethodReturnToCallResultBridge,
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.MethodReturnToCallResult));
  }

  // N5：三种可单例的桥种类必须给出**互不相同**的实例。
  //
  // 单例化最危险的失效形态是"三个字段指向同一个实例"——此时 N4 仍然全绿，
  // 但图里三种桥会被折叠成一种，且 `StableKey` 也全部相同。
  // 故必须与"两两不同"配对断言，否则 N4 单独成立不构成证据。
  [Fact]
  public void ForInterproceduralBridge_DistinctKinds_ReturnDistinctInstances()
  {
    var labels = new[]
    {
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.ArgumentToParameter),
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.ReturnToMethodReturn),
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.MethodReturnToCallResult),
    };

    // 先断言非空，否则 ReferenceEquals 在 null 上会给出误导性的"不同"。
    Assert.All(labels, label => Assert.NotNull(label));
    Assert.All(labels, label => Assert.False(string.IsNullOrEmpty(label.StableKey)));

    for (var i = 0; i < labels.Length; i++)
    {
      for (var j = i + 1; j < labels.Length; j++)
      {
        Assert.NotSame(labels[i], labels[j]);
        Assert.NotEqual(labels[i].StableKey, labels[j].StableKey);
      }
    }
  }

  // 静态单例字段自身也必须两两不同、且各携带正确的 kind。
  // 这条独立于工厂：即便工厂被改成不经字段，也能发现"三个字段退化成同一个"。
  [Fact]
  public void StaticSingletonFields_ArePairwiseDistinctAndCarryTheirOwnKind()
  {
    var byField = new (NLCPGEdgeLabel Label, NLCPGInterproceduralBridgeKind Kind)[]
    {
      (NLCPGEdgeLabel.ArgumentToParameterBridge, NLCPGInterproceduralBridgeKind.ArgumentToParameter),
      (NLCPGEdgeLabel.ReturnToMethodReturnBridge, NLCPGInterproceduralBridgeKind.ReturnToMethodReturn),
      (NLCPGEdgeLabel.MethodReturnToCallResultBridge, NLCPGInterproceduralBridgeKind.MethodReturnToCallResult),
    };

    Assert.All(byField, entry => Assert.Equal(entry.Kind, entry.Label.InterproceduralBridgeKind));

    // 若某个单例被初始化成 null（或全部指向同一实例），断言必须红。
    Assert.All(byField, entry => Assert.NotNull(entry.Label));
    Assert.Equal(3, byField.Select(entry => entry.Label).Distinct(ReferenceEqualityComparer.Instance).Count());
  }

  // 单例化不得改变标签自身的可观测内容：桥种类必须原样保留。
  // 若有人"顺手"把三个单例初始化成同一个 kind，N5 会挂，但这条把失败点指到具体字段。
  [Theory]
  [InlineData(NLCPGInterproceduralBridgeKind.ArgumentToParameter, "interprocedural-bridge:ArgumentToParameter")]
  [InlineData(NLCPGInterproceduralBridgeKind.ReturnToMethodReturn, "interprocedural-bridge:ReturnToMethodReturn")]
  [InlineData(NLCPGInterproceduralBridgeKind.MethodReturnToCallResult, "interprocedural-bridge:MethodReturnToCallResult")]
  public void ForInterproceduralBridge_PreservesKindAndStableKey(
    NLCPGInterproceduralBridgeKind kind,
    string expectedStableKey)
  {
    var label = NLCPGEdgeLabel.ForInterproceduralBridge(kind);

    Assert.Equal(kind, label.InterproceduralBridgeKind);
    // 桥标签不得被误标成决策关系（构造函数的"只能是一种"约束的另一面）。
    Assert.Null(label.DecisionRelationKind);
    Assert.Null(label.FlowSummaryResolution);
    Assert.Equal(expectedStableKey, label.StableKey);
  }

  // 单例化必须**不**波及 SummaryMapping：它走 ForFlowSummaryBridge，标签还带
  // 分辨率/方法键/两端点，取值空间不是常数。这条钉住"未列举取值仍走原路径"。
  [Fact]
  public void ForInterproceduralBridge_SummaryMapping_IsNotSingletonized()
  {
    var plain = NLCPGEdgeLabel.ForInterproceduralBridge(
      NLCPGInterproceduralBridgeKind.SummaryMapping);

    Assert.Equal(NLCPGInterproceduralBridgeKind.SummaryMapping, plain.InterproceduralBridgeKind);
    Assert.Null(plain.FlowSummaryResolution);

    // SummaryMapping 不在缓存之列 ⇒ 与改动前一致，每次都是新实例。
    // ⚠️ 这条**不是**在主张"新实例更好"，只是把"兜底分支保持原行为"钉成事实：
    // 若有人把 4 个取值一起缓存，本断言会红，从而迫使那次改动显式说明理由。
    Assert.NotSame(plain, NLCPGEdgeLabel.ForInterproceduralBridge(
      NLCPGInterproceduralBridgeKind.SummaryMapping));
  }

  // 单例不得让"同值不同实例"的去重语义变成必需：按值比较仍然成立。
  // 这条守住既有契约——元数据池按值去重，故共享实例不改变边数。
  [Fact]
  public void SharedLabelInstances_StillDeduplicateByValue()
  {
    var graph = new NLCPGGraph();
    var source = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "source"));
    var target = graph.AddNode(new NLCPGNodeDraft(NLCPGNodeKind.Operation, Name: "target"));

    // 同一桥种类的两条同端点边：单例化后标签**同一个实例**，仍必须折叠成一条边。
    graph.AddEdge(
      source,
      target,
      NLCPGEdgeKind.InterproceduralDataFlow,
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.ArgumentToParameter));
    graph.AddEdge(
      source,
      target,
      NLCPGEdgeKind.InterproceduralDataFlow,
      NLCPGEdgeLabel.ForInterproceduralBridge(NLCPGInterproceduralBridgeKind.ArgumentToParameter));
    graph.FreezeQueryIndex();

    Assert.Single(graph.Edges);
  }
}
