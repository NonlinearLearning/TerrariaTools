using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Context;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;

/// <summary>
/// 执行 Pass 1：语法分析与重写计划生成。
/// </summary>
public sealed class AnalysisPass
{
    private readonly RewritePlanner _planner;
    private readonly RuleEngine _ruleEngine;

    public AnalysisPass(RewritePlanner planner, RuleEngine ruleEngine)
    {
        _planner = planner;
        _ruleEngine = ruleEngine;
    }

    public RewritePlan Execute(SyntaxNode root, RewriteContext context)
    {
        var visitor = new AnalysisVisitor(context);
        visitor.Visit(root);
        visitor.Complete();
        visitor.CollectMruPlan(root, _ruleEngine);
        return _planner.Build(root, context);
    }
}
