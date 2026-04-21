using Domain.Rewrite;
using Domain.Rewrite.Artifacts;

namespace Logic.Rewrite;

/// <summary>
/// 基于领域网关的代码隔离门面。
/// </summary>
public sealed class RoslynCodeIsolationFacade : IRoslynCodeIsolationFacade
{
    private readonly ICodeIsolationGateway codeIsolationGateway;

    public RoslynCodeIsolationFacade(ICodeIsolationGateway codeIsolationGateway)
    {
        this.codeIsolationGateway = codeIsolationGateway;
    }

    public CodeRewriteResult DeleteClass(string sourceCode, string className)
    {
        return codeIsolationGateway.DeleteClass(sourceCode, className);
    }

    public CodeRewriteResult DeleteMethod(string sourceCode, string className, string methodName, int? parameterCount)
    {
        return codeIsolationGateway.DeleteMethod(sourceCode, className, methodName, parameterCount);
    }

    public CodeRewriteResult PrivatizeMethod(string sourceCode, string className, string methodName, int? parameterCount)
    {
        return codeIsolationGateway.PrivatizeMethod(sourceCode, className, methodName, parameterCount);
    }

    public CodeRewriteResult ClearMethodBody(string sourceCode, string className, string methodName, int? parameterCount)
    {
        return codeIsolationGateway.ClearMethodBody(sourceCode, className, methodName, parameterCount);
    }

    public MemberSlice BuildMemberSlice(string sourceCode, string className, string methodName, int? parameterCount)
    {
        return codeIsolationGateway.BuildMemberSlice(sourceCode, className, methodName, parameterCount);
    }

    public ShadowClass GenerateShadowClass(string sourceCode, string className, string methodName, int? parameterCount)
    {
        return codeIsolationGateway.GenerateShadowClass(sourceCode, className, methodName, parameterCount);
    }

    public RuntimeClosure ExtractMinimalRuntimeClosure(string sourceCode, string className, string methodName, int? parameterCount)
    {
        return codeIsolationGateway.ExtractMinimalRuntimeClosure(sourceCode, className, methodName, parameterCount);
    }
}
