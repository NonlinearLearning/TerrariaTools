using NLCPG.Contracts;

namespace NLCPG.Model;

/// 表示两个图节点之间的一条带类型关系边。
public sealed record NLCPGEdge
{
    // 创建一条图边，并在需要时校验调用点上下文与上下文标识一致。
    public NLCPGEdge(NodeId sourceNodeId, NodeId targetNodeId, NLCPGEdgeKind kind, NLCPGEdgeLabel? structuredLabel = null, NLCPGContextId? contextId = null, NLCPGCallSiteContext? callSiteContext = null)
    {
        var resolvedContextId = callSiteContext?.ToContextId() ?? contextId;
        if (callSiteContext.HasValue &&
            contextId.HasValue &&
            contextId.Value != resolvedContextId)
        {
            throw new ArgumentException(
              "CallSiteContext must derive the same ContextId when both are provided.");
        }

        SourceNodeId = sourceNodeId;
        TargetNodeId = targetNodeId;
        Kind = kind;
        StructuredLabel = structuredLabel;
        ContextId = resolvedContextId;
        CallSiteContext = callSiteContext;
    }

    public NodeId SourceNodeId { get; init; }

    public NodeId TargetNodeId { get; init; }

    public NLCPGEdgeKind Kind { get; init; }

    public NLCPGEdgeLabel? StructuredLabel { get; init; }

    public NLCPGContextId? ContextId { get; init; }

    public NLCPGCallSiteContext? CallSiteContext { get; init; }
}
