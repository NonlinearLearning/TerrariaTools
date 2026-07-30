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

    // 为跨过程桥接边创建结构化标签。
    public static NLCPGEdgeLabel ForInterproceduralBridge(NLCPGInterproceduralBridgeKind interproceduralBridgeKind)
    {
        return new NLCPGEdgeLabel(interproceduralBridgeKind, null);
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
