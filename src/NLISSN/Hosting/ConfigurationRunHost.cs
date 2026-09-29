using NLCPG.ProjectJson;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Infrastructure.Workspace;

namespace NLISSN.Hosting;

internal sealed class ConfigurationRunHost
{
  internal async Task RunAsync()
  {
    var configuration = YamlConfigurationLoader.LoadFromWorkingDirectory();
    YamlConfigurationLoader.PrepareRunArtifacts(configuration);
    var host = new CommandHost();
    await host.AnalyzeAsync(configuration);
    await ExportProjectJsonAsync(configuration, host.LastWorkspaceSnapshot);
  }

  // artifacts.projectJson 是可选的后置步骤：未配置时完全不进入，保持既有行为。
  private static async Task ExportProjectJsonAsync(
    AnalysisConfiguration configuration,
    WorkspaceSolutionSnapshot? reusedSnapshot)
  {
    if (configuration.ProjectExport is not { Enabled: true })
    {
      return;
    }

    // 复用分析用过的同一份快照即可省掉第二次 MSBuild 加载；但只有在**加载选项等价**时才成立。
    // 导出按 IncludeGenerated 选生成源且从不启用 generators，而分析侧由 yaml 的
    // generatedSources/generators 决定；二者不等价时复用会让 manifest 的文档集与诊断改变，
    // 故此时退回自加载，宁可多付一次加载也不改变输出。
    var snapshot = IsSnapshotReusable(configuration, reusedSnapshot) ? reusedSnapshot : null;

    // 导出以“不中断丢结果”为原则：个别文件/项目失败只降级为 incomplete，manifest 已落盘即不抛异常。
    var result = await new ProjectJsonExportService().ExportAsync(configuration, snapshot);
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

    await WriteExportMetricsAsync(result);
  }

  // 判断分析用过的快照能否直接交给导出复用。
  //
  // 需要同时成立：
  //   ① 有快照（分析确实走了工作区路径且加载成功）；
  //   ② generators 未被启用——导出侧从不启用 generators，若分析启用了，
  //      两者编译的语法树集合不同，复用会改变导出内容；
  //   ③ 生成源取舍一致——分析按 GeneratedSourceMode，导出按 IncludeGenerated
  //      （由同一个 GeneratedSourceMode 推出），故此项在 ② 成立时自动等价。
  //
  // 只有 TargetDocumentPath 不参与判定：它只影响**分析**遍历哪些文档，
  // 导出始终遍历项目全部文档，且快照本身含全量 Documents，故无分歧。
  private static bool IsSnapshotReusable(
    AnalysisConfiguration configuration,
    WorkspaceSolutionSnapshot? snapshot)
  {
    if (snapshot is null || configuration.Workspace is not { } workspace)
    {
      return false;
    }

    return workspace.GeneratorMode == WorkspaceGeneratorMode.Disabled;
  }

  // 导出阶段计时只写到 stdout（进 run-summary.txt），**不**在 out/ 下落任何新文件：
  // out/ 下的 *.json 会被 payload 基线脚本整体当成 payload 收集，
  // 多写一个 json 会让「967 条」这条判据失真。
  private static async Task WriteExportMetricsAsync(ProjectExportResult result)
  {
    if (result.Metrics is not { } metrics)
    {
      return;
    }

    await Console.Out.WriteLineAsync(
      "ProjectJson metrics (ms): " +
      $"workspaceLoad={metrics.WorkspaceLoadMilliseconds} " +
      $"build={metrics.TotalBuildMilliseconds} " +
      $"projection={metrics.TotalProjectionMilliseconds} " +
      $"write={metrics.TotalWriteMilliseconds} " +
      $"documents={metrics.Documents.Count}");

    // 逐文档全量数据在 stdout 上会淹没日志，只列最慢的若干份；
    // 排序键取三者之和，即该文档占用的总墙钟（不含内存闸门等待）。
    var slowest = metrics.Documents
      .OrderByDescending(document =>
        document.BuildMilliseconds + document.ProjectionMilliseconds + document.WriteMilliseconds)
      .ThenBy(document => document.SourcePath, StringComparer.Ordinal)
      .Take(10)
      .ToArray();
    foreach (var document in slowest)
    {
      await Console.Out.WriteLineAsync(
        $"  {document.SourcePath}: build={document.BuildMilliseconds} " +
        $"projection={document.ProjectionMilliseconds} write={document.WriteMilliseconds} " +
        $"bytes={document.PayloadBytes} nodes={document.NodeCount} " +
        $"edges={document.EdgeCount} shards={document.ShardCount}");
    }
  }
}
