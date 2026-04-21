namespace Logic.Marking;

/// <summary>
/// 定义基于规则目标构造候选的能力。
/// </summary>
public interface IRuleTargetCandidateBuilder
{
    /// <summary>
    /// 构造规则执行结果。
    /// </summary>
    /// <param name="input">构造输入。</param>
    /// <returns>规则执行结果。</returns>
    RuleExecutionResult Build(RuleTargetCandidateBuildInput input);
}
