using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Passes;

/// <summary>
/// Immutable, deterministically ordered boundary edge candidate produced after local flow is available.
/// </summary>
internal sealed record InterproceduralDataFlowPlan(
  RoslynCpgNode CallSiteNode,
  RoslynCpgNode TargetMethodNode,
  RoslynCpgNode SourceNode,
  RoslynCpgNode TargetNode,
  RoslynCpgInterproceduralBridgeKind BridgeKind,
  int ArgumentOrdinal = -1);
