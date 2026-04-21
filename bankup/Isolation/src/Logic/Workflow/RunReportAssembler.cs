using Domain.Output.Audit;

namespace Logic.Workflow;

/// <summary>
/// 默认运行报告装配器。
/// </summary>
public sealed class RunReportAssembler : IRunReportAssembler
{

    public RunReport Assemble(RunReportAssemblyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return RunReport.CreateFromExecutionOutcome(
            input.WorkspaceContextId,
            input.DecisionId,
            input.PlanId,
            input.Result,
            input.Evidence);
    }
}
