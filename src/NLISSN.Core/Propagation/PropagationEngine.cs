using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
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
        return rule.Propagate(ruleContext, inputMarks)
          .Select(candidate =>
          {
              ValidatePropagateNode(rule, candidate.Mark.SyntaxNode);
              MarkingEngine.ValidateProducedStructure(rule.Produces, candidate.Mark);
              return BindPropagatedMarkRecord(ruleContext, candidate);
          })
          .ToList();
    }

    // 兼容入口也按规则图执行，并按产生规则和语法位置去重。
    public IReadOnlyList<PropagatedMarkRecord> Run(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<RuleDefinitionPropagate> rules)
    {
        var consumedStructures = rules
          .SelectMany(rule => rule.Consumes.Structures)
          .ToList();
        var sourceNodes = seedMarks
          .GroupBy(mark => mark.RuleId, StringComparer.Ordinal)
          .Select(group =>
          {
              var produces = RuleStructureContractValidator.CreateObservedProduces(
                group.ToList(),
                consumedStructures);
              return new RuleGraphNode(
                RuleNodeId.For(RuleKind.Mark, group.Key),
                RuleKind.Mark,
                Array.Empty<RuleDependency>())
              {
                ProducedStructures = produces.Structures
              };
          })
          .ToList();
        var contractGraph = new RuleStructureContractGraphCompiler().Compile(sourceNodes
          .Select(node => new RuleStructureContractGraphNode(
            node.NodeId,
            node.Kind,
            RuleConsumesContract.Empty,
            new RuleProducesContract(node.ProducedStructures)))
          .Concat(rules
          .Select(rule => new RuleStructureContractGraphNode(
            rule.NodeId,
            RuleKind.Propagate,
            rule.Consumes,
            rule.Produces)))
          .ToList());
        var ruleNodes = rules.Select(rule =>
        {
            var dependencies = rule.Consumes.Structures.Count > 0
              ? contractGraph.Edges
                .Where(edge => edge.Consumer == rule.NodeId)
                .Select(edge => new RuleDependency(
                  edge.Producer,
                  edge.Selector))
                .ToList()
              : sourceNodes.Select(node => new RuleDependency(node.NodeId)).ToList();
            return new RuleGraphNode(
              RuleNodeId.For(RuleKind.Propagate, rule.RuleId),
              RuleKind.Propagate,
              dependencies)
            {
              ProducedStructures = rule.Produces.Structures,
              ConsumedStructures = rule.Consumes.Structures
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
        var execution = new RuleGraphExecutor(context.Runtime.ConcurrencyPool).ExecuteAsync(
            graph,
            executionNodes,
            context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
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
        return candidate with
        {
            Mark = MarkingEngine.BindMarkRecord(context, candidate.Mark),
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

        var structureView = context.StructureViews.BuildStructureView(fragments);
        return context.StructureViews.WithStructureView(structureView);
    }

    private static RuleNodeResult CreateSourceResult(RuleGraphNode node, IReadOnlyList<MarkRecord> seedMarks)
    {
        var marks = seedMarks.Where(mark =>
          string.Equals(mark.RuleId, node.NodeId.Value["Mark:".Length..], StringComparison.Ordinal));
        return RuleNodeResult.FromObservedValues(
          marks.Cast<object>().ToList(),
          new RuleProducesContract(node.ProducedStructures));
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
          .SelectMany(dependency => dependency.RequiredStructure is { } selector
            ? inputs.GetOutputs(dependency.Producer, selector)
            : inputs.GetValues(dependency.Producer))
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
