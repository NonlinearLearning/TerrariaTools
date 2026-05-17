using RoslynPrototype.Marking;
using Rules;

namespace RoslynPrototype.Propagation;

public sealed class PropagationEngine : IPropagationEngine
{
  public IReadOnlyList<PropagatedMarkRecord> Run(
    RuleContext context,
    IReadOnlyList<MarkRecord> seedMarks,
    IReadOnlyList<IDeletionRule> rules)
  {
    var propagatedMarks = new List<PropagatedMarkRecord>();
    var seedMarksByRuleId = seedMarks
      .GroupBy(mark => mark.RuleId, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => (IReadOnlyList<MarkRecord>)group.ToList(), StringComparer.Ordinal);

    foreach (var rule in rules.Where(rule => rule.Metadata.EnabledByDefault)) {
      if (!seedMarksByRuleId.TryGetValue(rule.Metadata.RuleId, out var ruleSeedMarks) || ruleSeedMarks.Count == 0) {
        continue;
      }

      foreach (var propagatedMark in rule.Propagate(context, ruleSeedMarks)) {
        MarkingEngine.ValidateHitNode(rule, propagatedMark.Mark.SyntaxNode);
        propagatedMarks.Add(MarkingEngine.BindPropagatedMarkRecord(context, propagatedMark));
      }
    }

    return propagatedMarks
      .DistinctBy(mark => (mark.RuleId, mark.Mark.SyntaxNode.SpanStart, mark.Mark.SyntaxNode.Span.Length))
      .ToList();
  }
}
