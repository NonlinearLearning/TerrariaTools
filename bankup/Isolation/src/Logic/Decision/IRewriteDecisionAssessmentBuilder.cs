using Domain.Decision;

namespace Logic.Decision;

/// <summary>
/// 定义改写决策评估构造能力。
/// </summary>
public interface IRewriteDecisionAssessmentBuilder
{
    /// <summary>
    /// 构造决策评估结果。
    /// </summary>
    /// <param name="input">构造输入。</param>
    /// <returns>决策评估结果。</returns>
    RewriteDecisionAssessment Build(RewriteDecisionAssessmentBuildInput input);
}
