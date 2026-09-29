namespace NLCPG.ProjectJson;

/// <summary>
/// 导出阶段的计时读数。仅在 <see cref="ProjectExportOptions.PerformanceDiagnostics"/> 为
/// <c>true</c> 时收集；默认 <c>null</c>，此时导出不付任何计时开销。
/// </summary>
/// <remarks>
/// 这套读数是为了回答一个此前无法回答的问题：导出的墙钟时间里，**构图 / 投影 / 序列化落盘**
/// 各占多少。此前导出侧没有任何阶段遥测，故「复用分析构图能省多少」只能靠推算。
/// <para>
/// 口径：所有毫秒都是**墙钟累加**。文档之间是串行的，故
/// <see cref="TotalBuildMilliseconds"/> 等合计值可与导出总墙钟直接比较；
/// 分片共用一次构图，故 <see cref="ProjectExportDocumentMetrics.ProjectionMilliseconds"/>
/// 是该文档全部分片的合计。
/// </para>
/// </remarks>
public sealed record ProjectExportMetrics(
  long WorkspaceLoadMilliseconds,
  long TotalBuildMilliseconds,
  long TotalProjectionMilliseconds,
  long TotalWriteMilliseconds,
  IReadOnlyList<ProjectExportDocumentMetrics> Documents);

/// <summary>单个文档的导出计时。<see cref="SourcePath"/> 与 manifest 中记录的一致。</summary>
public sealed record ProjectExportDocumentMetrics(
  string SourcePath,
  long BuildMilliseconds,
  long ProjectionMilliseconds,
  long WriteMilliseconds,
  long PayloadBytes,
  int NodeCount,
  int EdgeCount,
  int ShardCount);
