using NLISSN.Application;
using NLISSN.Composition;

namespace NLISSN.Cli.Hosting;

/// 提供薄可执行入口，并将选项处理交给命令宿主。
public static class  CliRunner
{
  // 构造默认命令宿主并执行一次完整 CLI 分析流程。
  public static async Task RunAsync(string[] args)
  {
    var host = new  CommandHost(RuleRegistry.CreateDefaultRules());
    await host.AnalyzeFromArgsAsync(args);
  }
}
