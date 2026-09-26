using NLCPG.Contracts;

namespace NLCPG.Model;

/// 表示两个图节点之间的一条带类型关系边值。
public readonly record struct NLCPGEdge
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

    // 由已解析的字段直接构造，跳过 ToContextId() 归一化。
    //
    // 仅供 CanonicalEdgeStore 投影使用：那里的 ContextId 在构图期已由本类型的公开构造函数
    // 解析并存入元数据池，故再次归一化只会重复插值（CallSiteContext.ToContextId() 每次
    // 访问都重新构造字符串），把构图的"每边一次"放大成"每次读取一次"。
    private NLCPGEdge(
      NodeId sourceNodeId,
      NodeId targetNodeId,
      NLCPGEdgeKind kind,
      NLCPGEdgeLabel? structuredLabel,
      NLCPGContextId? contextId,
      NLCPGCallSiteContext? callSiteContext,
      bool skipNormalization)
    {
        _ = skipNormalization;
        SourceNodeId = sourceNodeId;
        TargetNodeId = targetNodeId;
        Kind = kind;
        StructuredLabel = structuredLabel;
        ContextId = contextId;
        CallSiteContext = callSiteContext;
    }

    // 按已解析字段投影一条边，不做归一化校验。字段值与公开构造函数的结果逐字段相同。
    internal static NLCPGEdge CreateProjected(
      NodeId sourceNodeId,
      NodeId targetNodeId,
      NLCPGEdgeKind kind,
      NLCPGEdgeLabel? structuredLabel,
      NLCPGContextId? contextId,
      NLCPGCallSiteContext? callSiteContext)
    {
        return new NLCPGEdge(
          sourceNodeId,
          targetNodeId,
          kind,
          structuredLabel,
          contextId,
          callSiteContext,
          skipNormalization: true);
    }

    public NodeId SourceNodeId { get; }

    public NodeId TargetNodeId { get; }

    public NLCPGEdgeKind Kind { get; }

    public NLCPGEdgeLabel? StructuredLabel { get; }

    public NLCPGContextId? ContextId { get; }

    public NLCPGCallSiteContext? CallSiteContext { get; }
}
