using Domain.Output.Audit;

namespace Logic.Workflow;

/// <summary>
/// 定义运行报告装配器。
/// </summary>
public interface IRunReportAssembler
{
    RunReport Assemble(RunReportAssemblyInput input);
}
