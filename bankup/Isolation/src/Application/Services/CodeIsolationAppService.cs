using Application.Abstractions;
using Application.Contracts.Rewrite;
using Application.Contracts.Rewrite.Artifacts;
using Application.Mappers;
using Logic.Rewrite;

namespace Application.Services;

/// <summary>
/// 代码隔离应用服务实现。
/// </summary>
public sealed class CodeIsolationAppService : ICodeIsolationAppService
{
    private readonly IRoslynCodeIsolationFacade roslynCodeIsolationFacade;

    public CodeIsolationAppService(IRoslynCodeIsolationFacade roslynCodeIsolationFacade)
    {
        this.roslynCodeIsolationFacade = roslynCodeIsolationFacade;
    }

    public Task<CodeRewriteResultDto> DeleteClassAsync(
        DeleteClassRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            roslynCodeIsolationFacade.DeleteClass(request.SourceCode, request.ClassName)));
    }

    public Task<CodeRewriteResultDto> DeleteMethodAsync(
        DeleteMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            roslynCodeIsolationFacade.DeleteMethod(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }

    public Task<CodeRewriteResultDto> PrivatizeMethodAsync(
        PrivatizeMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            roslynCodeIsolationFacade.PrivatizeMethod(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }

    public Task<CodeRewriteResultDto> ClearMethodBodyAsync(
        ClearMethodBodyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            roslynCodeIsolationFacade.ClearMethodBody(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }

    public Task<MemberSliceDto> BuildMemberSliceAsync(
        BuildMemberSliceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            roslynCodeIsolationFacade.BuildMemberSlice(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }

    public Task<ShadowClassDto> GenerateShadowClassAsync(
        GenerateShadowClassRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            roslynCodeIsolationFacade.GenerateShadowClass(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }

    public Task<RuntimeClosureDto> ExtractMinimalRuntimeClosureAsync(
        ExtractMinimalRuntimeClosureRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ContractMapper.Map(
            roslynCodeIsolationFacade.ExtractMinimalRuntimeClosure(
                request.SourceCode,
                request.ClassName,
                request.MethodName,
                request.ParameterCount)));
    }
}
