using NLCPG.Contracts;
using NLCPG.Analysis.FlowSummaries;

namespace NLCPG.Model;

/// 承载与图边关联的结构化元数据。
public sealed record NLCPGEdgeLabel
{
    private NLCPGEdgeLabel(
      NLCPGInterproceduralBridgeKind? interproceduralBridgeKind,
      NLCPGDecisionRelationKind? decisionRelationKind,
      FlowSummaryResolution? flowSummaryResolution = null,
      string? flowSummaryMethodKey = null,
      FlowSummaryEndpoint? flowSummarySource = null,
      FlowSummaryEndpoint? flowSummaryTarget = null)
    {
        if (interproceduralBridgeKind.HasValue == decisionRelationKind.HasValue &&
            interproceduralBridgeKind.HasValue)
        {
            throw new ArgumentException(
              "An edge label can represent only one structured label kind.");
        }

        InterproceduralBridgeKind = interproceduralBridgeKind;
        DecisionRelationKind = decisionRelationKind;
        FlowSummaryResolution = flowSummaryResolution;
        FlowSummaryMethodKey = flowSummaryMethodKey;
        FlowSummarySource = flowSummarySource;
        FlowSummaryTarget = flowSummaryTarget;
    }

    public NLCPGInterproceduralBridgeKind? InterproceduralBridgeKind { get; }

    public NLCPGDecisionRelationKind? DecisionRelationKind { get; }

    public FlowSummaryResolution? FlowSummaryResolution { get; }

    public string? FlowSummaryMethodKey { get; }

    public FlowSummaryEndpoint? FlowSummarySource { get; }

    public FlowSummaryEndpoint? FlowSummaryTarget { get; }

    public string StableKey =>
      InterproceduralBridgeKind is { } bridgeKind
        ? FlowSummaryResolution is { } resolution
          ? $"interprocedural-summary:{bridgeKind}:{resolution}:{FlowSummaryMethodKey}:{FlowSummarySource}->{FlowSummaryTarget}"
          : $"interprocedural-bridge:{bridgeKind}"
        : DecisionRelationKind is { } relationKind
          ? $"decision-relation:{relationKind}"
          : string.Empty;

    // 为决策关系边创建结构化标签。
    public static NLCPGEdgeLabel ForDecisionRelation(NLCPGDecisionRelationKind decisionRelationKind)
    {
        return new NLCPGEdgeLabel(null, decisionRelationKind);
    }

    // 三个"无附加内容"的跨过程桥标签。取值只由桥种类决定，且本类型不可变，
    // 故同类标签的任意两个实例都不可区分 ⇒ 可安全共享同一实例。
    //
    // 发布段原先对【每条桥边】都 new 一个：`NLCPGBuilder` 的发布循环内调用
    // `ForInterproceduralBridge(BridgeKindOf(edge))`，而该值域只有 3 个。
    // 这些实例除每个元数据条目保留的首个之外全部即弃（pending 池按值去重），
    // 属于纯冗余分配。缓存后分配次数由"边数"降为"常数"。
    //
    // 注意 SummaryMapping 不在缓存之列：它走 ForFlowSummaryBridge，其标签还带
    // 分辨率/方法键/两端点，取值空间不是常数。落下界分支即可保持它原有语义。
    //
    // 可见性取 internal（而非 private）：本项的唯一直接判据是"同 kind 恒同实例"
    // （N4/N5），把三个单例本身开放给友元测试可直接断言其两两不同，
    // 无需经工厂间接推断。
    internal static readonly NLCPGEdgeLabel ArgumentToParameterBridge =
      new(NLCPGInterproceduralBridgeKind.ArgumentToParameter, null);

    internal static readonly NLCPGEdgeLabel ReturnToMethodReturnBridge =
      new(NLCPGInterproceduralBridgeKind.ReturnToMethodReturn, null);

    internal static readonly NLCPGEdgeLabel MethodReturnToCallResultBridge =
      new(NLCPGInterproceduralBridgeKind.MethodReturnToCallResult, null);

    // 为跨过程桥接边创建结构化标签。
    //
    // 缓存放在工厂内（而非只在某个调用点取单例）：本方法有两个调用方
    // （`NLCPGBuilder` 的发布段、`CpgFrozenShardGraphReader` 的标签还原），
    // 放在这里可保证两者都受益，也不会因将来新增调用点而漏掉。
    public static NLCPGEdgeLabel ForInterproceduralBridge(NLCPGInterproceduralBridgeKind interproceduralBridgeKind)
    {
        return interproceduralBridgeKind switch
        {
            NLCPGInterproceduralBridgeKind.ArgumentToParameter => ArgumentToParameterBridge,
            NLCPGInterproceduralBridgeKind.ReturnToMethodReturn => ReturnToMethodReturnBridge,
            NLCPGInterproceduralBridgeKind.MethodReturnToCallResult => MethodReturnToCallResultBridge,
            // 兜底保持原行为（每次新实例），使本改动对未列举取值逐位无变化。
            _ => new NLCPGEdgeLabel(interproceduralBridgeKind, null),
        };
    }

    public static NLCPGEdgeLabel ForFlowSummaryBridge(
      NLCPGInterproceduralBridgeKind interproceduralBridgeKind,
      FlowSummaryResolution resolution,
      string methodKey,
      FlowSummaryEndpoint source,
      FlowSummaryEndpoint target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodKey);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        return new NLCPGEdgeLabel(
          interproceduralBridgeKind,
          null,
          resolution,
          methodKey,
          source,
          target);
    }

    // 返回标签的稳定字符串键。
    public override string ToString()
    {
        return StableKey;
    }
}
