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
}
