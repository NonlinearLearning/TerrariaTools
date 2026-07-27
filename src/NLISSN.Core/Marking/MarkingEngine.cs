using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Rules;

namespace NLISSN.Core.Marking;

public sealed class MarkingEngine
{
    // 执行所有标记规则，补齐图绑定后按规则组和语法位置去重返回种子标记。
    public IReadOnlyList<MarkRecord> Run(RuleContext context, SyntaxNode root, IReadOnlyList<RuleDefinitionMark> rules)
    {
        var seedMarks = ShouldRunRulesInParallel(context, rules.Count)
          ? RunRulesInParallel(context, root, rules)
          : RunRulesSerial(context, root, rules);

        // 同一规则可能通过多条路径命中同一个语法节点，这里按规则和语法位置去重。
        return seedMarks
        .DistinctBy(mark => (
          RuleStageGroupKey.Get(mark),
          mark.SyntaxNode.SpanStart,
          mark.SyntaxNode.Span.Length))
        .ToList();
    }

    private static List<MarkRecord> RunRulesSerial(RuleContext context, SyntaxNode root, IReadOnlyList<RuleDefinitionMark> rules)
    {
        var seedMarks = new List<MarkRecord>();
        for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            seedMarks.AddRange(RunRule(context, root, rules[ruleIndex], ruleIndex));
        }

        return seedMarks;
    }

    private static List<MarkRecord> RunRulesInParallel(RuleContext context, SyntaxNode root, IReadOnlyList<RuleDefinitionMark> rules)
    {
        // 调度器保留规则声明顺序，避免并发完成顺序改变后续去重和可观测结果。
        var orderedRuleMarks = context.Runtime.Scheduler.RunOrderedAsync(
            rules.Count,
            context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
            (index, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.Run(
                  () => (IReadOnlyList<MarkRecord>)RunRule(context, root, rules[index], index),
                  cancellationToken);
            },
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        return orderedRuleMarks.SelectMany(marks => marks).ToList();
    }

    private static List<MarkRecord> RunRule(RuleContext context, SyntaxNode root, RuleDefinitionMark rule, int ruleOrder)
    {
        var producedMarks = new List<MarkRecord>();
        foreach (var mark in rule.Mark(context, root))
        {
            ValidateMarkNode(rule, mark.SyntaxNode);
            producedMarks.Add(BindMarkRecord(context, mark, rule.GroupKey));
        }

        return producedMarks;
    }

    private static bool ShouldRunRulesInParallel(RuleContext context, int ruleCount)
    {
        return context.Runtime.ExecutionOptions.EnableGroupParallelism &&
          context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism > 1 &&
          ruleCount > 1;
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
