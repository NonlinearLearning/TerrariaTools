using NLCPG.Contracts;

namespace NLCPG.Model;

/// 承载与图边关联的结构化元数据。
public sealed record NLCPGEdgeLabel
{
    private NLCPGEdgeLabel(NLCPGInterproceduralBridgeKind? interproceduralBridgeKind, NLCPGDecisionRelationKind? decisionRelationKind)
    {
        if (interproceduralBridgeKind.HasValue == decisionRelationKind.HasValue &&
            interproceduralBridgeKind.HasValue)
        {
            throw new ArgumentException(
              "An edge label can represent only one structured label kind.");
        }

        InterproceduralBridgeKind = interproceduralBridgeKind;
        DecisionRelationKind = decisionRelationKind;
    }

    public NLCPGInterproceduralBridgeKind? InterproceduralBridgeKind { get; }

    public NLCPGDecisionRelationKind? DecisionRelationKind { get; }

    public string StableKey =>
      InterproceduralBridgeKind is { } bridgeKind
        ? $"interprocedural-bridge:{bridgeKind}"
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

    // 返回标签的稳定字符串键。
    public override string ToString()
    {
        return StableKey;
    }
}
