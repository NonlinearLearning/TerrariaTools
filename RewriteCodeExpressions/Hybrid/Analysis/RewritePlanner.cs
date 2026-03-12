using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

/// <summary>
/// 根据规则引擎和分析上下文生成重写计划。
/// </summary>
public sealed class RewritePlanner
{
    private readonly RuleEngine _ruleEngine;

    public RewritePlanner(RuleEngine ruleEngine)
    {
        _ruleEngine = ruleEngine;
    }

    public RewritePlan Build(SyntaxNode root, IRewriteContext context)
    {
        var plan = new RewritePlan();

        var mruItems = context.GetState<List<RewritePlanItem>>(AnalysisStateKeys.MruPlanItems);
        if (mruItems is not null && mruItems.Count > 0)
        {
            foreach (var item in mruItems)
            {
                plan.Add(item.Node, item.Rule);
            }

            return plan;
        }

        foreach (var node in root.DescendantNodesAndSelf())
        {
            var matched = _ruleEngine.FindMatchingRule(node, context);
            if (matched is not null)
            {
                plan.Add(node, matched);
            }
        }

        return plan;
    }
}
