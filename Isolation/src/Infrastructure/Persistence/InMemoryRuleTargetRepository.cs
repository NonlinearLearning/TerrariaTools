using Domain.Marking;

namespace Infrastructure.Persistence;

/// <summary>
/// 内存版规则命中目标仓储。
/// </summary>
public sealed class InMemoryRuleTargetRepository : IRuleTargetRepository
{
    private readonly Dictionary<Guid, RuleTarget> storage = new();

    /// <inheritdoc />
    public Task AddAsync(RuleTarget ruleTarget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ruleTarget);
        storage[ruleTarget.Id] = ruleTarget;
        return Task.CompletedTask;
    }
}
