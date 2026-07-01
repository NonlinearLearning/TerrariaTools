using RoslynPrototype.Marking;
using Rules;

namespace RoslynPrototype.Propagation;

public sealed class PropagationEngine
{
    /// <summary>
    /// 按规则分别执行传播，只允许规则扩展自己产出的种子标记。
    /// </summary>
    /// <param name="context">当前规则执行所需的分析上下文。</param>
    /// <param name="seedMarks">标记阶段直接命中的种子标记集合。</param>
    /// <param name="rules">参与当前分析的删除规则集合。</param>
    /// <returns>去重后的传播标记集合。</returns>
    public IReadOnlyList<PropagatedMarkRecord> Run(RuleContext context, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<RuleDefinition> rules)
    {
        var propagatedMarks = new List<PropagatedMarkRecord>();
        // 传播按规则隔离执行，避免不同规则之间互相消费对方的种子标记。
        var seedMarksByRuleId = seedMarks
        .GroupBy(mark => mark.RuleId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => (IReadOnlyList<MarkRecord>)group.ToList(), StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            if (!seedMarksByRuleId.TryGetValue(rule.RuleId, out var ruleSeedMarks) || ruleSeedMarks.Count == 0)
            {
                continue;
            }

            foreach (var propagatedMark in rule.Propagate(context, ruleSeedMarks))
            {
                MarkingEngine.ValidatePropagateNode(rule, propagatedMark.Mark.SyntaxNode);
                propagatedMarks.Add(MarkingEngine.BindPropagatedMarkRecord(context, propagatedMark));
            }
        }

        // 不同传播路径可能命中同一个语法节点，这里按规则和语法位置收口去重。
        return propagatedMarks
        .DistinctBy(mark => (mark.RuleId, mark.Mark.SyntaxNode.SpanStart, mark.Mark.SyntaxNode.Span.Length))
        .ToList();
    }
}
