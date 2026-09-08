namespace NLISSN.Core.Pipeline;

public enum RuleKind
{
    Mark,
    Propagate,
    Lift,
    Propose
}

public sealed record RuleNodeId
{
    public RuleNodeId(RuleKind kind, string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            throw new ArgumentException("Rule node rule ID cannot be empty.", nameof(ruleId));
        }

        Kind = kind;
        RuleId = ruleId;
    }

    public RuleKind Kind { get; }

    public string RuleId { get; }

    public string Value => $"{Kind}:{RuleId}";

    public static RuleNodeId For(RuleKind kind, string ruleId)
    {
        return new RuleNodeId(kind, ruleId);
    }
}

public sealed record RuleDependency(RuleNodeId Producer, RuleConsumedSyntax? RequiredInput);

public sealed record RuleGraphNode(RuleNodeId NodeId, RuleKind Kind, IReadOnlyList<RuleDependency> Dependencies)
{
    public IReadOnlyList<RuleProducedSyntax> ProducedSyntax { get; init; } =
      Array.Empty<RuleProducedSyntax>();

    public RuleGraphNode(
      RuleNodeId nodeId,
      RuleKind kind,
      params RuleDependency[] dependencies)
      : this(nodeId, kind, (IReadOnlyList<RuleDependency>)dependencies)
    {
    }
}

public sealed record CompiledRuleGraph(
  IReadOnlyList<RuleGraphNode> Nodes,
  IReadOnlyDictionary<RuleNodeId, int> NodeIndexes);
