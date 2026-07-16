using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Model;

/// <summary>
/// Carries the structured metadata associated with a graph edge.
/// </summary>
public sealed record RoslynCpgEdgeLabel
{
  private RoslynCpgEdgeLabel(
    RoslynCpgInterproceduralBridgeKind? interproceduralBridgeKind,
    RoslynCpgDecisionRelationKind? decisionRelationKind)
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

  public RoslynCpgInterproceduralBridgeKind? InterproceduralBridgeKind { get; }

  public RoslynCpgDecisionRelationKind? DecisionRelationKind { get; }

  public string StableKey =>
    InterproceduralBridgeKind is { } bridgeKind
      ? $"interprocedural-bridge:{bridgeKind}"
      : DecisionRelationKind is { } relationKind
        ? $"decision-relation:{relationKind}"
        : string.Empty;

  public static RoslynCpgEdgeLabel ForDecisionRelation(
    RoslynCpgDecisionRelationKind decisionRelationKind)
  {
    return new RoslynCpgEdgeLabel(null, decisionRelationKind);
  }

  public static RoslynCpgEdgeLabel ForInterproceduralBridge(
    RoslynCpgInterproceduralBridgeKind interproceduralBridgeKind)
  {
    return new RoslynCpgEdgeLabel(interproceduralBridgeKind, null);
  }

  public override string ToString()
  {
    return StableKey;
  }
}
