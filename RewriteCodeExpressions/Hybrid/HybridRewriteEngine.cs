using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Context;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid;

/// <summary>
/// Hybrid 架构总编排器：Pass 1 分析 + Pass 2 执行。
/// </summary>
public sealed class HybridRewriteEngine
{
    private readonly RuleEngine _ruleEngine;
    private readonly RewriteExecutionPass _executionPass;

    /// <summary>
    /// 初始化 HybridRewriteEngine 的新实例。
    /// </summary>
    /// <param name="ruleEngine">规则引擎实例。</param>
    public HybridRewriteEngine(RuleEngine ruleEngine)
    {
        _ruleEngine = ruleEngine;
        _executionPass = new RewriteExecutionPass();
    }

    /// <summary>
    /// 为指定的语法树和语义模型创建重写上下文。
    /// </summary>
    /// <param name="semanticModel">语法树的语义模型。</param>
    /// <param name="tree">要重写的语法树。</param>
    /// <returns>新创建的重写上下文。</returns>
    public RewriteContext CreateContext(SemanticModel semanticModel, SyntaxTree tree)
    {
        return new RewriteContext(semanticModel, tree);
    }

    /// <summary>
    /// 分析语法节点并生成重写计划。
    /// </summary>
    /// <param name="root">要分析的根语法节点。</param>
    /// <param name="context">重写上下文。</param>
    /// <returns>生成的重写计划。</returns>
    public RewritePlan Analyze(SyntaxNode root, RewriteContext context)
    {
        var planner = new RewritePlanner(_ruleEngine);
        var analysisPass = new AnalysisPass(planner, _ruleEngine);
        return analysisPass.Execute(root, context);
    }

    /// <summary>
    /// 执行语法节点的重写操作。
    /// </summary>
    /// <param name="root">要重写的根语法节点。</param>
    /// <param name="context">重写上下文。</param>
    /// <returns>重写后的语法节点。</returns>
    public SyntaxNode Rewrite(SyntaxNode root, RewriteContext context)
    {
        var plan = Analyze(root, context);
        var execution = _executionPass.Execute(root, plan, context);
        var postProcessedRoot = HybridPostProcessing.Process(execution.Root, context.SemanticModel);

        context.SetState(HybridMetricsStateKeys.PlanItemCount, plan.Items.Count);
        context.SetState(HybridMetricsStateKeys.ExecutedRuleCount, execution.Summary.ExecutedRuleCount);
        context.SetState(HybridMetricsStateKeys.ReplacedNodeCount, execution.Summary.ReplacedNodeCount);
        context.SetState(HybridMetricsStateKeys.DeletedNodeCount, execution.Summary.DeletedNodeCount);

        return postProcessedRoot;
    }
}
