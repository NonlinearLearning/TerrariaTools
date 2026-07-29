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

public sealed record RuleDependency(
  RuleNodeId Producer,
  MarkedStructureSelector? RequiredStructure = null,
  RuleTerminalFactSelector? RequiredTerminalFact = null);

public sealed record RuleGraphNode(RuleNodeId NodeId, RuleKind Kind, IReadOnlyList<RuleDependency> Dependencies)
{
    public IReadOnlyList<MarkedStructureSelector> ProducedStructures { get; init; } =
      Array.Empty<MarkedStructureSelector>();

    public IReadOnlyList<RuleConsumedStructure> ConsumedStructures { get; init; } =
      Array.Empty<RuleConsumedStructure>();

    public RuleFactDomain FactDomain { get; init; } = RuleFactDomain.None;

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
