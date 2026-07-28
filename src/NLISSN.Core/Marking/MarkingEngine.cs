using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Rules;

namespace NLISSN.Core.Marking;

public sealed class MarkingEngine
{
    // 执行所有标记规则，补齐图绑定后按规则节点和语法位置去重返回种子标记。
    public IReadOnlyList<MarkRecord> Run(RuleContext context, SyntaxNode root, IReadOnlyList<RuleDefinitionMark> rules)
    {
        var nodeIds = rules.Select(rule => RuleNodeId.For(RuleKind.Mark, rule.RuleId)).ToHashSet();
        var nodes = rules.Select(rule => new RuleGraphNode(
          RuleNodeId.For(RuleKind.Mark, rule.RuleId),
          RuleKind.Mark,
          rule.ProducedOutputs,
          RuleGraphDependencyCatalog.GetDependencies(rule, RuleKind.Mark, rule.Dependencies)
            .Where(dependency => nodeIds.Contains(dependency.Producer))
            .ToList())).ToList();
        var graph = new RuleGraphCompiler().Compile(nodes);
        var executionNodes = rules.Select(rule =>
        {
            var node = graph.Nodes.Single(candidate => candidate.NodeId == RuleNodeId.For(RuleKind.Mark, rule.RuleId));
            return new RuleGraphExecutionNode(
              node,
              (_, _) => Task.FromResult(CreateResult(rule.ProducedOutputs, RunRule(context, root, rule))));
        }).ToList();
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
          .ToList();

        // 同一规则可能通过多条路径命中同一个语法节点，这里按规则和语法位置去重。
        return seedMarks
        .DistinctBy(mark => (
          RuleStageGroupKey.Get(mark),
          mark.SyntaxNode.SpanStart,
          mark.SyntaxNode.Span.Length))
        .ToList();
    }

    private static List<MarkRecord> RunRule(RuleContext context, SyntaxNode root, RuleDefinitionMark rule)
    {
        var producedMarks = new List<MarkRecord>();
        foreach (var mark in rule.Mark(context, root))
        {
            ValidateMarkNode(rule, mark.SyntaxNode);
            producedMarks.Add(BindMarkRecord(context, mark, rule.GroupKey));
        }

        return producedMarks;
    }

    private static RuleNodeResult CreateResult<T>(IReadOnlyList<RuleOutputKind> outputKinds, IReadOnlyList<T> values)
    {
        var boxed = values.Cast<object>().ToList();
        return RuleNodeResult.FromProducedOutputs(outputKinds, boxed);
    }

    internal static void ValidateMarkNode(RuleDefinitionMark rule, SyntaxNode syntaxNode)
    {
        var nodeKind = (SyntaxKind)syntaxNode.RawKind;
        if (rule.AllowedMarkNodeKinds.Contains(nodeKind))
        {
            return;
        }

        var allowedKinds = string.Join(", ", rule.AllowedMarkNodeKinds);
        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted unsupported mark node kind '{nodeKind}'. Allowed mark node kinds: {allowedKinds}.");
    }

    internal static void ValidatePropagateNode(RuleDefinitionPropagate rule, SyntaxNode syntaxNode)
    {
        var nodeKind = (SyntaxKind)syntaxNode.RawKind;
        if (rule.AllowedPropagateNodeKinds.Contains(nodeKind))
        {
            return;
        }

        var allowedKinds = string.Join(", ", rule.AllowedPropagateNodeKinds);
        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted unsupported propagate node kind '{nodeKind}'. Allowed propagate node kinds: {allowedKinds}.");
    }

    internal static MarkRecord BindMarkRecord(RuleContext context, MarkRecord candidate, string? groupKey = null)
    {
        var annotation = candidate.Annotation ?? new SyntaxAnnotation("RuleHitNode", Guid.NewGuid().ToString("N"));
        var primaryGraphNode = candidate.PrimaryGraphNode;
        if (primaryGraphNode is null)
        {
            context.GraphBinding.TryResolvePrimaryGraphNode(candidate.SyntaxNode, out primaryGraphNode);
        }

        if (primaryGraphNode is null)
        {
            throw new InvalidOperationException(
              $"Could not bind syntax node '{candidate.SyntaxNode.Kind()}' to a graph node.");
        }

        return candidate with
        {
            Annotation = annotation,
            PrimaryGraphNode = primaryGraphNode,
            GroupKey = candidate.GroupKey ?? groupKey
        };
    }
}
