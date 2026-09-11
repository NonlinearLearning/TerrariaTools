using System.Diagnostics;
using RoslynPrototype.Analysis;
using RoslynPrototype.Marking;
using Rules;

namespace RoslynPrototype.Propagation;

public sealed class PropagationEngine
{
    public PropagationTelemetry LastTelemetry { get; private set; } = PropagationTelemetry.Empty;

    /// <summary>
    /// 按规则分别执行传播，只允许规则扩展自己产出的种子标记。
    /// </summary>
    /// <param name="context">当前规则执行所需的分析上下文。</param>
    /// <param name="seedMarks">标记阶段直接命中的种子标记集合。</param>
    /// <param name="rules">参与当前分析的删除规则集合。</param>
    /// <returns>去重后的传播标记集合。</returns>
    public IReadOnlyList<PropagatedMarkRecord> Run(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<RuleDefinitionPropagate> rules)
    {
        var seedMarksByGroupKey = seedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var groupedRules = rules
          .GroupBy(rule => rule.GroupKey, StringComparer.Ordinal)
          .Select(group => new PropagationRuleGroup(group.Key, group.ToList()))
          .Where(group =>
            seedMarksByGroupKey.TryGetValue(group.GroupKey, out var groupSeedMarks) &&
            groupSeedMarks.Count > 0)
          .ToList();
        var groupResults = ShouldRunGroupsInParallel(context, groupedRules.Count)
          ? RunGroupsInParallel(context, groupedRules, seedMarksByGroupKey)
          : RunGroupsSerial(context, groupedRules, seedMarksByGroupKey);
        LastTelemetry = new PropagationTelemetry(
          groupResults.Select(result => result.Telemetry).ToList(),
          groupResults.SelectMany(result => result.RuleTelemetry).ToList());
        var propagatedMarks = groupResults.SelectMany(result => result.Marks).ToList();

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

    private static List<PropagationGroupResult> RunGroupsSerial(
      RuleContext context,
      IReadOnlyList<PropagationRuleGroup> groupedRules,
      IReadOnlyDictionary<string, List<MarkRecord>> seedMarksByGroupKey)
    {
        var groupResults = new List<PropagationGroupResult>();
        foreach (var ruleGroup in groupedRules)
        {
            groupResults.Add(RunGroup(context, ruleGroup, seedMarksByGroupKey[ruleGroup.GroupKey]));
        }

        return groupResults;
    }

    private static List<PropagationGroupResult> RunGroupsInParallel(
      RuleContext context,
      IReadOnlyList<PropagationRuleGroup> groupedRules,
      IReadOnlyDictionary<string, List<MarkRecord>> seedMarksByGroupKey)
    {
        var orderedGroupMarks = context.Runtime.Scheduler.RunOrderedAsync(
            groupedRules.Count,
            context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
            (index, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ruleGroup = groupedRules[index];
                return Task.Run(
                  () => RunGroup(
                    context,
                    ruleGroup,
                    seedMarksByGroupKey[ruleGroup.GroupKey]),
                  cancellationToken);
            },
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        return orderedGroupMarks.ToList();
    }

    private static PropagationGroupResult RunGroup(
      RuleContext context,
      PropagationRuleGroup ruleGroup,
      IReadOnlyList<MarkRecord> groupSeedMarks)
    {
        var propagatedMarks = new List<PropagatedMarkRecord>();
        var ruleTelemetry = new List<PropagationRuleTelemetry>();
        var groupMarks = new List<MarkRecord>(groupSeedMarks);
        var groupMarkSyntaxKeys = groupMarks
          .Select(CreateSyntaxKey)
          .ToHashSet();
        foreach (var rule in ruleGroup.Rules)
        {
            var inputMarkCount = groupMarks.Count;
            var ruleStopwatch = Stopwatch.StartNew();
            var ruleExecution = BuildRuleExecutionContext(context, groupMarks, rule.RequiresStructureView);
            var producedMarks = new List<PropagatedMarkRecord>();
            foreach (var propagatedMark in rule.Propagate(ruleExecution.Context, groupMarks))
            {
                MarkingEngine.ValidatePropagateNode(rule, propagatedMark.Mark.SyntaxNode);
                var boundMark = BindPropagatedMarkRecord(ruleExecution.Context, propagatedMark, rule.GroupKey);
                producedMarks.Add(boundMark);
                propagatedMarks.Add(boundMark);
            }

            var groupMembershipDuplicateCount = 0;
            foreach (var producedMark in producedMarks)
            {
                if (!groupMarkSyntaxKeys.Add(CreateSyntaxKey(producedMark.Mark)))
                {
                    groupMembershipDuplicateCount++;
                    continue;
                }

                groupMarks.Add(producedMark.Mark);
            }

            ruleStopwatch.Stop();
            ruleTelemetry.Add(new PropagationRuleTelemetry(
              ruleGroup.GroupKey,
              rule.RuleId,
              InputMarkCount: inputMarkCount,
              ProducedMarkCount: producedMarks.Count,
              GroupMembershipLookupCount: producedMarks.Count,
              GroupMembershipDuplicateCount: groupMembershipDuplicateCount,
              StructureViewRequestCount: ruleExecution.StructureViewRequestCount,
              StructureViewCacheHitCount: ruleExecution.StructureViewCacheHitCount,
              StructureViewCacheMissCount: ruleExecution.StructureViewCacheMissCount,
              StructureViewNodeCount: ruleExecution.StructureViewNodeCount,
              StructureViewEdgeCount: ruleExecution.StructureViewEdgeCount,
              ElapsedMilliseconds: ruleStopwatch.ElapsedMilliseconds));
        }

        return new PropagationGroupResult(
          propagatedMarks,
          new PropagationGroupTelemetry(
            ruleGroup.GroupKey,
            groupSeedMarks.Count,
            propagatedMarks.Count,
            ruleGroup.Rules.Count),
          ruleTelemetry);
    }

    private static bool ShouldRunGroupsInParallel(RuleContext context, int groupCount)
    {
        return context.Runtime.ExecutionOptions.EnableGroupParallelism &&
          context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism > 1 &&
          groupCount > 1;
    }

    private static PropagatedMarkRecord BindPropagatedMarkRecord(
        RuleContext context,
        PropagatedMarkRecord candidate,
        string? groupKey = null)
    {
        return candidate with
        {
            Mark = MarkingEngine.BindMarkRecord(context, candidate.Mark, groupKey),
            SourceMark = MarkingEngine.BindMarkRecord(context, candidate.SourceMark, groupKey),
            GroupKey = candidate.GroupKey ?? groupKey
        };
    }

    private static (int SpanStart, int SpanLength, int RawKind) CreateSyntaxKey(MarkRecord mark)
    {
        var syntaxNode = mark.SyntaxNode;
        return (syntaxNode.SpanStart, syntaxNode.Span.Length, syntaxNode.RawKind);
    }

    private static RuleExecutionContext BuildRuleExecutionContext(
      RuleContext context,
      IReadOnlyList<MarkRecord> marks,
      bool requiresStructureView)
    {
        if (!requiresStructureView)
        {
            return new RuleExecutionContext(context, 0, 0, 0, 0, 0);
        }

        var fragments = marks
          .Select(mark => mark.SyntaxNode)
          .Distinct()
          .ToList();
        if (fragments.Count == 0)
        {
            return new RuleExecutionContext(context, 0, 0, 0, 0, 0);
        }

        var cacheTelemetryBefore = context.StructureViewCacheTelemetry;
        var structureView = context.StructureViews.BuildStructureView(fragments);
        var cacheTelemetryAfter = context.StructureViewCacheTelemetry;
        return new RuleExecutionContext(
          context.StructureViews.WithStructureView(structureView),
          1,
          (int)(cacheTelemetryAfter.CacheHitCount - cacheTelemetryBefore.CacheHitCount),
          (int)(cacheTelemetryAfter.CacheMissCount - cacheTelemetryBefore.CacheMissCount),
          structureView.Nodes.Count,
          structureView.Edges.Count);
    }

    private sealed record PropagationRuleGroup(
      string GroupKey,
      IReadOnlyList<RuleDefinitionPropagate> Rules);

    private sealed record RuleExecutionContext(
      RuleContext Context,
      int StructureViewRequestCount,
      int StructureViewCacheHitCount,
      int StructureViewCacheMissCount,
      int StructureViewNodeCount,
      int StructureViewEdgeCount);

    private sealed record PropagationGroupResult(
      IReadOnlyList<PropagatedMarkRecord> Marks,
      PropagationGroupTelemetry Telemetry,
      IReadOnlyList<PropagationRuleTelemetry> RuleTelemetry);
}

public sealed record PropagationTelemetry(
  IReadOnlyList<PropagationGroupTelemetry> GroupTelemetry,
  IReadOnlyList<PropagationRuleTelemetry> RuleTelemetry)
{
    public static PropagationTelemetry Empty { get; } = new(
      Array.Empty<PropagationGroupTelemetry>(),
      Array.Empty<PropagationRuleTelemetry>());
}

public sealed record PropagationGroupTelemetry(
  string GroupKey,
  int InputMarkCount,
  int PropagatedMarkCount,
  int RuleCount);

public sealed record PropagationRuleTelemetry(
  string GroupKey,
  string RuleId,
  int InputMarkCount,
  int ProducedMarkCount,
  int GroupMembershipLookupCount,
  int GroupMembershipDuplicateCount,
  int StructureViewRequestCount,
  int StructureViewCacheHitCount,
  int StructureViewCacheMissCount,
  int StructureViewNodeCount,
  int StructureViewEdgeCount,
  long ElapsedMilliseconds);
