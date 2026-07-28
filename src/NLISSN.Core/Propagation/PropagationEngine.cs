using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Rules;

namespace NLISSN.Core.Propagation;

public sealed class PropagationEngine
{
    // 为规则图执行器执行一个已准备好显式输入的传播节点；不读取 GroupKey。
    public static IReadOnlyList<PropagatedMarkRecord> ExecuteRule(
      RuleContext context,
      RuleDefinitionPropagate rule,
      IReadOnlyList<MarkRecord> inputMarks)
    {
        var ruleContext = BuildRuleContext(context, inputMarks);
        return rule.Propagate(ruleContext, inputMarks)
          .Select(candidate =>
          {
              MarkingEngine.ValidatePropagateNode(rule, candidate.Mark.SyntaxNode);
              return BindPropagatedMarkRecord(ruleContext, candidate, rule.GroupKey);
          })
          .ToList();
    }

    // 按 GroupKey 组织传播规则，并把同组链式传播收束成去重后的传播标记集合。
    public IReadOnlyList<PropagatedMarkRecord> Run(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<RuleDefinitionPropagate> rules)
    {
        var sourceNodes = seedMarks
          .GroupBy(mark => mark.RuleId, StringComparer.Ordinal)
          .Select(group => new RuleGraphNode(
            RuleNodeId.For(RuleKind.Mark, group.Key),
            RuleKind.Mark,
            new[] { RuleOutputKind.SeedMark },
            Array.Empty<RuleDependency>()))
          .ToList();
        var sourceById = sourceNodes.ToDictionary(node => node.NodeId);
        var virtualNodes = new Dictionary<RuleNodeId, RuleGraphNode>();
        var propagationNodeIds = rules
          .Select(rule => RuleNodeId.For(RuleKind.Propagate, rule.RuleId))
          .ToHashSet();
        var ruleNodes = rules.Select(rule =>
        {
            var declaredDependencies = RuleGraphDependencyCatalog.GetDependencies(
              rule,
              RuleKind.Propagate,
              rule.Dependencies);
            var dependencies = ResolveDependencies(
              declaredDependencies,
              sourceNodes,
              sourceById,
              propagationNodeIds,
              virtualNodes,
              seedMarks);
            return new RuleGraphNode(
              RuleNodeId.For(RuleKind.Propagate, rule.RuleId),
              RuleKind.Propagate,
              rule.ProducedOutputs,
              dependencies);
        }).ToList();
        var graph = new RuleGraphCompiler().Compile(sourceNodes.Concat(virtualNodes.Values).Concat(ruleNodes).ToList());
        var executionNodes = sourceNodes
          .Concat(virtualNodes.Values)
          .Select(node => new RuleGraphExecutionNode(
            node,
            (_, _) => Task.FromResult(CreateSourceResult(node, seedMarks))))
          .Concat(rules.Select(rule =>
          {
              var node = graph.Nodes.Single(candidate => candidate.NodeId == RuleNodeId.For(RuleKind.Propagate, rule.RuleId));
              return new RuleGraphExecutionNode(
                node,
                (inputs, _) => Task.FromResult(CreateResult(
                  rule.ProducedOutputs,
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
          .SelectMany(node => node.Result.GetOutputs(RuleOutputKind.PropagatedMark))
          .OfType<PropagatedMarkRecord>()
          .DistinctBy(mark => (
            mark.GroupKey ?? mark.RuleId,
            mark.RuleId,
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
    }

    private static PropagatedMarkRecord BindPropagatedMarkRecord(RuleContext context, PropagatedMarkRecord candidate, string? groupKey = null)
    {
        return candidate with
        {
            Mark = MarkingEngine.BindMarkRecord(context, candidate.Mark, groupKey),
            SourceMark = MarkingEngine.BindMarkRecord(context, candidate.SourceMark, groupKey),
            GroupKey = candidate.GroupKey ?? groupKey
        };
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

    private static IReadOnlyList<RuleDependency> ResolveDependencies(
      IReadOnlyList<RuleDependency> declaredDependencies,
      IReadOnlyList<RuleGraphNode> sourceNodes,
      IReadOnlyDictionary<RuleNodeId, RuleGraphNode> sourceById,
      IReadOnlySet<RuleNodeId> propagationNodeIds,
      IDictionary<RuleNodeId, RuleGraphNode> virtualNodes,
      IReadOnlyList<MarkRecord> seedMarks)
    {
        if (declaredDependencies.Count == 0)
        {
            return sourceNodes
              .Select(node => new RuleDependency(node.NodeId, RuleOutputKind.SeedMark))
              .ToList();
        }

        var dependencies = new List<RuleDependency>();
        foreach (var dependency in declaredDependencies)
        {
            if (sourceById.ContainsKey(dependency.Producer) ||
                propagationNodeIds.Contains(dependency.Producer) ||
                virtualNodes.ContainsKey(dependency.Producer))
            {
                dependencies.Add(dependency);
                continue;
            }

            if (dependency.RequiredOutput == RuleOutputKind.SeedMark)
            {
                dependencies.AddRange(sourceNodes.Select(node => new RuleDependency(node.NodeId, RuleOutputKind.SeedMark)));
                continue;
            }

            if (seedMarks.Any(mark => mark.OutputKind == dependency.RequiredOutput))
            {
                virtualNodes[dependency.Producer] = new RuleGraphNode(
                  dependency.Producer,
                  ParseKind(dependency.Producer),
                  new[] { dependency.RequiredOutput },
                  Array.Empty<RuleDependency>());
                dependencies.Add(dependency);
            }
        }

        return dependencies
          .GroupBy(dependency => dependency.Producer)
          .Select(group => group.First())
          .ToList();
    }

    private static RuleNodeResult CreateSourceResult(RuleGraphNode node, IReadOnlyList<MarkRecord> seedMarks)
    {
        var outputKind = node.ProducedOutputs.Single();
        var marks = outputKind == RuleOutputKind.SeedMark
          ? seedMarks.Where(mark => string.Equals(mark.RuleId, node.NodeId.Value["Mark:".Length..], StringComparison.Ordinal))
          : seedMarks.Where(mark => mark.OutputKind == outputKind);
        return RuleNodeResult.From(outputKind, marks.Cast<object>().ToArray());
    }

    private static RuleNodeResult CreateResult<T>(IReadOnlyList<RuleOutputKind> outputKinds, IReadOnlyList<T> values)
    {
        var boxed = values.Cast<object>().ToList();
        return RuleNodeResult.FromProducedOutputs(outputKinds, boxed);
    }

    private static IReadOnlyList<MarkRecord> GetInputMarks(RuleGraphNode node, RuleNodeInputs inputs)
    {
        return node.Dependencies
          .SelectMany(dependency => inputs.GetOutputs(dependency.Producer, dependency.RequiredOutput))
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

    private static RuleKind ParseKind(RuleNodeId nodeId)
    {
        return Enum.Parse<RuleKind>(nodeId.Value.Split(':', 2)[0], ignoreCase: false);
    }
}
