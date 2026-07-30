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
    public RuleNodeId(string value)
    {
        Value = Validate(value);
    }

    public string Value { get; }

    public static RuleNodeId For(RuleKind kind, string ruleId)
    {
        return new RuleNodeId($"{kind}:{ruleId}");
    }

    private static string Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Rule node ID cannot be empty.", nameof(value));
        }

        return value;
    }
}

public sealed record RuleDependency(RuleNodeId Producer, RuleConsumedSyntax RequiredInput);

public sealed record RuleGraphNode(RuleNodeId NodeId, RuleKind Kind, IReadOnlyList<RuleDependency> Dependencies)
{
    public IReadOnlyList<RuleProducedSyntax> ProducedSyntax { get; init; } =
      Array.Empty<RuleProducedSyntax>();

    public IReadOnlyList<RuleConsumedSyntax> ConsumedSyntax { get; init; } =
      Array.Empty<RuleConsumedSyntax>();

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
  IReadOnlyDictionary<RuleNodeId, int> NodeIndexes,
  IReadOnlyDictionary<RuleNodeId, IReadOnlyList<RuleNodeId>> DownstreamNodes);
