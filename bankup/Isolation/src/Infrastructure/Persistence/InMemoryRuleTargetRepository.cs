using Domain.Marking;

namespace Infrastructure.Persistence;

/// <summary>
/// 内存版规则命中目标仓储。
/// </summary>
public sealed class InMemoryRuleTargetRepository : IRuleTargetRepository
{
    private readonly Dictionary<Guid, RuleTarget> storage = new();


    public Task AddAsync(RuleTarget ruleTarget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ruleTarget);
        storage[ruleTarget.Id] = ruleTarget;
        return Task.CompletedTask;
    }


    public Task<RuleTarget?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        storage.TryGetValue(id, out RuleTarget? ruleTarget);
        return Task.FromResult(ruleTarget);
    }
}
