using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rules;

namespace RoslynPrototype.Marking;

public sealed class MarkingEngine
{
    /// <summary>
    /// 执行所有启用规则的标记逻辑，并为每条命中补齐注解与主图节点绑定。
    /// </summary>
    /// <param name="context">当前规则执行所需的分析上下文。</param>
    /// <param name="root">待分析源码的语法树根节点。</param>
    /// <param name="rules">参与当前分析的删除规则集合。</param>
    /// <returns>去重后的种子标记集合。</returns>
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

    private static List<MarkRecord> RunRulesSerial(
      RuleContext context,
      SyntaxNode root,
      IReadOnlyList<RuleDefinitionMark> rules)
    {
        var seedMarks = new List<MarkRecord>();
        for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            seedMarks.AddRange(RunRule(context, root, rules[ruleIndex], ruleIndex));
        }

        return seedMarks;
    }

    private static List<MarkRecord> RunRulesInParallel(
      RuleContext context,
      SyntaxNode root,
      IReadOnlyList<RuleDefinitionMark> rules)
    {
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

    private static List<MarkRecord> RunRule(
      RuleContext context,
      SyntaxNode root,
      RuleDefinitionMark rule,
      int ruleOrder)
    {
        using var telemetryScope = context.BeginMarkRuleTelemetry(ruleOrder, rule.RuleId, rule.GroupKey);
        var producedMarks = new List<MarkRecord>();
        foreach (var mark in rule.Mark(context, root))
        {
            telemetryScope.RecordCandidateMark();
            ValidateMarkNode(rule, mark.SyntaxNode);
            if (mark.PrimaryGraphNode is null)
            {
                telemetryScope.RecordGraphBindingFallback();
            }

            producedMarks.Add(BindMarkRecord(context, mark, rule.GroupKey));
            telemetryScope.RecordAcceptedMark();
        }

        return producedMarks;
    }

    private static bool ShouldRunRulesInParallel(RuleContext context, int ruleCount)
    {
        return context.Runtime.ExecutionOptions.EnableGroupParallelism &&
          context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism > 1 &&
          ruleCount > 1;
    }

    /// <summary>
    /// 校验标记阶段产出的命中节点是否落在规则声明允许的语法种类范围内。
    /// </summary>
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

    /// <summary>
    /// 校验传播阶段产出的命中节点是否落在规则声明允许的语法种类范围内。
    /// </summary>
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

    /// <summary>
    /// 为命中记录补齐缺失的语法注解与主图节点绑定。
    /// </summary>
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
