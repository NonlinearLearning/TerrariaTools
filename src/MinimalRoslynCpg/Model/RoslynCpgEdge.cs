using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// 表示两个图节点之间的一条带类型关系边。
/// </summary>
public sealed record RoslynCpgEdge
{
  public RoslynCpgEdge(
    NodeId sourceNodeId,
    NodeId targetNodeId,
    RoslynCpgEdgeKind kind,
    RoslynCpgEdgeLabel? structuredLabel = null,
    RoslynCpgContextId? contextId = null,
    RoslynCpgCallSiteContext? callSiteContext = null)
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

  public RoslynCpgEdgeKind Kind { get; init; }

  public RoslynCpgEdgeLabel? StructuredLabel { get; init; }

  public RoslynCpgContextId? ContextId { get; init; }

  public RoslynCpgCallSiteContext? CallSiteContext { get; init; }
}
