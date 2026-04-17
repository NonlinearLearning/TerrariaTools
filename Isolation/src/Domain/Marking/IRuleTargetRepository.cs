namespace Domain.Marking;

/// <summary>
/// 定义规则命中目标仓储。
/// </summary>
public interface IRuleTargetRepository
{
    /// <summary>
    /// 新增规则命中目标。
    /// </summary>
    Task AddAsync(RuleTarget ruleTarget, CancellationToken cancellationToken = default);
}
