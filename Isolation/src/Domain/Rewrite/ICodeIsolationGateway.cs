namespace Domain.Rewrite;

/// <summary>
/// 代码隔离网关。
/// </summary>
public interface ICodeIsolationGateway
{
    CodeRewriteResult DeleteClass(string sourceCode, string className);

    CodeRewriteResult DeleteMethod(string sourceCode, string className, string methodName, int? parameterCount);

    CodeRewriteResult PrivatizeMethod(string sourceCode, string className, string methodName, int? parameterCount);

    CodeRewriteResult ClearMethodBody(string sourceCode, string className, string methodName, int? parameterCount);

    MemberSlice BuildMemberSlice(string sourceCode, string className, string methodName, int? parameterCount);

    ShadowClass GenerateShadowClass(string sourceCode, string className, string methodName, int? parameterCount);

    RuntimeClosure ExtractMinimalRuntimeClosure(string sourceCode, string className, string methodName, int? parameterCount);
}
