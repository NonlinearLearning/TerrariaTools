using NLISSN.Core.Pipeline;

namespace NLISSN.Application;

public static class AnalysisRuntimeFactory
{
  public static AnalysisRuntime Create(
    RoslynPrototypeExecutionOptions executionOptions)
  {
    ArgumentNullException.ThrowIfNull(executionOptions);
    return new AnalysisRuntime(executionOptions, new AnalysisEpoch(0, 0, 0));
  }

  public static AnalysisRuntime CreateDefault()
  {
    return Create(RoslynPrototypeExecutionOptions.CreateDefault());
  }
}
