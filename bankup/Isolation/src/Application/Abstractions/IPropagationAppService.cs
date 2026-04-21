using Application.Contracts.Propagation;

namespace Application.Abstractions;

/// <summary>
/// 传播应用服务。
/// </summary>
public interface IPropagationAppService
{
    Task<PropagationResultDto> PropagateAsync(
        BuildPropagationRequest request,
        CancellationToken cancellationToken = default);
}
