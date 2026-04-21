namespace Logic.Decision;

/// <summary>
/// 定义改写决策构造能力。
/// </summary>
public interface IRewriteDecisionMaker
{
    /// <summary>
    /// 构造改写决策。
    /// </summary>
    /// <param name="input">构造输入。</param>
    /// <returns>决策结果。</returns>
    RewriteDecisionResolution Make(RewriteDecisionBuildInput input);
}
