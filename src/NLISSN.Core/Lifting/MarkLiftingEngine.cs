using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLCPG.Analysis;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Lifting;

public sealed class MarkLiftingEngine
{
    // 为规则图执行器执行一个已准备好显式输入的提升节点。
    public static IReadOnlyList<LiftedMarkRecord> ExecuteRule(
      RuleContext context,
      RuleDefinitionLift rule,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<LiftedMarkRecord> existingLiftedMarks)
    {
        var ruleContext = BuildRuleContext(context, seedMarks, propagatedMarks);
        var results = rule.Lift(ruleContext, seedMarks, propagatedMarks, existingLiftedMarks)
          .Select(candidate =>
          {
              var tagged = candidate with
              {
                  Mark = MarkingEngine.BindDeclaredSemanticTag(rule.Produces, candidate.Mark)
              };
              ValidateLiftNode(rule, tagged.Mark.SyntaxNode);
              MarkingEngine.ValidateProducedSyntax(rule.Produces, tagged.Mark);
              return BindLiftedMarkRecord(ruleContext, tagged);
          })
          .ToList();
        context.Evidence.RecordLift(rule.RuleId, seedMarks, propagatedMarks, results);
        return results;
    }

    // 兼容入口也按规则图执行。
    public IReadOnlyList<LiftedMarkRecord> Run(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<RuleDefinitionLift> rules)
    {
        var liftEligiblePropagatedMarks = propagatedMarks
          .Where(mark => mark.Payload is null)
          .ToList();
        var consumedInputs = rules
          .SelectMany(rule => rule.Consumes.Inputs)
          .ToList();
        var sourceNodes = CreateSourceNodes(
          seedMarks,
          liftEligiblePropagatedMarks,
          consumedInputs);
        var sourceNodeIds = sourceNodes.Select(node => node.NodeId).ToHashSet();
        var liftNodeIds = rules.Select(rule => RuleNodeId.For(RuleKind.Lift, rule.RuleId)).ToHashSet();
        var contractGraph = new RuleStructureContractGraphCompiler().Compile(sourceNodes
          .Select(node => new RuleStructureContractGraphNode(
            node.NodeId,
            node.Kind,
            RuleConsumesContract.Empty,
            new RuleProducesContract(node.ProducedSyntax)))
          .Concat(rules
          .Select(rule => new RuleStructureContractGraphNode(
            rule.NodeId,
            RuleKind.Lift,
            rule.Consumes,
            rule.Produces)))
          .ToList());
        var ruleNodes = rules.Select(rule => new RuleGraphNode(
          RuleNodeId.For(RuleKind.Lift, rule.RuleId),
          RuleKind.Lift,
          ResolveDependencies(rule, sourceNodes, sourceNodeIds, liftNodeIds, contractGraph))
        {
          ProducedSyntax = rule.Produces.Outputs,
          ConsumedSyntax = rule.Consumes.Inputs
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
          .SelectMany(node => node.Result.Values)
          .OfType<LiftedMarkRecord>()
          .DistinctBy(mark => (
            mark.RuleId,
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
    }

    private static IReadOnlyList<RuleGraphNode> CreateSourceNodes(
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<RuleConsumedSyntax> consumedInputs)
    {
        return seedMarks
          .GroupBy(mark => RuleNodeId.For(RuleKind.Mark, mark.RuleId))
          .Select(group => CreateSourceNode(
            group.Key,
            RuleKind.Mark,
            group.ToList(),
            consumedInputs))
          .Concat(propagatedMarks
            .GroupBy(mark => RuleNodeId.For(RuleKind.Propagate, mark.RuleId))
            .Select(group => CreateSourceNode(
              group.Key,
              RuleKind.Propagate,
              group.Select(mark => mark.Mark).ToList(),
              consumedInputs)))
          .ToList();
    }

    private static RuleGraphNode CreateSourceNode(
      RuleNodeId nodeId,
      RuleKind kind,
      IReadOnlyList<MarkRecord> marks,
      IReadOnlyList<RuleConsumedSyntax> consumedInputs)
    {
        var produces = RuleSyntaxContractValidator.CreateObservedProduces(marks, consumedInputs);
        return new RuleGraphNode(nodeId, kind, Array.Empty<RuleDependency>())
        {
            ProducedSyntax = produces.Outputs
        };
    }

    private static IReadOnlyList<RuleDependency> ResolveDependencies(
      RuleDefinitionLift rule,
      IReadOnlyList<RuleGraphNode> sourceNodes,
      IReadOnlySet<RuleNodeId> sourceNodeIds,
      IReadOnlySet<RuleNodeId> liftNodeIds,
      CompiledRuleStructureContractGraph contractGraph)
    {
        IReadOnlyList<RuleDependency> declared = rule.Consumes.Inputs.Count > 0
          ? contractGraph.Edges
            .Where(edge => edge.Consumer == rule.NodeId)
            .Select(edge => new RuleDependency(edge.Producer, edge.Input))
            .ToList()
          : Array.Empty<RuleDependency>();
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
        return RuleNodeResult.FromObservedValues(
          values.ToList(),
          new RuleProducesContract(node.ProducedSyntax));
    }

    private static RuleNodeResult CreateResult<T>(
      RuleProducesContract produces,
      IReadOnlyList<T> values)
    {
        var boxed = values.Cast<object>().ToList();
        return RuleNodeResult.FromValues(boxed, produces);
    }

    private static IReadOnlyList<object> GetValues(RuleGraphNode node, RuleNodeInputs inputs)
    {
        return node.Dependencies
          .SelectMany(dependency => inputs.GetOutputs(dependency.Producer, dependency.RequiredInput))
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

        var query = context.StructureViews.QueryStructureView(
          fragments,
          CpgRelationProfile.StructuralContainment,
          CpgQueryDirection.Bidirectional,
          new NLCPGTraversalBudget(16, 1, 1, 4096, 8192));
        if (query.Status != CpgQueryStatus.Complete || query.View is null)
        {
            return context;
        }

        return context.StructureViews.WithStructureView(query.View);
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

    internal static LiftedMarkRecord BindLiftedMarkRecord(RuleContext context, LiftedMarkRecord candidate)
    {
        return candidate with
        {
            Mark = MarkingEngine.BindMarkRecord(context, candidate.Mark),
            SourceMark = MarkingEngine.BindMarkRecord(context, candidate.SourceMark)
        };
    }
}
