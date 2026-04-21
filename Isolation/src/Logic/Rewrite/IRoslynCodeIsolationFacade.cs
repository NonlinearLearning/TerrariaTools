using Domain.Rewrite.Artifacts;

namespace Logic.Rewrite;

/// <summary>
/// 定义 Roslyn 代码隔离低等级门面。
/// </summary>
public interface IRoslynCodeIsolationFacade
{
    CodeRewriteResult DeleteClass(string sourceCode, string className);

    CodeRewriteResult DeleteMethod(string sourceCode, string className, string methodName, int? parameterCount);

    CodeRewriteResult PrivatizeMethod(string sourceCode, string className, string methodName, int? parameterCount);

    CodeRewriteResult ClearMethodBody(string sourceCode, string className, string methodName, int? parameterCount);

    MemberSlice BuildMemberSlice(string sourceCode, string className, string methodName, int? parameterCount);

    ShadowClass GenerateShadowClass(string sourceCode, string className, string methodName, int? parameterCount);

    RuntimeClosure ExtractMinimalRuntimeClosure(string sourceCode, string className, string methodName, int? parameterCount);
}
