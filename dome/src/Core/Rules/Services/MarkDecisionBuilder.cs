namespace TerrariaTools.Dome.Core.Rules.Services;

using ModelAnalysis = TerrariaTools.Dome.Core.Analysis;
using ModelRules = TerrariaTools.Dome.Core.Rules.Model;

/// <summary>
/// 定义基于分析上下文构建标记决策的能力。
/// </summary>
public interface IMarkDecisionBuilder
{
    /// <summary>
    /// 根据分析上下文构建规则决策集合。
    /// </summary>
    /// <param name="context">待评估的分析上下文。</param>
    /// <param name="cancellationToken">用于取消评估过程的令牌。</param>
    /// <returns>构建得到的标记决策集合。</returns>
    IReadOnlyList<ModelRules.MarkDecision> BuildDecisions(ModelAnalysis.AnalysisContext context, CancellationToken cancellationToken);
}
