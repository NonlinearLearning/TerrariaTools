using Application.Contracts.Marking;

namespace Application.Abstractions;

/// <summary>
/// 规则命中目标应用服务。
/// </summary>
public interface IRuleTargetAppService
{
    Task<RuleTargetDto> CreateAsync(
        CreateRuleTargetRequest request,
        CancellationToken cancellationToken = default);
}
