using Domain.Marking;

namespace Logic.Marking;

/// <summary>
/// 定义规则目标构造能力。
/// </summary>
public interface IRuleTargetBuilder
{
    /// <summary>
    /// 构造规则目标。
    /// </summary>
    /// <param name="input">构造输入。</param>
    /// <returns>规则目标聚合根。</returns>
    RuleTarget Build(RuleTargetBuildInput input);
}
