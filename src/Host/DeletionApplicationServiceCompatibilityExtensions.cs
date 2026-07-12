using RoslynPrototype.Rewrite;

namespace RoslynPrototype.Application;

public static class DeletionApplicationServiceCompatibilityExtensions
{
    public static PrototypeAnalysisResult AnalyzeFromArgs(
      this DeletionApplicationService application,
      string[] args)
    {
        _ = application;
        return new DeletionCommandHost(RuleRegistry.CreateDefaultRules()).AnalyzeFromArgs(args);
    }

    public static IReadOnlyList<string> FormatResult(
      this DeletionApplicationService application,
      PrototypeAnalysisResult result)
    {
        _ = application;
        return new DeletionResultFormatter().FormatResult(result);
    }
}
