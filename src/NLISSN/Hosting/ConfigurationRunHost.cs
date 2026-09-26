using NLCPG.ProjectJson;
using NLISSN.Infrastructure.Configuration;

namespace NLISSN.Hosting;

internal sealed class ConfigurationRunHost
{
  internal async Task RunAsync()
  {
    var configuration = YamlConfigurationLoader.LoadFromWorkingDirectory();
    YamlConfigurationLoader.PrepareRunArtifacts(configuration);
    var host = new CommandHost();
    await host.AnalyzeAsync(configuration);
    await ExportProjectJsonAsync(configuration);
  }

  // artifacts.projectJson 是可选的后置步骤：未配置时完全不进入，保持既有行为。
  private static async Task ExportProjectJsonAsync(AnalysisConfiguration configuration)
  {
    if (configuration.ProjectExport is not { Enabled: true })
    {
      return;
    }

    // 导出以“不中断丢结果”为原则：个别文件/项目失败只降级为 incomplete，manifest 已落盘即不抛异常。
    var result = await new ProjectJsonExportService().ExportAsync(configuration);
    foreach (var diagnostic in result.Diagnostics)
    {
      await Console.Error.WriteLineAsync(diagnostic);
    }

    if (!result.Succeeded)
    {
      throw new InvalidOperationException(
        $"ProjectJson export did not complete: no manifest was written. Manifest: {result.ManifestPath}");
    }

    var statusText = result.Status == ExportStatus.Complete ? "complete" : "incomplete";
    await Console.Out.WriteLineAsync(
      $"ProjectJson: {result.WrittenFileCount} file(s) written, " +
      $"{result.FailedFileCount} failed, status {statusText}. Manifest: {result.ManifestPath}");
  }
}
