using Application.Contracts.Decision;

namespace Application.Abstractions;

/// <summary>
/// 决策应用服务。
/// </summary>
public interface IDecisionAppService
{
    Task<DecisionResultDto> DecideAsync(
        BuildRewriteDecisionRequest request,
        CancellationToken cancellationToken = default);
}
