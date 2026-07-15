using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Builder.Passes;

/// <summary>
/// Immutable, deterministically ordered boundary edge candidate produced after local flow is available.
/// </summary>
internal sealed record InterproceduralDataFlowPlan(
  string CallSiteId,
  string TargetMethodId,
  string SourceNodeId,
  string TargetNodeId,
  RoslynCpgInterproceduralBridgeKind BridgeKind,
  int ArgumentOrdinal = -1)
{
  public string Label => BridgeKind.ToString();
}
