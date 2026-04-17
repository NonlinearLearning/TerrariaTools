using Application.Contracts.Rewrite;

namespace Application.Abstractions;

/// <summary>
/// 代码隔离应用服务。
/// </summary>
public interface ICodeIsolationAppService
{
    Task<CodeRewriteResultDto> DeleteClassAsync(
        DeleteClassRequest request,
        CancellationToken cancellationToken = default);

    Task<CodeRewriteResultDto> DeleteMethodAsync(
        DeleteMethodRequest request,
        CancellationToken cancellationToken = default);

    Task<CodeRewriteResultDto> PrivatizeMethodAsync(
        PrivatizeMethodRequest request,
        CancellationToken cancellationToken = default);

    Task<CodeRewriteResultDto> ClearMethodBodyAsync(
        ClearMethodBodyRequest request,
        CancellationToken cancellationToken = default);

    Task<MemberSliceDto> BuildMemberSliceAsync(
        BuildMemberSliceRequest request,
        CancellationToken cancellationToken = default);

    Task<ShadowClassDto> GenerateShadowClassAsync(
        GenerateShadowClassRequest request,
        CancellationToken cancellationToken = default);

    Task<RuntimeClosureDto> ExtractMinimalRuntimeClosureAsync(
        ExtractMinimalRuntimeClosureRequest request,
        CancellationToken cancellationToken = default);
}
