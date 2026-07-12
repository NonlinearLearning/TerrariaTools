using RoslynPrototype.Analysis;
using RoslynPrototype.Marking;
using Rules;

namespace RoslynPrototype.Propagation;

public sealed class PropagationEngine
{
    private readonly RoslynCpgStructureViewBuilder _structureViewBuilder = new();

    /// <summary>
    /// 按规则分别执行传播，只允许规则扩展自己产出的种子标记。
    /// </summary>
    /// <param name="context">当前规则执行所需的分析上下文。</param>
    /// <param name="seedMarks">标记阶段直接命中的种子标记集合。</param>
    /// <param name="rules">参与当前分析的删除规则集合。</param>
    /// <returns>去重后的传播标记集合。</returns>
    public IReadOnlyList<PropagatedMarkRecord> Run(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<RuleDefinitionPropagate> rules)
    {
        var propagatedMarks = new List<PropagatedMarkRecord>();
        // 传播按 GroupKey 分组。组间隔离，组内按注册顺序串行，让后续规则能消费前序规则产物。
        var seedMarksByGroupKey = seedMarks
          .GroupBy(RuleStageGroupKey.Get, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var rulesByGroupKey = rules
          .GroupBy(rule => rule.GroupKey, StringComparer.Ordinal)
          .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var (groupKey, groupRules) in rulesByGroupKey)
        {
            if (!seedMarksByGroupKey.TryGetValue(groupKey, out var groupSeedMarks) || groupSeedMarks.Count == 0)
            {
                continue;
            }

            var groupMarks = new List<MarkRecord>(groupSeedMarks);
            foreach (var rule in groupRules)
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
        }

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
    private RuleContext BuildRuleContext(RuleContext context, IReadOnlyList<MarkRecord> marks)
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
}
