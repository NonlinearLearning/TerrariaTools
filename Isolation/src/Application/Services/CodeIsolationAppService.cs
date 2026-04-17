using Application.Abstractions;
using Application.Contracts.Rewrite;
using Application.Mappers;
using Domain.Rewrite;

namespace Application.Services;

/// <summary>
/// 代码隔离应用服务实现。
/// </summary>
public sealed class CodeIsolationAppService : ICodeIsolationAppService
{
    private readonly ICodeIsolationGateway codeIsolationGateway;

    public CodeIsolationAppService(ICodeIsolationGateway codeIsolationGateway)
    {
        this.codeIsolationGateway = codeIsolationGateway;
    }

    /// <inheritdoc />
    public Task<CodeRewriteResultDto> DeleteClassAsync(
        DeleteClassRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            codeIsolationGateway.DeleteClass(request.SourceCode, request.ClassName)));
    }

    /// <inheritdoc />
    public Task<CodeRewriteResultDto> DeleteMethodAsync(
        DeleteMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            codeIsolationGateway.DeleteMethod(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }

    /// <inheritdoc />
    public Task<CodeRewriteResultDto> PrivatizeMethodAsync(
        PrivatizeMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            codeIsolationGateway.PrivatizeMethod(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }

    /// <inheritdoc />
    public Task<CodeRewriteResultDto> ClearMethodBodyAsync(
        ClearMethodBodyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            codeIsolationGateway.ClearMethodBody(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }

    /// <inheritdoc />
    public Task<MemberSliceDto> BuildMemberSliceAsync(
        BuildMemberSliceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            codeIsolationGateway.BuildMemberSlice(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }

    /// <inheritdoc />
    public Task<ShadowClassDto> GenerateShadowClassAsync(
        GenerateShadowClassRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            codeIsolationGateway.GenerateShadowClass(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }

    /// <inheritdoc />
    public Task<RuntimeClosureDto> ExtractMinimalRuntimeClosureAsync(
        ExtractMinimalRuntimeClosureRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            codeIsolationGateway.ExtractMinimalRuntimeClosure(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }
}
