using Microsoft.CodeAnalysis;
using NLISSN.Core.Decision;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Validation;

namespace NLISSN.Application;

internal sealed record RuleGraphAnalysisResult(
  IReadOnlyList<MarkRecord> SeedMarks,
  IReadOnlyList<PropagatedMarkRecord> PropagatedMarks,
  IReadOnlyList<LiftedMarkRecord> LiftedMarks,
  IReadOnlyList<RuleDecision> Decisions,
  AnalysisEvidenceGraph Evidence,
  AnalysisValidationReport? ValidationReport,
  IReadOnlyList<RuleGraphNodeTelemetry> Telemetry,
  IReadOnlyDictionary<RuleNodeId, RuleGraphNodeStatus> NodeStatuses,
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
        var graphDegree = context.Runtime.ExecutionOptions.EnableGroupParallelism
          ? context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism
          : 1;
        var execution = new RuleGraphExecutor(context.Runtime.ConcurrencyPool).ExecuteAsync(
            graph,
            executionNodes,
            graphDegree,
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        var seedMarks = execution.Nodes
          .Where(node => node.NodeId.Value.StartsWith("Mark:", StringComparison.Ordinal))
          .SelectMany(node => node.Result.Values)
          .OfType<MarkRecord>()
          .DistinctBy(mark => (mark.RuleId, mark.SyntaxNode.SpanStart, mark.SyntaxNode.Span.Length))
          .ToList();
        var propagatedMarks = execution.Nodes
          .Where(node => node.NodeId.Value.StartsWith("Propagate:", StringComparison.Ordinal))
          .SelectMany(node => node.Result.Values)
          .OfType<PropagatedMarkRecord>()
          .DistinctBy(mark => (
            mark.RuleId,
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
        var liftedMarks = execution.Nodes
          .Where(node => node.NodeId.Value.StartsWith("Lift:", StringComparison.Ordinal))
          .SelectMany(node => node.Result.Values)
          .OfType<LiftedMarkRecord>()
          .DistinctBy(mark => (
            mark.RuleId,
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
        var units = execution.Nodes
          .Where(node => node.NodeId.Value.StartsWith("Propose:", StringComparison.Ordinal))
          .SelectMany(node => node.Result.Values)
          .OfType<DecisionUnit>()
          .ToList();
        var decisions = new RuleDecisionEngine().ResolveUnits(context, units, pipeline.Proposers);
        var nodeStatuses = execution.Nodes.ToDictionary(node => node.NodeId, node => node.Status);
        context.Evidence.RecordNodeStatuses(nodeStatuses);
        context.Evidence.RecordEmptyOutputs(execution.Nodes);
        var evidence = context.Evidence.Complete(decisions);
        var validationReport = IsValidationEnabled(context)
          ? new RuleBindingValidator()
            .Validate(context, graph, execution)
            .Combine(new DecisionBindingValidator().Validate(root, units, evidence.Decisions, evidence.Graph))
          : null;

        return new RuleGraphAnalysisResult(
          seedMarks,
          propagatedMarks,
          liftedMarks,
          evidence.Decisions,
          evidence.Graph,
          validationReport,
          execution.Telemetry ?? Array.Empty<RuleGraphNodeTelemetry>(),
          nodeStatuses,
          execution.Metrics);
    }

    private static bool IsValidationEnabled(RuleContext context)
    {
        return context.TryGetOption("validate-bindings", out var value) &&
          (string.IsNullOrEmpty(value) || bool.TryParse(value, out var enabled) && enabled);
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
            rule.Produces,
            MarkingEngine.ExecuteRule(context, root, rule))));
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
            rule.Produces,
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
                rule.Produces,
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
                .ToList();
              context.Evidence.RecordProposal(rule.RuleId, values, units);
              return Task.FromResult(CreateResult(rule.Produces, units));
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
            (_, _) => Task.FromResult(RuleNodeResult.Empty),
            RuleGraphNodeStatus.Disabled));
    }

    private static RuleNodeResult CreateResult<T>(
      RuleProducesContract produces,
      IReadOnlyList<T> values)
    {
        var boxedValues = values.Cast<object>().ToList();
        return RuleNodeResult.FromValues(boxedValues, produces);
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
          .SelectMany(dependency => dependency.RequiredInput is { } input
            ? inputs.GetOutputs(dependency.Producer, input)
            : inputs.GetValues(dependency.Producer))
          .ToList();
    }
}
