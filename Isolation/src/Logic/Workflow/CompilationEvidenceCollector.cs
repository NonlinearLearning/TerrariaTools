using Domain.Output.Verification;

namespace Logic.Workflow;

/// <summary>
/// 默认编译证据采集器。
/// </summary>
public sealed class CompilationEvidenceCollector : ICompilationEvidenceCollector
{

    public CompilationEvidence Collect(CompilationEvidenceCollectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string summary = input.Success ? "编译验证通过。" : "编译验证失败。";
        return new CompilationEvidence(input.Success, input.DiagnosticCount, summary);
    }
}
