namespace NLCPG.ProjectJson;

/// <summary>
/// 导出结果。<see cref="Succeeded"/> 只表示导出未因致命错误中断（工作区不可用或致命异常）；
/// 个别文件或项目失败属可继续的降级，由 <see cref="Status"/> 表达。
/// </summary>
public sealed record ProjectExportResult(
  bool Succeeded,
  string ManifestPath,
  int WrittenFileCount,
  int FailedFileCount,
  IReadOnlyList<string> Diagnostics,
  ExportStatus Status);
