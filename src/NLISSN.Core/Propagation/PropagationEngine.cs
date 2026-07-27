using NLISSN.Core.Analysis;
using NLISSN.Core.Marking;
using NLISSN.Rules;

namespace NLISSN.Core.Propagation;

public sealed class PropagationEngine
{
    // 按 GroupKey 组织传播规则，并把同组链式传播收束成去重后的传播标记集合。
    public IReadOnlyList<PropagatedMarkRecord> Run(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<RuleDefinitionPropagate> rules)
    {
        var seedMarksByGroupKey = seedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        // 组是传播隔离边界：同组规则可消费前序规则新增的标记，不同组之间互不影响。
        var groupedRules = rules
          .GroupBy(rule => rule.GroupKey, StringComparer.Ordinal)
          .Select(group => new PropagationRuleGroup(group.Key, group.ToList()))
          .Where(group =>
            seedMarksByGroupKey.TryGetValue(group.GroupKey, out var groupSeedMarks) &&
            groupSeedMarks.Count > 0)
          .ToList();
        var propagatedMarks = ShouldRunGroupsInParallel(context, groupedRules.Count)
          ? RunGroupsInParallel(context, groupedRules, seedMarksByGroupKey)
          : RunGroupsSerial(context, groupedRules, seedMarksByGroupKey);

        // 不同传播路径可能命中同一个语法节点，这里按规则和语法位置收口去重。
        return propagatedMarks
        .DistinctBy(mark => (
          RuleStageGroupKey.Get(mark),
          mark.RuleId,
          mark.Mark.SyntaxNode.SpanStart,
          mark.Mark.SyntaxNode.Span.Length,
          mark.Mark.SyntaxNode.RawKind))
        .ToList();
    }

    private static List<PropagatedMarkRecord> RunGroupsSerial(RuleContext context, IReadOnlyList<PropagationRuleGroup> groupedRules, IReadOnlyDictionary<string, List<MarkRecord>> seedMarksByGroupKey)
    {
        var propagatedMarks = new List<PropagatedMarkRecord>();
        foreach (var ruleGroup in groupedRules)
        {
            propagatedMarks.AddRange(RunGroup(context, ruleGroup, seedMarksByGroupKey[ruleGroup.GroupKey]));
        }

        return propagatedMarks;
    }

    private static List<PropagatedMarkRecord> RunGroupsInParallel(RuleContext context, IReadOnlyList<PropagationRuleGroup> groupedRules, IReadOnlyDictionary<string, List<MarkRecord>> seedMarksByGroupKey)
    {
        var orderedGroupMarks = context.Runtime.Scheduler.RunOrderedAsync(
            groupedRules.Count,
            context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
            (index, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ruleGroup = groupedRules[index];
                return Task.Run(
                  () => (IReadOnlyList<PropagatedMarkRecord>)RunGroup(
                    context,
                    ruleGroup,
                    seedMarksByGroupKey[ruleGroup.GroupKey]),
                  cancellationToken);
            },
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        return orderedGroupMarks.SelectMany(marks => marks).ToList();
    }

    private static List<PropagatedMarkRecord> RunGroup(RuleContext context, PropagationRuleGroup ruleGroup, IReadOnlyList<MarkRecord> groupSeedMarks)
    {
        var propagatedMarks = new List<PropagatedMarkRecord>();
        // 依次扩充同组工作集，使链式传播在本组内闭合，同时保持规则声明顺序。
        var groupMarks = new List<MarkRecord>(groupSeedMarks);
        foreach (var rule in ruleGroup.Rules)
        {
            var ruleContext = BuildRuleContext(context, groupMarks);
            var producedMarks = new List<PropagatedMarkRecord>();
            foreach (var propagatedMark in rule.Propagate(ruleContext, groupMarks))
            {
                MarkingEngine.ValidatePropagateNode(rule, propagatedMark.Mark.SyntaxNode);
                var boundMark = BindPropagatedMarkRecord(ruleContext, propagatedMark, rule.GroupKey);
                producedMarks.Add(boundMark);
                propagatedMarks.Add(boundMark);
            }

            foreach (var producedMark in producedMarks)
            {
                if (groupMarks.Any(existing =>
                      existing.SyntaxNode.SpanStart == producedMark.Mark.SyntaxNode.SpanStart &&
                      existing.SyntaxNode.Span.Length == producedMark.Mark.SyntaxNode.Span.Length &&
                      existing.SyntaxNode.RawKind == producedMark.Mark.SyntaxNode.RawKind))
                {
                    continue;
                }

                groupMarks.Add(producedMark.Mark);
            }
        }

        return propagatedMarks;
    }

    private static bool ShouldRunGroupsInParallel(RuleContext context, int groupCount)
    {
        return context.Runtime.ExecutionOptions.EnableGroupParallelism &&
          context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism > 1 &&
          groupCount > 1;
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

    private sealed record PropagationRuleGroup(
      string GroupKey,
      IReadOnlyList<RuleDefinitionPropagate> Rules);
}
