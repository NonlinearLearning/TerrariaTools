using Domain.Output.Verification;

namespace Logic.Workflow;

/// <summary>
/// 定义编译证据采集器。
/// </summary>
public interface ICompilationEvidenceCollector
{
    CompilationEvidence Collect(CompilationEvidenceCollectionInput input);
}
