using NLISSN.Application;

namespace NLISSN;

public static class DeletionCliRunner
{
  public static async Task RunAsync(string[] args)
  {
    var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());
    await host.AnalyzeFromArgsAsync(args);
  }
}
