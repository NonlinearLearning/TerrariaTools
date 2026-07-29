using NLCPG.Contracts;
using NLISSN.Rules;

namespace NLISSN.Application;

public sealed record RulePipeline(
  IReadOnlyList<RuleDefinitionMark> Markers,
  IReadOnlyList<RuleDefinitionPropagate> Propagators,
  IReadOnlyList<RuleDefinitionLift> Lifters,
  IReadOnlyList<RuleDefinitionPropose> Proposers,
  bool EnableHelperReturnSlicePilot = false,
  IReadOnlyList<RuleDefinitionMark>? DisabledMarkers = null,
  IReadOnlyList<RuleDefinitionPropagate>? DisabledPropagators = null,
  IReadOnlyList<RuleDefinitionLift>? DisabledLifters = null,
  IReadOnlyList<RuleDefinitionPropose>? DisabledProposers = null)
{
    public CompiledRuleGraph CompileRuleGraph()
    {
        var declarations = Markers
          .Select(rule => new RuleGraphRuleDeclaration(rule, RuleKind.Mark, true))
          .Concat(Propagators.Select(rule => new RuleGraphRuleDeclaration(rule, RuleKind.Propagate, true)))
          .Concat(Lifters.Select(rule => new RuleGraphRuleDeclaration(rule, RuleKind.Lift, true)))
          .Concat(Proposers.Select(rule => new RuleGraphRuleDeclaration(rule, RuleKind.Propose, true)))
          .Concat((DisabledMarkers ?? Array.Empty<RuleDefinitionMark>())
            .Select(rule => new RuleGraphRuleDeclaration(rule, RuleKind.Mark, false)))
          .Concat((DisabledPropagators ?? Array.Empty<RuleDefinitionPropagate>())
            .Select(rule => new RuleGraphRuleDeclaration(rule, RuleKind.Propagate, false)))
          .Concat((DisabledLifters ?? Array.Empty<RuleDefinitionLift>())
            .Select(rule => new RuleGraphRuleDeclaration(rule, RuleKind.Lift, false)))
          .Concat((DisabledProposers ?? Array.Empty<RuleDefinitionPropose>())
            .Select(rule => new RuleGraphRuleDeclaration(rule, RuleKind.Propose, false)))
          .ToList();
        var contractGraph = new RuleStructureContractGraphCompiler().Compile(declarations
          .Select(declaration => new RuleStructureContractGraphNode(
            declaration.Rule.NodeId,
            declaration.Kind,
            declaration.Rule.Consumes,
            declaration.Rule.Produces,
            declaration.IsEnabled))
          .ToList());
        var contractNodesById = contractGraph.Nodes.ToDictionary(node => node.NodeId);
        var declaredNodes = declarations
          .Select(declaration => ToNode(
            declaration,
            declarations,
            contractGraph,
            contractNodesById))
          .ToList();
        return new RuleGraphCompiler().Compile(declaredNodes);
    }

    // 汇总四个阶段所有规则声明的能力需求，并按试验开关补充额外查询能力。
    public IReadOnlyList<NLCPGCapability> GetRequiredCapabilities()
    {
        var requiredCapabilities = Markers.SelectMany(rule => rule.RequiredCapabilities)
          .Concat(Propagators.SelectMany(rule => rule.RequiredCapabilities))
          .Concat(Lifters.SelectMany(rule => rule.RequiredCapabilities))
          .Concat(Proposers.SelectMany(rule => rule.RequiredCapabilities))
          .ToList();
        if (EnableHelperReturnSlicePilot && Propagators.Any(rule => string.Equals(
              rule.GetType().Name,
              "ClassSymbolReferencePropagationRule",
              StringComparison.Ordinal)))
        {
            requiredCapabilities.Add(NLCPGCapability.InterproceduralDataFlow);
        }

        return requiredCapabilities
          .Distinct()
          .OrderBy(capability => capability)
          .ToList();
    }

    private static RuleGraphNode ToNode(
      RuleGraphRuleDeclaration declaration,
      IReadOnlyList<RuleGraphRuleDeclaration> declarations,
      CompiledRuleStructureContractGraph contractGraph,
      IReadOnlyDictionary<RuleNodeId, RuleStructureContractGraphNode> contractNodesById)
    {
        var rule = declaration.Rule;
        var dependencies = rule.Consumes.Structures.Count > 0
          ? contractGraph.Edges
            .Where(edge => edge.Consumer == rule.NodeId)
            .Select(edge => new RuleDependency(
              edge.Producer,
              GetStageOutput(contractNodesById[edge.Producer].Kind),
              edge.Selector))
            .ToList()
          : rule.TerminalConsumes.Selectors.Count > 0
          ? CreateTerminalDependencies(rule.TerminalConsumes, declarations)
          : rule.Dependencies;
        return new RuleGraphNode(
          rule.NodeId,
          declaration.Kind,
          rule.ProducedOutputs,
          dependencies)
        {
            ProducedStructures = rule.Produces.Structures,
            ConsumedStructures = rule.Consumes.Structures,
            FactDomain = rule.FactDomain
        };
    }

    private static IReadOnlyList<RuleDependency> CreateTerminalDependencies(
      RuleTerminalConsumesContract terminalConsumes,
      IReadOnlyList<RuleGraphRuleDeclaration> declarations)
    {
        var dependencies = new List<RuleDependency>();
        foreach (var selector in terminalConsumes.Selectors)
        {
            if (selector.Domain == RuleFactDomain.None || selector.SourceStages.Count == 0)
            {
                throw new InvalidOperationException("A terminal rule fact selector requires a domain and at least one source stage.");
            }

            dependencies.AddRange(declarations
              .Where(declaration =>
                declaration.Rule.FactDomain == selector.Domain &&
                selector.SourceStages.Contains(declaration.Kind))
              .Select(declaration => new RuleDependency(
                declaration.Rule.NodeId,
                GetStageOutput(declaration.Kind),
                RequiredTerminalFact: selector)));
        }

        return dependencies
          .Distinct()
          .ToList();
    }

    private static RuleOutputKind GetStageOutput(RuleKind kind)
    {
        return kind switch
        {
            RuleKind.Mark => RuleOutputKind.SeedMark,
            RuleKind.Propagate => RuleOutputKind.PropagatedMark,
            RuleKind.Lift => RuleOutputKind.LiftedMark,
            RuleKind.Propose => RuleOutputKind.DecisionUnit,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported rule kind.")
        };
    }

    private sealed record RuleGraphRuleDeclaration(
      IRuleDefinition Rule,
      RuleKind Kind,
      bool IsEnabled);
}
