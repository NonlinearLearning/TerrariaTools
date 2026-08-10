using Microsoft.CodeAnalysis;
using NL.Concurrency;
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
      AnalysisSession session,
      SyntaxNode root,
      RulePipeline pipeline,
      CompiledRuleGraph graph)
    {
        var propagationRegion = new PropagationRegion(session, pipeline.Propagators);
        var executionNodes = pipeline.Markers
          .Select(rule => CreateMarkerNode(session, rule, root, graph))
          .Concat(pipeline.Propagators.Select(rule =>
            CreatePropagatorNode(rule, graph, propagationRegion)))
          .Concat(pipeline.Lifters.Select(rule => CreateLifterNode(session, rule, graph)))
          .Concat(pipeline.Proposers.Select(rule => CreateProposerNode(session, rule, graph)))
          .Concat(CreateDisabledNodes(graph, pipeline))
          .ToList();
        var graphDegree = ConcurrencyExecutionPolicy.ResolveMaxDegreeOfParallelism(
          session.Runtime.ExecutionOptions.EnableGroupParallelism,
          session.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism);
        var execution = new RuleGraphExecutor(session.Runtime.ConcurrencyPool).ExecuteAsync(
            graph,
            executionNodes,
            graphDegree,
            session.Runtime.ExecutionOptions.CancellationToken)
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
        var decisions = new RuleDecisionEngine().ResolveUnits(session, units, pipeline.Proposers);
        var nodeStatuses = execution.Nodes.ToDictionary(node => node.NodeId, node => node.Status);
        session.Evidence.RecordNodeStatuses(nodeStatuses);
        session.Evidence.RecordEmptyOutputs(execution.Nodes);
        var evidence = session.Evidence.Complete(decisions);
        var validationReport = IsValidationEnabled(session)
          ? new RuleBindingValidator()
             .Validate(session, graph, execution)
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

    private static bool IsValidationEnabled(AnalysisSession session)
    {
        return session.Settings.ValidateBindings;
    }

    private static RuleGraphExecutionNode CreateMarkerNode(
      AnalysisSession session,
      RuleDefinitionMark rule,
      SyntaxNode root,
      CompiledRuleGraph graph)
    {
        var node = FindNode(graph, rule, RuleKind.Mark);
        return new RuleGraphExecutionNode(
          node,
          (_, _) => Task.FromResult(CreateResult(
            rule.Produces,
             MarkingEngine.ExecuteRule(session, root, rule))));
    }

    private static RuleGraphExecutionNode CreatePropagatorNode(
      RuleDefinitionPropagate rule,
      CompiledRuleGraph graph,
      PropagationRegion propagationRegion)
    {
        var node = FindNode(graph, rule, RuleKind.Propagate);
        return new RuleGraphExecutionNode(
          node,
          (inputs, _) =>
          {
              var seedMarks = GetValues(node, inputs)
                .OfType<MarkRecord>()
                .DistinctBy(mark => (
                  mark.RuleId,
                  mark.SyntaxNode.SpanStart,
                  mark.SyntaxNode.Span.Length,
                  mark.SyntaxNode.RawKind,
                  mark.SemanticTag))
                .ToList();
              var outputs = propagationRegion.Run(seedMarks)
                .Where(mark => string.Equals(mark.RuleId, rule.RuleId, StringComparison.Ordinal))
                .ToList();
              return Task.FromResult(CreateResult(rule.Produces, outputs));
          });
    }

    private static RuleGraphExecutionNode CreateLifterNode(
      AnalysisSession session,
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
                   session,
                  rule,
                  values.OfType<MarkRecord>().ToList(),
                  values.OfType<PropagatedMarkRecord>().ToList(),
                  values.OfType<LiftedMarkRecord>().ToList())));
          });
    }

    private static RuleGraphExecutionNode CreateProposerNode(
      AnalysisSession session,
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
                   session.CreateProposeContext(),
                  values.OfType<MarkRecord>().ToList(),
                  values.OfType<PropagatedMarkRecord>().ToList(),
                  values.OfType<LiftedMarkRecord>().ToList())
                .ToList();
               session.Evidence.RecordProposal(rule.RuleId, values, units);
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

    private sealed class PropagationRegion
    {
        private readonly object _gate = new();
        private readonly AnalysisSession _session;
        private readonly IReadOnlyList<RuleDefinitionPropagate> _rules;
        private IReadOnlyList<PropagatedMarkRecord>? _results;

        public PropagationRegion(
          AnalysisSession session,
          IReadOnlyList<RuleDefinitionPropagate> rules)
        {
            _session = session;
            _rules = rules;
        }

        public IReadOnlyList<PropagatedMarkRecord> Run(IReadOnlyList<MarkRecord> seedMarks)
        {
            lock (_gate)
            {
                _results ??= new PropagationEngine().Run(_session, seedMarks, _rules);
                return _results;
            }
        }
    }
}
