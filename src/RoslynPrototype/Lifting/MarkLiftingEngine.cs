using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MinimalRoslynCpg.Analysis;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Lifting;

public sealed class MarkLiftingEngine
{
    private readonly RoslynCpgStructureViewBuilder _structureViewBuilder = new();

    public IReadOnlyList<LiftedMarkRecord> Run(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks,
      IReadOnlyList<RuleDefinitionLift> rules)
    {
        var liftedMarks = new List<LiftedMarkRecord>();
        var liftEligiblePropagatedMarks = propagatedMarks
          .Where(mark => mark.Payload is null)
          .ToList();
        var seedMarksByGroupKey = seedMarks
          .GroupBy(MarkingEngine.GetGroupKey, StringComparer.Ordinal)
          .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<MarkRecord>)group.ToList(),
            StringComparer.Ordinal);
        var propagatedMarksByGroupKey = liftEligiblePropagatedMarks
          .GroupBy(MarkingEngine.GetGroupKey, StringComparer.Ordinal)
          .ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<PropagatedMarkRecord>)group.ToList(),
            StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            seedMarksByGroupKey.TryGetValue(rule.GroupKey, out var ruleSeedMarks);
            propagatedMarksByGroupKey.TryGetValue(rule.GroupKey, out var rulePropagatedMarks);
            if ((ruleSeedMarks is null || ruleSeedMarks.Count == 0) &&
                (rulePropagatedMarks is null || rulePropagatedMarks.Count == 0))
            {
                continue;
            }

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
                liftedMarks.Add(BindLiftedMarkRecord(ruleContext, liftedMark, rule.GroupKey));
            }
        }

        return liftedMarks
          .DistinctBy(mark => (
            mark.GroupKey ?? mark.RuleId,
            mark.Mark.SyntaxNode.SpanStart,
            mark.Mark.SyntaxNode.Span.Length,
            mark.Mark.SyntaxNode.RawKind))
          .ToList();
    }

    private RuleContext BuildRuleContext(
      RuleContext context,
      IReadOnlyList<MarkRecord> seedMarks,
      IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
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

        var structureView = _structureViewBuilder.Build(fragments, context.AnalysisContext);
        return context.WithStructureView(structureView);
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

    internal static LiftedMarkRecord BindLiftedMarkRecord(
      RuleContext context,
      LiftedMarkRecord candidate,
      string? groupKey = null)
    {
        return candidate with
        {
            Mark = MarkingEngine.BindMarkRecord(context, candidate.Mark, groupKey),
            SourceMark = MarkingEngine.BindMarkRecord(context, candidate.SourceMark, groupKey),
            GroupKey = candidate.GroupKey ?? groupKey
        };
    }
}
