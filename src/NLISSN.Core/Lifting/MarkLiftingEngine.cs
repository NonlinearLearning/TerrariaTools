using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Core.Lifting;

public sealed class MarkLiftingEngine
{
    // 为规则图执行器执行一个已准备好显式输入的提升节点；不读取 GroupKey。
    public static IReadOnlyList<LiftedMarkRecord> ExecuteRule(
      RuleContext context,
      RuleDefinitionLift rule,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        var ruleContext = BuildRuleContext(context, seedMarks, propagatedMarks);
        return rule.Lift(ruleContext, seedMarks, propagatedMarks, existingLiftedMarks)
          .Select(candidate =>
          {
              ValidateLiftNode(rule, candidate.Mark.SyntaxNode);
              MarkingEngine.ValidateProducedStructure(rule.Produces, candidate.Mark);
              return BindLiftedMarkRecord(ruleContext, candidate, rule.GroupKey);
          })
          .ToList();
    }

    // 兼容入口也按规则图执行；GroupKey 只保留在输出投影中。
    public IReadOnlyList<LiftedMarkRecord> Run(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<RuleDefinitionLift> rules)
    {
        var liftEligiblePropagatedMarks = propagatedMarks
          .Where(mark => mark.Payload is null)
          .ToList();
        var sourceNodes = CreateSourceNodes(seedMarks, liftEligiblePropagatedMarks);
        var sourceNodeIds = sourceNodes.Select(node => node.NodeId).ToHashSet();
        var liftNodeIds = rules.Select(rule => RuleNodeId.For(RuleKind.Lift, rule.RuleId)).ToHashSet();
        var contractGraph = new RuleStructureContractGraphCompiler().Compile(rules
          .Select(rule => new RuleStructureContractGraphNode(
            rule.NodeId,
            RuleKind.Lift,
            rule.Consumes,
            rule.Produces))
          .ToList());
        var ruleNodes = rules.Select(rule => new RuleGraphNode(
          RuleNodeId.For(RuleKind.Lift, rule.RuleId),
          RuleKind.Lift,
          rule.ProducedOutputs,
          ResolveDependencies(rule, sourceNodes, sourceNodeIds, liftNodeIds, contractGraph))
        {
          ProducedStructures = rule.Produces.Structures,
          ConsumedStructures = rule.Consumes.Structures
        }).ToList();
        var graph = new RuleGraphCompiler().Compile(sourceNodes.Concat(ruleNodes).ToList());
        var executionNodes = sourceNodes
          .Select(node => new RuleGraphExecutionNode(
            node,
            (_, _) => Task.FromResult(CreateSourceResult(node, seedMarks, liftEligiblePropagatedMarks))))
          .Concat(rules.Select(rule =>
          {
              var node = graph.Nodes.Single(candidate => candidate.NodeId == RuleNodeId.For(RuleKind.Lift, rule.RuleId));
              return new RuleGraphExecutionNode(
                node,
                (inputs, _) =>
                {
                    var values = GetValues(node, inputs);
                    return Task.FromResult(CreateResult(
                      rule.ProducedOutputs,
                      rule.Produces,
                      ExecuteRule(
                        context,
                        rule,
                        values.OfType<MarkRecord>().ToList(),
                        values.OfType<PropagatedMarkRecord>().ToList(),
                        values.OfType<LiftedMarkRecord>().ToList())));
                });
          }))
          .ToList();
        var execution = new RuleGraphExecutor(context.Runtime.ConcurrencyPool).ExecuteAsync(
            graph,
            executionNodes,
            context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        return execution.Nodes
          .Where(node => node.NodeId.Value.StartsWith("Lift:", StringComparison.Ordinal))
          .SelectMany(node => node.Result.GetOutputs(RuleOutputKind.LiftedMark))
          .OfType<LiftedMarkRecord>()
          .DistinctBy(mark => (
            RuleStageGroupKey.Get(mark),
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
    }

    private static IReadOnlyList<RuleGraphNode> CreateSourceNodes(
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        return seedMarks
          .GroupBy(mark => RuleNodeId.For(RuleKind.Mark, mark.RuleId))
          .Select(group => new RuleGraphNode(group.Key, RuleKind.Mark, new[] { RuleOutputKind.SeedMark }, Array.Empty<RuleDependency>()))
          .Concat(propagatedMarks
            .GroupBy(mark => RuleNodeId.For(RuleKind.Propagate, mark.RuleId))
            .Select(group => new RuleGraphNode(group.Key, RuleKind.Propagate, new[] { RuleOutputKind.PropagatedMark }, Array.Empty<RuleDependency>())))
          .ToList();
    }

    private static IReadOnlyList<RuleDependency> ResolveDependencies(
      RuleDefinitionLift rule,
      IReadOnlyList<RuleGraphNode> sourceNodes,
      IReadOnlySet<RuleNodeId> sourceNodeIds,
      IReadOnlySet<RuleNodeId> liftNodeIds,
      CompiledRuleStructureContractGraph contractGraph)
    {
        var declared = rule.Consumes.Structures.Count > 0
          ? contractGraph.Edges
            .Where(edge => edge.Consumer == rule.NodeId)
            .Select(edge => new RuleDependency(
              edge.Producer,
              RuleOutputKind.LiftedMark,
              edge.Selector))
            .ToList()
          : rule.Dependencies;
        if (declared.Count == 0)
        {
            return sourceNodes
              .Select(node => new RuleDependency(node.NodeId, node.ProducedOutputs.Single()))
              .ToList();
        }

        return declared
          .Where(dependency => sourceNodeIds.Contains(dependency.Producer) || liftNodeIds.Contains(dependency.Producer))
          .Distinct()
          .ToList();
    }

    private static RuleNodeResult CreateSourceResult(
      RuleGraphNode node,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var values = node.Kind == RuleKind.Mark
          ? seedMarks.Where(mark => string.Equals(mark.RuleId, node.NodeId.Value["Mark:".Length..], StringComparison.Ordinal)).Cast<object>()
          : propagatedMarks.Where(mark => string.Equals(mark.RuleId, node.NodeId.Value["Propagate:".Length..], StringComparison.Ordinal)).Cast<object>();
        return RuleNodeResult.From(node.ProducedOutputs.Single(), values.ToArray());
    }

    private static RuleNodeResult CreateResult<T>(
      IReadOnlyList<RuleOutputKind> outputKinds,
      RuleProducesContract produces,
      IReadOnlyList<T> values)
    {
        var boxed = values.Cast<object>().ToList();
        return RuleNodeResult.FromProducedOutputs(outputKinds, boxed, produces);
    }

    private static IReadOnlyList<object> GetValues(RuleGraphNode node, RuleNodeInputs inputs)
    {
        return node.Dependencies
          .SelectMany(dependency => dependency.RequiredStructure is { } selector
            ? inputs.GetOutputs(dependency.Producer, selector)
            : inputs.GetOutputs(dependency.Producer, dependency.RequiredOutput))
          .ToList();
    }

    private static RuleContext BuildRuleContext(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var fragments = seedMarks
          .Select(mark => mark.SyntaxNode)
          .Concat(propagatedMarks.Select(mark => mark.Mark.SyntaxNode))
          .Distinct()
          .ToList();
        if (fragments.Count == 0)
        {
            return context;
        }

        var structureView = context.StructureViews.BuildStructureView(fragments);
        return context.StructureViews.WithStructureView(structureView);
    }

    internal static void ValidateLiftNode(RuleDefinitionLift rule, SyntaxNode syntaxNode)
    {
        var nodeKind = (SyntaxKind)syntaxNode.RawKind;
        if (rule.AllowedLiftNodeKinds.Contains(nodeKind))
        {
            return;
        }

        var allowedKinds = string.Join(", ", rule.AllowedLiftNodeKinds);
        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted unsupported lift node kind '{nodeKind}'. Allowed lift node kinds: {allowedKinds}.");
    }

    internal static LiftedMarkRecord BindLiftedMarkRecord(RuleContext context, LiftedMarkRecord candidate, string? groupKey = null)
    {
        return candidate with
        {
            Mark = MarkingEngine.BindMarkRecord(context, candidate.Mark, groupKey),
            SourceMark = MarkingEngine.BindMarkRecord(context, candidate.SourceMark, groupKey),
            GroupKey = candidate.GroupKey ?? groupKey
        };
    }
}
