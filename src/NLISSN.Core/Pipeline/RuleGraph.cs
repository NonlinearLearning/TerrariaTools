namespace NLISSN.Rules;

public enum RuleKind
{
    Mark,
    Propagate,
    Lift,
    Propose
}

public enum RuleOutputKind
{
    SeedMark,
    PropagatedMark,
    LiftedMark,
    LocalDefinitionFromInitializer,
    LocalDefinitionFromObjectCreation,
    LocalReference,
    LogicalHost,
    IfCompletion,
    ExpressionHost,
    IfStructure,
    SwitchStructure,
    DecisionUnit
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
  RuleOutputKind RequiredOutput,
  MarkedStructureSelector? RequiredStructure = null,
  RuleTerminalFactSelector? RequiredTerminalFact = null);

//ProducedOutputs此节点能产出什么输出
//Dependencies
public sealed record RuleGraphNode(RuleNodeId NodeId, RuleKind Kind, IReadOnlyList<RuleOutputKind> ProducedOutputs, IReadOnlyList<RuleDependency> Dependencies)
{
    public IReadOnlyList<MarkedStructureSelector> ProducedStructures { get; init; } =
      Array.Empty<MarkedStructureSelector>();

    public IReadOnlyList<RuleConsumedStructure> ConsumedStructures { get; init; } =
      Array.Empty<RuleConsumedStructure>();

    public RuleFactDomain FactDomain { get; init; } = RuleFactDomain.None;

    public RuleGraphNode(
      RuleNodeId nodeId,
      RuleKind kind,
      IReadOnlyList<RuleOutputKind> producedOutputs,
      params RuleDependency[] dependencies)
      : this(nodeId, kind, producedOutputs, (IReadOnlyList<RuleDependency>)dependencies)
    {
    }
}

public sealed record CompiledRuleGraph(
  IReadOnlyList<RuleGraphNode> Nodes,
  IReadOnlyDictionary<RuleNodeId, int> NodeIndexes,
  IReadOnlyDictionary<RuleNodeId, IReadOnlyList<RuleNodeId>> DownstreamNodes);
