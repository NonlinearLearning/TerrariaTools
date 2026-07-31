using NLCPG.Contracts;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Application;

public sealed record RulePipeline(
  IReadOnlyList<RuleDefinitionMark> Markers,
  IReadOnlyList<RuleDefinitionPropagate> Propagators,
  IReadOnlyList<RuleDefinitionLift> Lifters,
  IReadOnlyList<RuleDefinitionPropose> Proposers,
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
            RuleNodeId.For(declaration.Kind, declaration.Rule.RuleId),
            declaration.Rule.Consumes,
            declaration.Rule.Produces,
            declaration.Rule.InputCardinality))
          .ToList());
        var declaredNodes = declarations
          .Select(declaration => ToNode(
            declaration,
            contractGraph))
          .ToList();
        return new RuleGraphCompiler().Compile(declaredNodes);
    }

    // 汇总四个阶段所有规则声明的能力需求。
    public IReadOnlyList<NLCPGCapability> GetRequiredCapabilities()
    {
        var requiredCapabilities = Markers.SelectMany(rule => rule.RequiredCapabilities)
          .Concat(Propagators.SelectMany(rule => rule.RequiredCapabilities))
          .Concat(Lifters.SelectMany(rule => rule.RequiredCapabilities))
          .Concat(Proposers.SelectMany(rule => rule.RequiredCapabilities))
          .ToList();
        return requiredCapabilities
          .Distinct()
          .OrderBy(capability => capability)
          .ToList();
    }

    private static RuleGraphNode ToNode(
      RuleGraphRuleDeclaration declaration,
      CompiledRuleStructureContractGraph contractGraph)
    {
        var rule = declaration.Rule;
        var nodeId = RuleNodeId.For(declaration.Kind, rule.RuleId);
        IReadOnlyList<RuleDependency> dependencies =
          rule.Consumes.Inputs.Count > 0
          ? contractGraph.Edges
            .Where(edge => edge.Consumer == nodeId)
            .Select(edge => new RuleDependency(edge.Producer, edge.Input))
            .ToList()
          : Array.Empty<RuleDependency>();
        return new RuleGraphNode(
          nodeId,
          declaration.Kind,
          dependencies)
        {
            ProducedSyntax = rule.Produces.Outputs
        };
    }

    private sealed record RuleGraphRuleDeclaration(
      IRuleDefinition Rule,
      RuleKind Kind,
      bool IsEnabled);
}
