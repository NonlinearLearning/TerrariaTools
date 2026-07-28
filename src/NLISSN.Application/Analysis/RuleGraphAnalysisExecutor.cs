using Microsoft.CodeAnalysis;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Rules;

namespace NLISSN.Application;

internal sealed record RuleGraphAnalysisResult(
  IReadOnlyList<MarkRecord> SeedMarks,
  IReadOnlyList<PropagatedMarkRecord> PropagatedMarks,
  IReadOnlyList<LiftedMarkRecord> LiftedMarks,
  IReadOnlyList<RuleDecision> Decisions,
  IReadOnlyList<RuleGraphNodeTelemetry> Telemetry,
  RuleGraphExecutionMetrics? Metrics);

internal sealed class RuleGraphAnalysisExecutor
{
    public RuleGraphAnalysisResult Run(
      RuleContext context,
      SyntaxNode root,
      RulePipeline pipeline,
      CompiledRuleGraph graph)
    {
        var executionNodes = pipeline.Markers
          .Select(rule => CreateMarkerNode(context, rule, root, graph))
          .Concat(pipeline.Propagators.Select(rule => CreatePropagatorNode(context, rule, graph)))
          .Concat(pipeline.Lifters.Select(rule => CreateLifterNode(context, rule, graph)))
          .Concat(pipeline.Proposers.Select(rule => CreateProposerNode(context, rule, graph)))
          .Concat(CreateDisabledNodes(graph, pipeline))
          .ToList();
        var execution = new RuleGraphExecutor().ExecuteAsync(
            graph,
            executionNodes,
            context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        var seedMarks = execution.Nodes
          .SelectMany(node => node.Result.GetOutputs(RuleOutputKind.SeedMark))
          .OfType<MarkRecord>()
          .DistinctBy(mark => (mark.GroupKey ?? mark.RuleId, mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length))
          .ToList();
        var propagatedMarks = execution.Nodes
          .SelectMany(node => node.Result.GetOutputs(RuleOutputKind.PropagatedMark))
          .OfType<PropagatedMarkRecord>()
          .DistinctBy(mark => (
            mark.GroupKey ?? mark.RuleId,
            mark.RuleId,
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
        var liftedMarks = execution.Nodes
          .SelectMany(node => node.Result.GetOutputs(RuleOutputKind.LiftedMark))
          .OfType<LiftedMarkRecord>()
          .DistinctBy(mark => (
            mark.GroupKey ?? mark.RuleId,
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
        var units = execution.Nodes
          .SelectMany(node => node.Result.GetOutputs(RuleOutputKind.DecisionUnit))
          .OfType<DecisionUnit>()
          .ToList();
        var decisions = new RuleDecisionEngine().ResolveUnits(context, units, pipeline.Proposers);

        return new RuleGraphAnalysisResult(
          seedMarks,
          propagatedMarks,
          liftedMarks,
          decisions,
          execution.Telemetry ?? Array.Empty<RuleGraphNodeTelemetry>(),
          execution.Metrics);
    }

    private static RuleGraphExecutionNode CreateMarkerNode(
      RuleContext context,
      RuleDefinitionMark rule,
      SyntaxNode root,
      CompiledRuleGraph graph)
    {
        var node = FindNode(graph, rule, RuleKind.Mark);
        return new RuleGraphExecutionNode(
          node,
          (_, _) => Task.FromResult(CreateResult(
            rule.ProducedOutputs,
            new MarkingEngine().Run(context, root, new[] { rule }))));
    }

    private static RuleGraphExecutionNode CreatePropagatorNode(
      RuleContext context,
      RuleDefinitionPropagate rule,
      CompiledRuleGraph graph)
    {
        var node = FindNode(graph, rule, RuleKind.Propagate);
        return new RuleGraphExecutionNode(
          node,
          (inputs, _) => Task.FromResult(CreateResult(
            rule.ProducedOutputs,
            PropagationEngine.ExecuteRule(context, rule, GetMarks(node, inputs)))));
    }

    private static RuleGraphExecutionNode CreateLifterNode(
      RuleContext context,
      RuleDefinitionLift rule,
      CompiledRuleGraph graph)
    {
        var node = FindNode(graph, rule, RuleKind.Lift);
        return new RuleGraphExecutionNode(
          node,
          (inputs, _) =>
          {
              var values = GetValues(node, inputs);
              return Task.FromResult(CreateResult(
                rule.ProducedOutputs,
                MarkLiftingEngine.ExecuteRule(
                  context,
                  rule,
                  values.OfType<MarkRecord>().ToList(),
                  values.OfType<PropagatedMarkRecord>()
                    .Where(mark => mark.Payload is null)
                    .ToList(),
                  values.OfType<LiftedMarkRecord>().ToList())));
          });
    }

    private static RuleGraphExecutionNode CreateProposerNode(
      RuleContext context,
      RuleDefinitionPropose rule,
      CompiledRuleGraph graph)
    {
        var node = FindNode(graph, rule, RuleKind.Propose);
        return new RuleGraphExecutionNode(
          node,
          (inputs, _) =>
          {
              var values = GetValues(node, inputs);
              var units = rule.Propose(
                  context,
                  values.OfType<MarkRecord>().ToList(),
                  values.OfType<PropagatedMarkRecord>().ToList(),
                  values.OfType<LiftedMarkRecord>().ToList())
                .Select(unit => unit.GroupKey is null ? unit with { GroupKey = rule.GroupKey } : unit)
                .ToList();
              return Task.FromResult(CreateResult(rule.ProducedOutputs, units));
          });
    }

    private static RuleGraphNode FindNode(
      CompiledRuleGraph graph,
      IRuleDefinition rule,
      RuleKind kind)
    {
        var nodeId = RuleNodeId.For(kind, rule.RuleId);
        return graph.Nodes.Single(node => node.NodeId == nodeId);
    }

    private static IEnumerable<RuleGraphExecutionNode> CreateDisabledNodes(
      CompiledRuleGraph graph,
      RulePipeline pipeline)
    {
        var activeNodeIds = pipeline.Markers
          .Select(rule => RuleNodeId.For(RuleKind.Mark, rule.RuleId))
          .Concat(pipeline.Propagators.Select(rule => RuleNodeId.For(RuleKind.Propagate, rule.RuleId)))
          .Concat(pipeline.Lifters.Select(rule => RuleNodeId.For(RuleKind.Lift, rule.RuleId)))
          .Concat(pipeline.Proposers.Select(rule => RuleNodeId.For(RuleKind.Propose, rule.RuleId)))
          .ToHashSet();
        return graph.Nodes
          .Where(node => !activeNodeIds.Contains(node.NodeId))
          .Select(node => new RuleGraphExecutionNode(
            node,
            (_, _) => Task.FromResult(RuleNodeResult.Empty)));
    }

    private static RuleNodeResult CreateResult<T>(
      IReadOnlyList<RuleOutputKind> outputKinds,
      IReadOnlyList<T> values)
    {
        var boxedValues = values.Cast<object>().ToList();
        return RuleNodeResult.FromProducedOutputs(outputKinds, boxedValues);
    }

    private static IReadOnlyList<MarkRecord> GetMarks(RuleGraphNode node, RuleNodeInputs inputs)
    {
        return GetValues(node, inputs)
          .Select(value => value switch
          {
              MarkRecord mark => mark,
              PropagatedMarkRecord propagated => propagated.Mark,
              _ => null
          })
          .Where(mark => mark is not null)
          .Cast<MarkRecord>()
          .ToList();
    }

    private static IReadOnlyList<object> GetValues(RuleGraphNode node, RuleNodeInputs inputs)
    {
        return node.Dependencies
          .SelectMany(dependency => inputs.GetOutputs(dependency.Producer, dependency.RequiredOutput))
          .ToList();
    }
}
