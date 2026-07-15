using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Analysis;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Lifting;

public sealed class MarkLiftingEngine
{
    public IReadOnlyList<LiftedMarkRecord> Run(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks, IReadOnlyList<RuleDefinitionLift> rules)
    {
        var liftEligiblePropagatedMarks = propagatedMarks
          .Where(mark => mark.Payload is null)
          .ToList();
        var seedMarksByGroupKey = seedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<MarkRecord>)group.ToList(),
            StringComparer.Ordinal);
        var propagatedMarksByGroupKey = liftEligiblePropagatedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<PropagatedMarkRecord>)group.ToList(),
            StringComparer.Ordinal);
        var groupedRules = rules
          .GroupBy(rule => rule.GroupKey, StringComparer.Ordinal)
          .Select(group => new LiftRuleGroup(group.Key, group.ToList()))
          .Where(group =>
            seedMarksByGroupKey.ContainsKey(group.GroupKey) ||
            propagatedMarksByGroupKey.ContainsKey(group.GroupKey))
          .ToList();
        var liftedMarks = ShouldRunGroupsInParallel(context, groupedRules.Count)
          ? RunGroupsInParallel(context, groupedRules, seedMarksByGroupKey, propagatedMarksByGroupKey)
          : RunGroupsSerial(context, groupedRules, seedMarksByGroupKey, propagatedMarksByGroupKey);

        return liftedMarks
          .DistinctBy(mark => (
            RuleStageGroupKey.Get(mark),
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
    }

    private static List<LiftedMarkRecord> RunGroupsSerial(
      RuleContext context,
      IReadOnlyList<LiftRuleGroup> groupedRules,
      IReadOnlyDictionary<string, IReadOnlyList<MarkRecord>> seedMarksByGroupKey,
      IReadOnlyDictionary<string, IReadOnlyList<PropagatedMarkRecord>> propagatedMarksByGroupKey)
    {
        var liftedMarks = new List<LiftedMarkRecord>();
        foreach (var ruleGroup in groupedRules)
        {
            liftedMarks.AddRange(RunRule(
              context,
              ruleGroup,
              seedMarksByGroupKey,
              propagatedMarksByGroupKey));
        }

        return liftedMarks;
    }

    private static List<LiftedMarkRecord> RunGroupsInParallel(
      RuleContext context,
      IReadOnlyList<LiftRuleGroup> groupedRules,
      IReadOnlyDictionary<string, IReadOnlyList<MarkRecord>> seedMarksByGroupKey,
      IReadOnlyDictionary<string, IReadOnlyList<PropagatedMarkRecord>> propagatedMarksByGroupKey)
    {
        var orderedLiftedMarks = context.Runtime.Scheduler.RunOrderedAsync(
            groupedRules.Count,
            context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
            (index, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.Run(
                  () => (IReadOnlyList<LiftedMarkRecord>)RunRule(
                    context,
                    groupedRules[index],
                    seedMarksByGroupKey,
                    propagatedMarksByGroupKey),
                  cancellationToken);
            },
            context.Runtime.ExecutionOptions.CancellationToken)
          .GetAwaiter()
          .GetResult();

        return orderedLiftedMarks.SelectMany(marks => marks).ToList();
    }

    private static List<LiftedMarkRecord> RunRule(
      RuleContext context,
      LiftRuleGroup ruleGroup,
      IReadOnlyDictionary<string, IReadOnlyList<MarkRecord>> seedMarksByGroupKey,
      IReadOnlyDictionary<string, IReadOnlyList<PropagatedMarkRecord>> propagatedMarksByGroupKey)
    {
        var liftedMarks = new List<LiftedMarkRecord>();
        foreach (var rule in ruleGroup.Rules)
        {
            liftedMarks.AddRange(RunRule(context, rule, seedMarksByGroupKey, propagatedMarksByGroupKey));
        }

        return liftedMarks;
    }

    private static List<LiftedMarkRecord> RunRule(
      RuleContext context,
      RuleDefinitionLift rule,
      IReadOnlyDictionary<string, IReadOnlyList<MarkRecord>> seedMarksByGroupKey,
      IReadOnlyDictionary<string, IReadOnlyList<PropagatedMarkRecord>> propagatedMarksByGroupKey)
    {
        seedMarksByGroupKey.TryGetValue(rule.GroupKey, out var ruleSeedMarks);
        propagatedMarksByGroupKey.TryGetValue(rule.GroupKey, out var rulePropagatedMarks);
        if ((ruleSeedMarks is null || ruleSeedMarks.Count == 0) &&
            (rulePropagatedMarks is null || rulePropagatedMarks.Count == 0))
        {
            return new List<LiftedMarkRecord>();
        }

        var producedMarks = new List<LiftedMarkRecord>();
        var ruleContext = BuildRuleContext(
          context,
          ruleSeedMarks ?? Array.Empty<MarkRecord>(),
          rulePropagatedMarks ?? Array.Empty<PropagatedMarkRecord>());
        foreach (var liftedMark in rule.Lift(
                   ruleContext,
                   ruleSeedMarks ?? Array.Empty<MarkRecord>(),
                   rulePropagatedMarks ?? Array.Empty<PropagatedMarkRecord>()))
        {
            ValidateLiftNode(rule, liftedMark.Mark.SyntaxNode);
            producedMarks.Add(BindLiftedMarkRecord(ruleContext, liftedMark, rule.GroupKey));
        }

        return producedMarks;
    }

    private static bool ShouldRunGroupsInParallel(RuleContext context, int groupCount)
    {
        return context.Runtime.ExecutionOptions.EnableGroupParallelism &&
          context.Runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism > 1 &&
          groupCount > 1;
    }

    private static RuleContext BuildRuleContext(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var fragments = seedMarks
          .Select(mark => mark.SyntaxNode)
          .Concat(propagatedMarks.Select(mark => mark.Mark.SyntaxNode))
          .Distinct()
          .ToList();
        if (fragments.Count == 0)
        {
            return context;
        }

        var structureView = context.StructureViews.BuildStructureView(fragments);
        return context.StructureViews.WithStructureView(structureView);
    }

    internal static void ValidateLiftNode(RuleDefinitionLift rule, SyntaxNode syntaxNode)
    {
        var nodeKind = (SyntaxKind)syntaxNode.RawKind;
        if (rule.AllowedLiftNodeKinds.Contains(nodeKind))
        {
            return;
        }

        var allowedKinds = string.Join(", ", rule.AllowedLiftNodeKinds);
        throw new InvalidOperationException(
          $"Rule '{rule.RuleId}' emitted unsupported lift node kind '{nodeKind}'. Allowed lift node kinds: {allowedKinds}.");
    }

    internal static LiftedMarkRecord BindLiftedMarkRecord(RuleContext context, LiftedMarkRecord candidate, string? groupKey = null)
    {
        return candidate with
        {
            Mark = MarkingEngine.BindMarkRecord(context, candidate.Mark, groupKey),
            SourceMark = MarkingEngine.BindMarkRecord(context, candidate.SourceMark, groupKey),
            GroupKey = candidate.GroupKey ?? groupKey
        };
    }

    private sealed record LiftRuleGroup(
      string GroupKey,
      IReadOnlyList<RuleDefinitionLift> Rules);
}
