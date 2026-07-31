using NLCPG.Analysis;
using NL.Concurrency;
using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Lifting;
using NLISSN.Core.Pipeline;

namespace NLISSN.Core.Propagation;

public sealed class PropagationEngine
{
    // 为规则图执行器执行一个已准备好显式输入的传播节点。
    public static IReadOnlyList<PropagatedMarkRecord> ExecuteRule(
      RuleContext context,
      RuleDefinitionPropagate rule,
      IReadOnlyList<MarkRecord> inputMarks)
    {
        var ruleContext = BuildRuleContext(context, inputMarks);
        var results = rule.Propagate(ruleContext.CreatePropagationRuleContext(), inputMarks)
          .Select(candidate =>
          {
              var tagged = candidate with
              {
                  Mark = MarkingEngine.BindDeclaredSemanticTag(rule.Produces, candidate.Mark)
              };
              ValidatePropagateNode(rule, tagged.Mark.SyntaxNode);
              ValidatePropagationPayload(rule, tagged.Payload);
              MarkingEngine.ValidateProducedSyntax(rule.Produces, tagged.Mark);
              return BindPropagatedMarkRecord(ruleContext, tagged);
          })
          .ToList();
        context.Evidence.RecordPropagation(rule.RuleId, inputMarks, results);
        return results;
    }

    // 兼容入口也按规则图执行，并按产生规则和语法位置去重。
    public IReadOnlyList<PropagatedMarkRecord> Run(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<RuleDefinitionPropagate> rules)
    {
        var consumedInputs = rules
          .SelectMany(rule => rule.Consumes.Inputs)
          .ToList();
        var sourceNodes = seedMarks
          .GroupBy(mark => mark.RuleId, StringComparer.Ordinal)
          .Select(group =>
          {
              var produces = RuleSyntaxContractValidator.CreateObservedProduces(
                group.ToList(),
                consumedInputs);
              return new RuleGraphNode(
                RuleNodeId.For(RuleKind.Mark, group.Key),
                RuleKind.Mark,
                Array.Empty<RuleDependency>())
              {
                ProducedSyntax = produces.Outputs
              };
          })
          .ToList();
        var contractGraph = new RuleStructureContractGraphCompiler().Compile(sourceNodes
          .Select(node => new RuleStructureContractGraphNode(
            node.NodeId,
            RuleConsumesContract.Empty,
            new RuleProducesContract(node.ProducedSyntax)))
          .Concat(rules
          .Select(rule => new RuleStructureContractGraphNode(
            RuleNodeId.For(RuleKind.Propagate, rule.RuleId),
            rule.Consumes,
            rule.Produces)))
          .ToList());
        var ruleNodes = rules.Select(rule =>
        {
            IReadOnlyList<RuleDependency> dependencies = rule.Consumes.Inputs.Count > 0
              ? contractGraph.Edges
                .Where(edge => edge.Consumer == RuleNodeId.For(RuleKind.Propagate, rule.RuleId))
                .Select(edge => new RuleDependency(edge.Producer, edge.Input))
                .ToList()
              : Array.Empty<RuleDependency>();
            return new RuleGraphNode(
              RuleNodeId.For(RuleKind.Propagate, rule.RuleId),
              RuleKind.Propagate,
              dependencies)
            {
              ProducedSyntax = rule.Produces.Outputs
            };
        }).ToList();
        var graph = new RuleGraphCompiler().Compile(sourceNodes.Concat(ruleNodes).ToList());
        var executionNodes = sourceNodes
          .Select(node => new RuleGraphExecutionNode(
            node,
            (_, _) => Task.FromResult(CreateSourceResult(node, seedMarks))))
          .Concat(rules.Select(rule =>
          {
              var node = graph.Nodes.Single(candidate => candidate.NodeId == RuleNodeId.For(RuleKind.Propagate, rule.RuleId));
              return new RuleGraphExecutionNode(
                node,
                (inputs, _) => Task.FromResult(CreateResult(
                  rule.Produces,
                  ExecuteRule(context, rule, GetInputMarks(node, inputs)))));
          }))
          .ToList();
        var graphDegree = ConcurrencyExecutionPolicy.ResolveMaxDegreeOfParallelism(
          context.Runtime.ExecutionOptions.EnableGroupParallelism,
          context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism);
        var execution = new RuleGraphExecutor(context.Runtime.ConcurrencyPool).ExecuteAsync(
            graph,
            executionNodes,
            graphDegree,
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        return execution.Nodes
          .Where(node => node.NodeId.Value.StartsWith("Propagate:", StringComparison.Ordinal))
          .SelectMany(node => node.Result.Values)
          .OfType<PropagatedMarkRecord>()
          .DistinctBy(mark => (
            mark.RuleId,
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
    }

    private static PropagatedMarkRecord BindPropagatedMarkRecord(RuleContext context, PropagatedMarkRecord candidate)
    {
        var origins = candidate.Mark.Origins | candidate.SourceMark.Origins;
        return candidate with
        {
            Mark = MarkingEngine.BindMarkRecord(context, candidate.Mark with { Origins = origins }),
            SourceMark = MarkingEngine.BindMarkRecord(context, candidate.SourceMark)
        };
    }

    internal static void ValidatePropagateNode(RuleDefinitionPropagate rule, Microsoft.CodeAnalysis.SyntaxNode syntaxNode)
    {
        var nodeKind = (Microsoft.CodeAnalysis.CSharp.SyntaxKind)syntaxNode.RawKind;
        if (rule.AllowedPropagateNodeKinds.Contains(nodeKind))
        {
            return;
        }

        var allowedKinds = string.Join(", ", rule.AllowedPropagateNodeKinds);
        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted unsupported propagate node kind '{nodeKind}'. Allowed propagate node kinds: {allowedKinds}.");
    }

    private static void ValidatePropagationPayload(RuleDefinitionPropagate rule, object? payload)
    {
        if (payload is not ILiftPayload)
        {
            return;
        }

        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted a Lift-owned structural payload from Propagate.");
    }

    private static RuleContext BuildRuleContext(RuleContext context, IReadOnlyList<MarkRecord> marks)
    {
        var fragments = marks
          .Select(mark => mark.SyntaxNode)
          .Distinct()
          .ToList();
        if (fragments.Count == 0)
        {
            return context;
        }

        var query = context.QueryStructureView(
          fragments,
          CpgRelationProfile.StructuralContainment,
          CpgQueryDirection.Bidirectional,
          new NLCPGTraversalBudget(16, 1, 1, 4096, 8192));
        if (query.Status != CpgQueryStatus.Complete || query.View is null)
        {
            return context;
        }

        return context.WithStructureView(query.View);
    }

    private static RuleNodeResult CreateSourceResult(RuleGraphNode node, IReadOnlyList<MarkRecord> seedMarks)
    {
        var marks = seedMarks.Where(mark =>
          string.Equals(mark.RuleId, node.NodeId.Value["Mark:".Length..], StringComparison.Ordinal));
        return RuleNodeResult.FromObservedValues(
          marks.Cast<object>().ToList(),
          new RuleProducesContract(node.ProducedSyntax));
    }

    private static RuleNodeResult CreateResult<T>(
      RuleProducesContract produces,
      IReadOnlyList<T> values)
    {
        var boxed = values.Cast<object>().ToList();
        return RuleNodeResult.FromValues(boxed, produces);
    }

    private static IReadOnlyList<MarkRecord> GetInputMarks(RuleGraphNode node, RuleNodeInputs inputs)
    {
        return node.Dependencies
          .SelectMany(dependency => inputs.GetOutputs(dependency.Producer, dependency.RequiredInput))
          .Select(value => value switch
          {
              MarkRecord mark => mark,
              PropagatedMarkRecord propagated => propagated.Mark,
              _ => null
          })
          .Where(mark => mark is not null)
          .Cast<MarkRecord>()
          .DistinctBy(mark => (
            mark.SyntaxNode.SpanStart,
            mark.SyntaxNode.Span.Length,
            mark.SyntaxNode.RawKind))
          .ToList();
    }

}
