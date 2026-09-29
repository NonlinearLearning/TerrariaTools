using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NLCPG.Builder;
using NLCPG.Contracts;
using NLCPG.Model;
using NLISSN.Infrastructure.Workspace;

namespace NLCPG.ProjectJson;

/// <summary>导出完成度：<c>complete</c> 表示全部项目与文档都已产出，<c>incomplete</c> 表示存在失败项或被中断。</summary>
public enum ExportStatus
{
    Complete,
    Incomplete,
}

/// <summary>
/// 项目级 CPG JSON 导出器。每个源文件写出一个自包含 payload（含该文件的 nodes/edges），
/// <c>manifest.json</c> 只记录项目维度、文件索引与诊断，不重复承载全局节点/边并集。
/// </summary>
/// <remarks>
/// 不设全局累加器是有意的：manifest 的 <c>nodes[]</c>/<c>edges[]</c> 曾是全部文件的去重并集，
/// 必须先累积再写出，而累积只能落在内存（随文件数增长）或磁盘（实测峰值 5.3 GB/次）。
/// 两者都不要，因此全局并集这个产物本身被移除——节点 id 是内容派生的且含文件路径，
/// 跨文件引用可直接用 payload 内的 id 解析，不需要 manifest 再给一份索引。
/// 逐文件 projection 算完即写出并释放，常驻内存与文件数无关（O(1 文件)）。
/// </remarks>
public sealed class ProjectJsonExporter
{
    // manifest 的 tool 标识是导出格式契约的一部分，不随命名空间调整而变化。
    private const string ToolName = "NLCPG.ProjectExport";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<ProjectExportResult> ExportAsync(
      ProjectExportOptions options,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return await ExportOrderedOracleAsync(options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 复用调用方**已经加载好**的工作区快照，跳过导出阶段的第二次 MSBuild 加载。
    /// </summary>
    /// <param name="snapshot">
    /// 分析阶段使用的同一份快照。必须由**与本导出等价的加载选项**产生：导出按
    /// <see cref="ProjectExportOptions.IncludeGenerated"/> 选择生成源，且不启用 generators，
    /// 故只有当调用方的加载选项与之一致时复用才是字节中性的。
    /// 传 <c>null</c> 等价于 <see cref="ExportAsync(ProjectExportOptions, CancellationToken)"/>。
    /// </param>
    public async Task<ProjectExportResult> ExportAsync(
      ProjectExportOptions options,
      WorkspaceSolutionSnapshot? snapshot,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return await ExportOrderedOracleAsync(options, cancellationToken, snapshot).ConfigureAwait(false);
    }

    // 逐文档导出路径：文档之间串行，并行度落在单文档内部的 builder DOP。
    // 覆盖工作区的全部项目；无论正常结束、个别文档失败、取消还是异常都会写出 manifest，
    // 使中断的导出仍可判定完整性，而不是留下一堆无法解释的 payload。
    private async Task<ProjectExportResult> ExportOrderedOracleAsync(
      ProjectExportOptions options,
      CancellationToken cancellationToken = default,
      WorkspaceSolutionSnapshot? reusedSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var outputPath = options.FullOutputPath;
        Directory.CreateDirectory(outputPath);
        var manifestPath = Path.Combine(outputPath, "manifest.json");
        var diagnosticMessages = new List<string>();
        var workspaceDiagnostics = new List<ExportDiagnostic>();
        var files = new List<FileExportEntry>();
        var projectEntries = new List<ProjectEntry>();
        var totalDocumentCount = 0;
        var status = ExportStatus.Incomplete;
        var manifestWritten = false;

        // 阶段计时：只有显式打开时才取时钟。关闭时下列变量恒为 0/null，
        // 且不建任何列表，故导出路径的开销与引入度量前一致。
        var collectMetrics = options.PerformanceDiagnostics;
        var workspaceLoadStopwatch = collectMetrics ? Stopwatch.StartNew() : null;
        var documentMetrics = collectMetrics
          ? new List<ProjectExportDocumentMetrics>()
          : null;
        long totalBuildMilliseconds = 0;
        long totalProjectionMilliseconds = 0;
        long totalWriteMilliseconds = 0;

        async Task WriteManifestOnceAsync(CancellationToken manifestToken)
        {
            var primaryProject = projectEntries.Count > 0
              ? projectEntries[0]
              : new ProjectEntry(
                options.FullProjectPath,
                null,
                null,
                null,
                options.Configuration,
                options.Platform,
                null,
                ExportStatusText(ExportStatus.Incomplete),
                null,
                0,
                0,
                0,
                Array.Empty<FileExportEntry>());
            // manifest 是完整性的判定依据，因此文件与诊断都按稳定键排序：
            // Roslyn 的 Compilation.GetDiagnostics() 顺序在多次运行间并不稳定。
            var header = new ManifestHeader(
              1,
              ToolName,
              status,
              primaryProject,
              projectEntries.ToArray(),
              NormalizePath(options.FullOutputPath),
              totalDocumentCount,
              files.Count(file => file.Status == "written"),
              files.Count(file => file.Status == "failed"),
              files
                .OrderBy(file => file.SourcePath, StringComparer.Ordinal)
                .ToArray(),
              workspaceDiagnostics
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.ProjectPath, StringComparer.Ordinal)
                .ToArray());
            await WriteManifestFileAsync(
              manifestPath,
              header,
              manifestToken).ConfigureAwait(false);
            manifestWritten = true;
        }

        // manifest 写入本身失败（典型是磁盘写满）时不能把异常抛出覆盖原始失败原因：
        // 记录为诊断，让调用方看到 Succeeded=false 并据此退出非零。
        async Task TryWriteManifestOnceAsync()
        {
            if (manifestWritten)
            {
                return;
            }

            try
            {
                await WriteManifestOnceAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception writeException)
            {
                var diagnostic = new ExportDiagnostic(
                  "export",
                  "NLCPGEXP006",
                  "Failed to write the export manifest: " + writeException.Message,
                  "Error",
                  manifestPath);
                workspaceDiagnostics.Add(diagnostic);
                diagnosticMessages.Add(FormatDiagnostic(diagnostic));
            }
        }

        try
        {
            WorkspaceSolutionSnapshot snapshot;
            IReadOnlyList<WorkspaceInputDiagnostic> snapshotDiagnostics;
            if (reusedSnapshot is not null)
            {
                // 复用调用方（分析阶段）已加载好的快照：跳过第二次 MSBuild 加载。
                // 诊断同样取自该快照，保证 manifest.diagnostics 与自加载路径一致——
                // 快照里已经带了加载器产出的同一批 WorkspaceInputDiagnostic。
                snapshot = reusedSnapshot;
                snapshotDiagnostics = reusedSnapshot.Diagnostics;
                workspaceLoadStopwatch?.Stop();
            }
            else
            {
                var workspaceResult = await new MsBuildWorkspaceInputLoader().LoadAsync(
                  new WorkspaceInputOptions(
                    options.FullProjectPath,
                    TargetFramework: options.TargetFramework,
                    Configuration: options.Configuration,
                    Platform: options.Platform,
                    RestoreMode: options.RestoreMode,
                    GeneratedSourceMode: options.IncludeGenerated
                      ? WorkspaceGeneratedSourceMode.Include
                      : WorkspaceGeneratedSourceMode.Exclude,
                    RequireCleanCompilation: false),
                  cancellationToken).ConfigureAwait(false);

                // 第二次工作区加载的耗时（分析侧已加载过一次）。这是「去掉第二次加载」的真实读数。
                workspaceLoadStopwatch?.Stop();

                if (!workspaceResult.IsSuccess || workspaceResult.Snapshot is null)
                {
                    workspaceDiagnostics.AddRange(workspaceResult.Diagnostics.Select(MapWorkspaceDiagnostic));
                    diagnosticMessages.AddRange(workspaceDiagnostics.Select(FormatDiagnostic));
                    await WriteManifestOnceAsync(cancellationToken).ConfigureAwait(false);
                    manifestWritten = true;
                    return new ProjectExportResult(
                      false,
                      manifestPath,
                      0,
                      0,
                      diagnosticMessages,
                      ExportStatus.Incomplete);
                }

                snapshot = workspaceResult.Snapshot;
                snapshotDiagnostics = workspaceResult.Diagnostics;
            }

            workspaceDiagnostics.AddRange(snapshotDiagnostics.Select(MapWorkspaceDiagnostic));
            diagnosticMessages.AddRange(workspaceDiagnostics.Select(FormatDiagnostic));

            if (snapshot.Projects.Count == 0)
            {
                var diagnostic = new ExportDiagnostic(
                  "export",
                  "NLCPGEXP001",
                  "The workspace did not contain a C# project to export.",
                  "Error",
                  options.FullProjectPath);
                workspaceDiagnostics.Add(diagnostic);
                diagnosticMessages.Add(FormatDiagnostic(diagnostic));
                await WriteManifestOnceAsync(cancellationToken).ConfigureAwait(false);
                manifestWritten = true;
                return new ProjectExportResult(
                  false,
                  manifestPath,
                  0,
                  0,
                  diagnosticMessages,
                  ExportStatus.Incomplete);
            }

            var isSolution = snapshot.IsSolution;
            var usedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var project in snapshot.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var projectRoot = Path.GetDirectoryName(project.ProjectPath)!;
                var sourceDocuments = project.Documents
                  .Where(document => options.IncludeGenerated || !document.IsGenerated)
                  .Where(document => string.Equals(Path.GetExtension(document.FilePath), ".cs", StringComparison.OrdinalIgnoreCase))
                  .OrderBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase)
                  .ToArray();
                totalDocumentCount += sourceDocuments.Length;

                // .sln 输入：每个项目一个子目录，子目录内镜像该项目源目录；单项目输入保持既有扁平镜像。
                var projectDirectoryName = isSolution
                  ? CreateUniqueProjectDirectoryName(project, usedDirectoryNames)
                  : null;
                var projectFiles = new List<FileExportEntry>(sourceDocuments.Length);
                var projectFailed = false;

                // 内存准入闸门：串行执行下同时只有一个文档驻留，闸门本身不改变并发度；
                // 保留它是为了让"单文档峰值内存是否落在预算内"维持同一套判定。
                // 单文件峰值内存随源码大小急剧上升（实测 NPC.cs 源码 2.1 MB → payload 2.29 GB），
                // 故即便串行，仍按额度准入后再做可能高达数 GB 的 CPG 构建。
                var memoryBudgetBytes = BuildMemoryBudget(options.ProjectMemoryBudgetBytes);
                using var memoryGate = new MemoryAdmissionGate(
                  memoryBudgetBytes,
                  options.ProjectWorkerCount);

                try
                {
                    // 文档之间**串行**：projectWorkerCount 作用于单文档内部的 builder DOP
                    // （见下方 MaxDegreeOfParallelism = options.EffectiveMaxDegreeOfParallelism）。
                    foreach (var document in sourceDocuments)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // 先按内存额度准入，再做可能高达数 GB 的 CPG 构建。
                        // 同一个估算值也传给分片级闸门做「按记录数分摊」，使两级闸门口径一致。
                        var documentEstimatedBytes = EstimateDocumentMemoryBytes(document.Source.Length);
                        using var admission = await memoryGate
                          .AcquireAsync(
                            documentEstimatedBytes,
                            cancellationToken).ConfigureAwait(false);
                        // 解析失败时保留绝对路径，使该文档按“失败文件”记录而不是中断整次导出。
                        var sourcePath = NormalizePath(document.FilePath);
                        var outputRelativePath = sourcePath + ".json";
                        try
                        {
                            var relativePath = ToRelativePath(projectRoot, document.FilePath);
                            sourcePath = projectDirectoryName is null
                              ? relativePath
                              : projectDirectoryName + "/" + relativePath;
                            outputRelativePath = sourcePath + ".json";
                            var outputFilePath = CombineRelativePath(outputPath, outputRelativePath);

                            var semanticModel = project.Compilation.GetSemanticModel(document.SyntaxTree);
                            var root = await document.SyntaxTree.GetRootAsync(cancellationToken)
                              .ConfigureAwait(false);
                            // projectWorkerCount 的落点：单文档内部的 builder 并行度。
                            //
                            // RequestedCapabilities 是「在 Default 之外**额外**请求」的语义，
                            // 故这里**必须**把 Default 并进去：ResolveCapabilityBuildPlan 对非 null
                            // 的请求走 Aggregate(None, OR)，是**替换**而非并入 Default。
                            // 不并的后果已实测：只写 ["InterproceduralDataFlow"] 会丢掉
                            // SyntaxToken / Reference / TypeRef / Cfg 等 Default 位，
                            // 使 payload 反而比默认导出少掉了这些边（Collision.cs 实测
                            // SyntaxToken 28109→0、Reference 9829→0、TypeRef 8043→0）。
                            var builderOptions = NLCPGBuilderOptions.CreateDefault() with
                            {
                                MaxDegreeOfParallelism = options.EffectiveMaxDegreeOfParallelism,
                                // 导出只读图的 Nodes/Edges/Resolve*，从不读 LastBuildMetrics，
                                // 故跳过冻结期的 BuildInventoryAudit（全部 anchor 物化 + SHA-256 指纹）。
                                // 字节中性：该审计不参与任何 payload 字段的取值。
                                ComputeBuildInventory = false,
                                // null 保持 CreateDefault() 的默认能力集（= NLCPGCapability.Default）。
                                RequestedCapabilities = MergeRequestedCapabilities(options.RequestedCapabilities),
                            };
                            // 单文件构图耗时：这是「导出重构图值多少」的直接读数。
                            var buildStopwatch = collectMetrics ? Stopwatch.StartNew() : null;
                            var graph = new NLCPGBuilder(builderOptions).BuildFromSemanticModel(
                              semanticModel,
                              root,
                              document.Source,
                              document.FilePath);
                            buildStopwatch?.Stop();

                            // 单文档分片：节点身份（稳定 id 与归一化路径）**先整篇算一次**，
                            // 再按节点区间分片物化。id 的 ordinal 走整篇口径，故分片只改变
                            // 「记录写进哪个文件」，不改变任何一条记录的取值。
                            //
                            // 串行（默认，DocumentShardParallelism = 1）：接缝是**惰性**的，
                            // 整篇 id/路径/边序在首次 MoveNext 时算，各片 DTO 在后续每次 MoveNext 时算。
                            // 故「投影」耗时按每个 MoveNext 累加，不能只包住接缝调用本身——
                            // 包住调用测得恒为 0，投影耗时会被误记进写盘（度量失真）。
                            //
                            // 并行（> 1）：不能用惰性接缝，因为它的 MoveNext 把「推进枚举」与
                            // 「物化该片」绑在一起，取片必须串行化，于是投影也被串行化，
                            // 并行就只覆盖了写盘。故并行路径直接用下层两段（整篇准备 + 按下标并行物化）。
                            var shardOutcome = await WriteDocumentShardsAsync(
                              graph,
                              sourcePath,
                              projectRoot,
                              projectDirectoryName,
                              options.DocumentShardCount,
                              options.DocumentShardParallelism,
                              outputRelativePath,
                              outputFilePath,
                              outputPath,
                              collectMetrics,
                              cancellationToken,
                              documentEstimatedBytes,
                              memoryBudgetBytes).ConfigureAwait(false);
                            projectFiles.AddRange(shardOutcome.Entries);
                            var shardBytes = shardOutcome.Bytes;
                            var shardCount = shardOutcome.ShardCount;
                            if (collectMetrics)
                            {
                                var buildMs = buildStopwatch!.ElapsedMilliseconds;
                                var projectionMs = shardOutcome.ProjectionMs;
                                var writeMs = shardOutcome.WriteMs;
                                totalBuildMilliseconds += buildMs;
                                totalProjectionMilliseconds += projectionMs;
                                totalWriteMilliseconds += writeMs;
                                // PayloadBytes 是该文档**全部分片**的落盘字节合计，
                                // 与 manifest 的 sizeGB 口径（payload 总量）一致。
                                documentMetrics!.Add(new ProjectExportDocumentMetrics(
                                  sourcePath,
                                  buildMs,
                                  projectionMs,
                                  writeMs,
                                  shardBytes,
                                  graph.Nodes.Count,
                                  graph.Edges.Count,
                                  shardCount));
                            }
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            var diagnostic = new ExportDiagnostic(
                              "document",
                              "NLCPGEXP002",
                              exception.Message,
                              "Error",
                              document.FilePath,
                              project.ProjectPath,
                              project.TargetFramework);
                            workspaceDiagnostics.Add(diagnostic);
                            diagnosticMessages.Add(FormatDiagnostic(diagnostic));
                            projectFiles.Add(new FileExportEntry(
                              sourcePath,
                              outputRelativePath,
                              "failed",
                              0,
                              0,
                              exception.Message));
                        }
                    }

                    foreach (var diagnostic in project.Compilation.GetDiagnostics(cancellationToken))
                    {
                        var exportDiagnostic = MapCompilationDiagnostic(
                          diagnostic,
                          projectRoot,
                          project.ProjectPath,
                          project.TargetFramework);
                        workspaceDiagnostics.Add(exportDiagnostic);
                        diagnosticMessages.Add(FormatDiagnostic(exportDiagnostic));
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // 项目级失败：记录该项目并继续导出其余项目。
                    projectFailed = true;
                    var diagnostic = new ExportDiagnostic(
                      "project",
                      "NLCPGEXP003",
                      exception.Message,
                      "Error",
                      project.ProjectPath,
                      project.ProjectPath,
                      project.TargetFramework);
                    workspaceDiagnostics.Add(diagnostic);
                    diagnosticMessages.Add(FormatDiagnostic(diagnostic));
                }

                projectEntries.Add(new ProjectEntry(
                  project.ProjectPath,
                  project.ProjectName,
                  project.AssemblyName,
                  project.TargetFramework,
                  options.Configuration,
                  options.Platform,
                  project.Fingerprint,
                  projectFailed || projectFiles.Any(file => file.Status == "failed")
                    ? ExportStatusText(ExportStatus.Incomplete)
                    : ExportStatusText(ExportStatus.Complete),
                  projectDirectoryName,
                  sourceDocuments.Length,
                  projectFiles.Count(file => file.Status == "written"),
                  projectFiles.Count(file => file.Status == "failed"),
                  projectFiles
                    .OrderBy(file => file.SourcePath, StringComparer.Ordinal)
                    .ToArray()));
                files.AddRange(projectFiles);
            }

            status = files.Any(file => file.Status == "failed") ||
              projectEntries.Any(entry => entry.Status == ExportStatusText(ExportStatus.Incomplete))
                ? ExportStatus.Incomplete
                : ExportStatus.Complete;
            await WriteManifestOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消也要留下可判定的 manifest：已完成 payload 保留，缺口由 status/files 表达。
            status = ExportStatus.Incomplete;
            await TryWriteManifestOnceAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            status = ExportStatus.Incomplete;
            workspaceDiagnostics.Add(new ExportDiagnostic(
              "export",
              "NLCPGEXP004",
              exception.Message,
              "Error",
              options.FullProjectPath));
            diagnosticMessages.Add(FormatDiagnostic(workspaceDiagnostics[^1]));
            await TryWriteManifestOnceAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            TryDeleteFile(manifestPath + ".tmp");
        }

        if (!manifestWritten)
        {
            await TryWriteManifestOnceAsync().ConfigureAwait(false);
        }

        // Succeeded 只表示导出未因致命错误中断；“无 manifest”意味着没有可验收的导出结果。
        // 个别文件/项目失败属于可继续的降级，由 Status 与 manifest.status 表达。
        return new ProjectExportResult(
          manifestWritten,
          manifestPath,
          files.Count(file => file.Status == "written"),
          files.Count(file => file.Status == "failed"),
          diagnosticMessages,
          status,
          // 未开启度量时为 null（不产生任何计时对象）。
          collectMetrics
            ? new ProjectExportMetrics(
              workspaceLoadStopwatch!.ElapsedMilliseconds,
              totalBuildMilliseconds,
              totalProjectionMilliseconds,
              totalWriteMilliseconds,
              documentMetrics!)
            : null);
    }

    // manifest 统一写出点：全部字段由 JsonSerializer 直接写出。
    // 这里不再有第二遍流式阶段——节点/边只存在于各 payload 内，manifest 不重复承载全局并集。
    private static async Task WriteManifestFileAsync(
      string path,
      ManifestHeader header,
      CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
              temporaryPath,
              FileMode.Create,
              FileAccess.Write,
              FileShare.None))
            {
                await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
                {
                    Indented = true,
                });
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", header.SchemaVersion);
                writer.WriteString("tool", header.Tool);
                writer.WriteString("status", ExportStatusText(header.Status));
                writer.WritePropertyName("project");
                JsonSerializer.Serialize(writer, header.Project, JsonOptions);
                writer.WritePropertyName("projects");
                JsonSerializer.Serialize(writer, header.Projects, JsonOptions);
                writer.WriteString("outputRoot", header.OutputRoot);
                writer.WriteNumber("totalDocuments", header.TotalDocumentCount);
                writer.WriteNumber("writtenFiles", header.WrittenFileCount);
                writer.WriteNumber("failedFiles", header.FailedFileCount);
                writer.WritePropertyName("files");
                JsonSerializer.Serialize(writer, header.Files, JsonOptions);
                writer.WritePropertyName("diagnostics");
                JsonSerializer.Serialize(writer, header.Diagnostics, JsonOptions);
                writer.WriteEndObject();
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static string ExportStatusText(ExportStatus status)
    {
        return status == ExportStatus.Complete ? "complete" : "incomplete";
    }

    // .sln 下每个项目一个输出子目录名：按项目名去重，避免同名项目互相覆盖 payload。
    private static string CreateUniqueProjectDirectoryName(
      WorkspaceProjectSnapshot project,
      ISet<string> usedNames)
    {
        var baseName = SanitizeDirectoryName(
          !string.IsNullOrWhiteSpace(project.ProjectName)
            ? project.ProjectName
            : Path.GetFileNameWithoutExtension(project.ProjectPath));
        var candidate = baseName;
        var suffix = 2;
        while (!usedNames.Add(candidate))
        {
            candidate = baseName + "-" + suffix;
            suffix++;
        }

        return candidate;
    }

    private static string SanitizeDirectoryName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "project" : sanitized;
    }

    /// <summary>
    /// 单文档的**整篇**节点身份：稳定 id、归一化路径，以及「节点序号 → 分片」映射。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么身份必须先整篇算完再分片：</b><see cref="CreateStableId"/> 的入参含
    /// <c>ordinal</c>——它是同一 <c>canonicalBase</c> 在全篇内的出现序号。
    /// 若让各分片各自从 <c>ordinal=0</c> 起算，同一 canonical base 的两个节点一旦被切到
    /// 不同分片就会得到**相同的 id**，且不抛异常（消费方按 id 解析时会静默拿到错误节点）。
    /// 故本类先对全篇做一次 <c>ordinal</c> 计数，分片只决定「记录写进哪个文件」。
    /// </para>
    /// <para>
    /// 分片**不改变任何一条记录的取值**：单分片（默认）时节点序、边序与 id 与未分片实现逐字节相同。
    /// </para>
    /// </remarks>
    private sealed class DocumentProjection
    {
        internal DocumentProjection(
          NLCPGNode[] nodes,
          string[] ids,
          string[] filePaths,
          Dictionary<NodeId, int> nodeIndexByLocalId,
          ShardRange[] shardNodeRanges,
          int[] shardIndexByNodeIndex)
        {
            Nodes = nodes;
            Ids = ids;
            FilePaths = filePaths;
            NodeIndexByLocalId = nodeIndexByLocalId;
            ShardNodeRanges = shardNodeRanges;
            ShardIndexByNodeIndex = shardIndexByNodeIndex;
        }

        /// <summary>全篇节点，按 NodeId 升序（与未分片实现同一序）。</summary>
        internal NLCPGNode[] Nodes { get; }

        /// <summary>与 <see cref="Nodes"/> 同下标的稳定 id。</summary>
        internal string[] Ids { get; }

        /// <summary>与 <see cref="Nodes"/> 同下标的归一化节点文件路径（已套用 sourcePath 回落）。</summary>
        internal string[] FilePaths { get; }

        /// <summary>本地 NodeId → 节点下标。仅含 NodeId 非空的节点（与未分片实现一致）。</summary>
        internal Dictionary<NodeId, int> NodeIndexByLocalId { get; }

        internal ShardRange[] ShardNodeRanges { get; }

        internal int[] ShardIndexByNodeIndex { get; }

        internal int ShardCount => ShardNodeRanges.Length;
    }

    /// <summary>分片的节点区间：<c>[Start, Start + Count)</c>，按节点序号连续切分。</summary>
    private readonly record struct ShardRange(int Start, int Count);

    /// <summary>已物化的单片 payload，及其在文档内的下标与分片总数。</summary>
    internal readonly record struct DocumentShard(
      int ShardIndex,
      int ShardCount,
      FileExportProjection Projection);

    /// <summary>分片后的边归属：边的下标桶、解析结果与边界节点。</summary>
    /// <remarks>
    /// 边按下标分桶，而不是先物化成 <see cref="FileEdgeEntry"/> 再分片——
    /// 后者会让全篇 DTO 同时在内存里，正是分片要消除的峰值。
    /// </remarks>
    private sealed class ShardedEdgeInputs
    {
        internal ShardedEdgeInputs(
          NLCPGEdge[] sortedEdges,
          int[] sourceIndex,
          int[] targetIndex,
          List<int>[] edgeIndexesByShard,
          int[][] boundaryNodeIndexesByShard)
        {
            SortedEdges = sortedEdges;
            SourceIndex = sourceIndex;
            TargetIndex = targetIndex;
            EdgeIndexesByShard = edgeIndexesByShard;
            BoundaryNodeIndexesByShard = boundaryNodeIndexesByShard;
        }

        /// <summary>全篇边，按既有三键序（SourceNodeId → Kind 名 → TargetNodeId）。</summary>
        internal NLCPGEdge[] SortedEdges { get; }

        /// <summary>与 <see cref="SortedEdges"/> 同下标的节点下标；-1 表示端点未解析。</summary>
        internal int[] SourceIndex { get; }

        internal int[] TargetIndex { get; }

        /// <summary>各分片拥有的边下标（升序，即保持既有边序）。</summary>
        internal List<int>[] EdgeIndexesByShard { get; }

        /// <summary>各分片被其边引用、但不属于该分片的节点下标（升序、去重）。</summary>
        internal int[][] BoundaryNodeIndexesByShard { get; }
    }

    /// <summary>
    /// 把 <b>单篇文档</b>的图按 <paramref name="configuredShardCount"/> 切成若干 payload，
    /// **逐片惰性产出**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是分片的唯一入口，也是契约测试直接调用的接缝：它固定了「整篇算 id、再分片物化」的顺序，
    /// 因此不存在各分片各自从 ordinal=0 起算而产生重复 id 的可能。
    /// </para>
    /// <para>
    /// <b>必须是惰性序列，不可是 List。</b>调用方是「物化一片 → 写出 → 释放」的循环；
    /// 若这里先返回一个装满全分片的列表，则全部 <see cref="FileNodeEntry"/>/
    /// <see cref="FileEdgeEntry"/> 会同时驻留，峰值与不分片时相同（分片的内存收益归零）。
    /// 实测（Build/shard-perf，20k 节点/60k 边，取走第 0 片后测「继续消费剩余片」的保留增量）：
    /// 4 片时急切 5.3 MB / 惰性 1.8 MB，16 片时急切 6.6 MB / 惰性 0.4 MB。
    /// 整篇输入（id、路径、边序、端点下标）在两种方式下都必须常驻——它们被迭代器闭包持有，
    /// 也正是「不改变任何记录取值」的前提。
    /// </para>
    /// <para>
    /// ⚠ 但惰性只省下「payload DTO」这一段。端到端实测（同一 fixture）表明：
    /// 导出阶段峰值由**分析/构图**主导（导出关闭 1210 MB vs 开启 1399~1459 MB），
    /// 分片 8 片相对不分片仅约 −4%，且投影与写盘耗时上升、payload 膨胀 40%。
    /// 故该开关不能当作「让大文档导出不再 OOM」的手段。
    /// </para>
    /// <para>
    /// <paramref name="configuredShardCount"/> &lt;= 1 时产出单个元素，其 <c>Projection</c>
    /// 与分片改造前的逐文档 projection 逐字段相同。
    /// </para>
    /// <para>
    /// ⚠ 惰性还改变了失败语义：各片随枚举依次落盘，故第 N 片失败时前 N-1 片**已经在磁盘上**
    /// （且已被调用方计入 manifest）。这比"先全部物化再写"更符合既有的逐文档容错策略。
    /// </para>
    /// </remarks>
    internal static IEnumerable<DocumentShard> BuildDocumentShardProjections(
      NLCPGGraph graph,
      string sourcePath,
      string projectRoot,
      string? filePathPrefix,
      int configuredShardCount,
      CancellationToken cancellationToken)
    {
        // 这两个「整篇」步骤在首次 MoveNext 时执行一次，随后被迭代器闭包持有。
        var document = PrepareDocumentProjection(
          graph,
          sourcePath,
          projectRoot,
          filePathPrefix,
          configuredShardCount,
          cancellationToken);
        var edges = MaterializeShardEdgeInputs(graph, document, cancellationToken);
        for (var shardIndex = 0; shardIndex < document.ShardCount; shardIndex += 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new DocumentShard(
              shardIndex,
              document.ShardCount,
              BuildShardProjection(graph, sourcePath, document, edges, shardIndex, cancellationToken));
        }
    }

    /// <summary>
    /// 单片（或并行单片）的落盘计划：投影已在内存，只差「算什么路径、写哪个文件」。
    /// </summary>
    /// <remarks>
    /// 串行与并行两条路径共用它，保证「同一片 → 同一路径、同一条 manifest 记录」，
    /// 故并行不改变任何输出（并行度只影响这些写入的时序，不影响其集合与内容）。
    /// </remarks>
    private readonly record struct ShardWritePlan(
      string RelativePath,
      string FullPath,
      FileExportEntry Entry);

    // 由分片与命名规则算出该片的落盘计划。串行与并行路径共用，是"两条路径输出一致"的单点保证。
    private static ShardWritePlan PlanShardWrite(
      DocumentShard shard,
      string sourcePath,
      string outputRelativePath,
      string outputFilePath,
      string outputPath)
    {
      // 单片（未分片）保持既有文件名与既有单 payload 布局，逐字节不变；
      // 多片才加 .part-NNN 后缀。payload 内部 schema 与 sourcePath 不变。
      var shardRelativePath = shard.ShardCount == 1
        ? outputRelativePath
        : sourcePath
          + ".part-"
          + shard.ShardIndex.ToString("D3", CultureInfo.InvariantCulture)
          + ".json";
      return new ShardWritePlan(
        shardRelativePath,
        shard.ShardCount == 1
          ? outputFilePath
          : CombineRelativePath(outputPath, shardRelativePath),
        new FileExportEntry(
          sourcePath,
          shardRelativePath,
          "written",
          shard.Projection.Nodes.Count,
          shard.Projection.Edges.Count,
          null));
    }

    /// <summary>一次文档导出的落盘结果汇总；<see cref="ShardCount"/> 是该文档实际产出的片数。</summary>
    // 对契约测试可见：并行与串行两条路径的输出一致性必须直接对生产实现断言
    // （见 ProjectJsonDocumentShardTests 的不变量 7/8），而不是在测试里复制一份分片逻辑。
    internal readonly record struct ShardWriteOutcome(
      long Bytes,
      List<FileExportEntry> Entries,
      int ShardCount,
      long ProjectionMs,
      long WriteMs,
      // 分片字节闸门的观测面：峰值同时在途的片数、以及为等额度累计阻塞的毫秒。
      // 串行路径不经过闸门，故保持默认 0。它们只作度量与契约断言，不参与任何输出取值。
      int PeakParallelism = 0,
      long GateWaitMs = 0);

    /// <summary>
    /// 按 <paramref name="parallelism"/> 串行或并行地「逐片物化 → 落盘」，返回汇总。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>并行的正确性前提（三条）：</b>
    /// ① 整篇身份（稳定 id、ordinal、边序、端点下标）仍**串行算一次**才分片——
    ///    这是「分片不改变任何记录取值」的前提，并行不触碰它；
    /// ② <see cref="BuildShardProjection"/> 只读**冻结后**的图，故可并发调用：
    ///    <c>StringInterner.TryResolve</c> 自带锁、<c>_sourceByPath</c> 冻结后不再写
    ///    （写入口 <c>RegisterSource</c> 会 <c>EnsureMutable()</c> 拒绝）、Nodes/Edges 是不可变数组；
    /// ③ 各片写**互不相同**的路径，因此 <c>FileShare.None</c> 不冲突，
    ///    且 <c>WriteJsonAtomicallyAsync</c> 的临时名 <c>path + ".tmp"</c> 也各不相同，不会互相覆盖。
    /// </para>
    /// <para>
    /// <b>结果与并行度无关：</b>并行路径把各片结果按 <c>shardIndex</c> 归位后再展开，
    /// 与串行路径按分片序追加的顺序一致，故 manifest 的 <c>files[]</c> 与 payload 逐字节相同。
    /// 并行度只影响这些写入的**时序**，不影响其集合与内容。
    /// </para>
    /// <para>
    /// <b>并行度是上限，额度是另一道上限：</b>并行路径还经
    /// <see cref="MemoryAdmissionGate"/> 按 <see cref="EstimateShardMemoryBytes"/> 逐片记账，
    /// 故即便 <paramref name="parallelism"/> 很大，同时在途的分片数也受
    /// <paramref name="memoryBudgetBytes"/> 约束（两者取小）。闸门只**等待**不失败，
    /// 且单片额度超过整个预算时仍放行，避免最大片永远排不到。
    /// </para>
    /// <para>
    /// ⚠ 峰值：并行度 N 意味着最多 N 片 DTO 同时驻留，峰值高于串行。
    /// 这正是它必须显式开关、且默认保持 1 的原因。闸门限制的是并行度上限，
    /// **不减少**「N 片同时驻留」这一基本事实。
    /// </para>
    /// </remarks>
    internal static async Task<ShardWriteOutcome> WriteDocumentShardsAsync(
      NLCPGGraph graph,
      string sourcePath,
      string projectRoot,
      string? filePathPrefix,
      int configuredShardCount,
      int parallelism,
      string outputRelativePath,
      string outputFilePath,
      string outputPath,
      bool collectMetrics,
      CancellationToken cancellationToken,
      long documentEstimatedBytes = 0,
      long memoryBudgetBytes = 0)
    {
      if (parallelism <= 1)
      {
        // 串行：沿用惰性接缝，保持与引入并行前**完全相同**的物化/释放节奏与执行序。
        var serialEntries = new List<FileExportEntry>();
        long serialBytes = 0;
        long serialProjectionMs = 0;
        long serialWriteMs = 0;
        var serialShardCount = 0;
        using var enumerator = BuildDocumentShardProjections(
          graph,
          sourcePath,
          projectRoot,
          filePathPrefix,
          configuredShardCount,
          cancellationToken).GetEnumerator();
        while (true)
        {
          var projectionStopwatch = collectMetrics ? Stopwatch.StartNew() : null;
          var hasShard = enumerator.MoveNext();
          projectionStopwatch?.Stop();
          if (!hasShard)
          {
            break;
          }

          var shard = enumerator.Current;
          serialShardCount += 1;
          cancellationToken.ThrowIfCancellationRequested();
          var plan = PlanShardWrite(
            shard,
            sourcePath,
            outputRelativePath,
            outputFilePath,
            outputPath);
          var writeStopwatch = collectMetrics ? Stopwatch.StartNew() : null;
          serialBytes += await WriteJsonAtomicallyAsync(
            plan.FullPath,
            shard.Projection,
            cancellationToken).ConfigureAwait(false);
          writeStopwatch?.Stop();
          serialProjectionMs += projectionStopwatch?.ElapsedMilliseconds ?? 0;
          serialWriteMs += writeStopwatch?.ElapsedMilliseconds ?? 0;
          serialEntries.Add(plan.Entry);
        }

        return new ShardWriteOutcome(
          serialBytes,
          serialEntries,
          serialShardCount,
          serialProjectionMs,
          serialWriteMs);
      }

      // 并行：整篇身份与边序仍**串行算一次**（前提 ①），并行的是随后最贵的「逐片物化 + 落盘」。
      // 不能用惰性接缝来并行：它的 MoveNext 把「推进枚举」与「物化该片」绑在一起，
      // 取片必须串行化，于是投影也被串行化，并行只覆盖写盘——那正是本项要避免的低收益形态。
      // 故这里直接用下层两段：整篇准备 + 按下标并行物化。
      var document = PrepareDocumentProjection(
        graph,
        sourcePath,
        projectRoot,
        filePathPrefix,
        configuredShardCount,
        cancellationToken);
      var shardedEdges = MaterializeShardEdgeInputs(graph, document, cancellationToken);

      var perShardEntry = new FileExportEntry[document.ShardCount];
      var perShardBytes = new long[document.ShardCount];
      var perShardProjectionMs = new long[document.ShardCount];
      var perShardWriteMs = new long[document.ShardCount];

      // 分片级字节闸门：并行度是「条数」上限，额度是「总量」上限，两者取小。
      // 片大小本就不均（实测 8 片时节点数 3.8x 偏斜），只限条数会放行 4 个最大片，
      // 故这里按估算字节记账。
      // 预算沿用文档级同一个数（BuildMemoryBudget），避免两套预算口径；
      // 单片额度超过整个预算时闸门仍放行（见 MemoryAdmissionGate），否则最大片永远排不到。
      var effectiveBudget = memoryBudgetBytes > 0 ? memoryBudgetBytes : BuildMemoryBudget(0);
      var documentEstimate = documentEstimatedBytes > 0 ? documentEstimatedBytes : effectiveBudget;
      // 各片份额之和恰为该文档的记录总数，故分摊不改变总预算口径。
      long totalRecords = 0;
      for (var shardIndex = 0; shardIndex < document.ShardCount; shardIndex += 1)
      {
        totalRecords += CountShardRecords(document, shardedEdges, shardIndex);
      }

      using var shardGate = new MemoryAdmissionGate(effectiveBudget, parallelism);
      await Parallel.ForEachAsync(
        Enumerable.Range(0, document.ShardCount),
        new ParallelOptions
        {
          MaxDegreeOfParallelism = parallelism,
          CancellationToken = cancellationToken,
        },
        async (shardIndex, token) =>
        {
          // 先按额度准入，再做该片的物化与落盘；超出预算时在此等待而非失败。
          using var admission = await shardGate
            .AcquireAsync(
              EstimateShardMemoryBytes(
                documentEstimate,
                CountShardRecords(document, shardedEdges, shardIndex),
                totalRecords),
              token).ConfigureAwait(false);
          var projectionStopwatch = collectMetrics ? Stopwatch.StartNew() : null;
          var projection = BuildShardProjection(
            graph,
            sourcePath,
            document,
            shardedEdges,
            shardIndex,
            token);
          projectionStopwatch?.Stop();
          var shard = new DocumentShard(shardIndex, document.ShardCount, projection);
          var plan = PlanShardWrite(
            shard,
            sourcePath,
            outputRelativePath,
            outputFilePath,
            outputPath);
          var writeStopwatch = collectMetrics ? Stopwatch.StartNew() : null;
          perShardBytes[shardIndex] = await WriteJsonAtomicallyAsync(
            plan.FullPath,
            projection,
            token).ConfigureAwait(false);
          writeStopwatch?.Stop();
          perShardEntry[shardIndex] = plan.Entry;
          perShardProjectionMs[shardIndex] = projectionStopwatch?.ElapsedMilliseconds ?? 0;
          perShardWriteMs[shardIndex] = writeStopwatch?.ElapsedMilliseconds ?? 0;
        }).ConfigureAwait(false);

      // 按下标归位展开，与串行路径的分片序一致（并行完成序不定，故不能按完成序追加）。
      var entries = new List<FileExportEntry>(document.ShardCount);
      long bytes = 0;
      long projectionMs = 0;
      long writeMs = 0;
      for (var shardIndex = 0; shardIndex < document.ShardCount; shardIndex += 1)
      {
        entries.Add(perShardEntry[shardIndex]);
        bytes += perShardBytes[shardIndex];
        projectionMs += perShardProjectionMs[shardIndex];
        writeMs += perShardWriteMs[shardIndex];
      }

      return new ShardWriteOutcome(
        bytes,
        entries,
        document.ShardCount,
        projectionMs,
        writeMs,
        shardGate.PeakInFlight,
        shardGate.WaitMilliseconds);
    }

    /// <summary>该片将物化的记录数（自有节点 + 边界节点 + 归属该片的边），用于额度分摊。</summary>
    private static long CountShardRecords(
      DocumentProjection projection,
      ShardedEdgeInputs shardedEdges,
      int shardIndex)
    {
        return projection.ShardNodeRanges[shardIndex].Count
          + (long)shardedEdges.BoundaryNodeIndexesByShard[shardIndex].Length
          + shardedEdges.EdgeIndexesByShard[shardIndex].Count;
    }

    /// <summary>
    /// 把 <paramref name="configuredShardCount"/> 收敛为实际分片数：不超过节点数，至少 1。
    /// </summary>
    /// <remarks>
    /// 上限取节点数是为了不产出空分片（空 payload 无意义，且会让 manifest 计数失真）。
    /// 值为 1（默认）时不分片，输出与未分片实现逐字节相同。
    /// </remarks>
    private static int ResolveShardCount(int configuredShardCount, int nodeCount)
    {
        if (configuredShardCount <= 1 || nodeCount <= 1)
        {
            return 1;
        }

        return Math.Min(configuredShardCount, nodeCount);
    }

    /// <summary>把 <paramref name="nodeCount"/> 个节点按连续区间均分为 <paramref name="shardCount"/> 片。</summary>
    /// <remarks>
    /// 余数分给前若干片（每片多 1 个），故分片数恒等于 <paramref name="shardCount"/> 且无空片。
    /// <para>
    /// ⚠ 实测（Build/shard-perf，真实导出 591023 节点的文档）：<b>片是按「自有节点数」均分的，
    /// 但落盘后各片大小极不均</b>——8 片时最小 73878、最大 279682 节点（3.8x），
    /// 因为「边引用的跨片目标节点」也进本片，而边分布本身并不均。
    /// </para>
    /// <para>
    /// 此前这里写着「连续区间让邻接记录尽量同片、边界节点最少」，这是**错的**：
    /// 节点按 <c>NodeId</c>（内容哈希）排序，与边的拓扑无关，
    /// 故连续区间相对边拓扑等价于随机划分。实测 8 片时节点记录膨胀 109%
    /// （591023 → 1236697），16 片时 118%。若将来要压低膨胀，应改按边连通性分区，
    /// 而不是调这里的区间算法。
    /// </para>
    /// </remarks>
    private static ShardRange[] PlanShardNodeRanges(int nodeCount, int shardCount)
    {
        var ranges = new ShardRange[shardCount];
        var baseSize = nodeCount / shardCount;
        var remainder = nodeCount % shardCount;
        var start = 0;
        for (var index = 0; index < shardCount; index += 1)
        {
            var count = baseSize + (index < remainder ? 1 : 0);
            ranges[index] = new ShardRange(start, count);
            start += count;
        }

        return ranges;
    }

    /// <summary>
    /// 整篇计算节点身份（稳定 id、归一化路径、分片归属），供各分片共用。
    /// </summary>
    /// <remarks>
    /// 本方法是分片的**唯一**「全篇」步骤。它只保留 id 字符串与路径引用，
    /// **不**保留全篇的节点/边 DTO——后者是分片真正省下的部分：
    /// 旧实现会同时驻留全部 <c>FileNodeEntry</c> 与 <c>FileEdgeEntry</c> 直到序列化结束，
    /// 现在每片物化完即写、即释放。
    /// <para>
    /// ⚠ 边界要说清：<b>CPG 构建本身的峰值不在分片范围内</b>。图在这个方法之前就已建好并整篇驻留，
    /// 故分片降低的是「物化 payload」这一段，不是建立 CPG 的那一段；两者谁主导峰值取决于文档。
    /// </para>
    /// </remarks>
    private static DocumentProjection PrepareDocumentProjection(
      NLCPGGraph graph,
      string sourcePath,
      string projectRoot,
      string? filePathPrefix,
      int configuredShardCount,
      CancellationToken cancellationToken)
    {
        var nodes = graph.Nodes
          .OrderBy(node => node.NodeId?.Value)
          .ToArray();
        var ids = new string[nodes.Length];
        var filePaths = new string[nodes.Length];
        var nodeIndexByLocalId = new Dictionary<NodeId, int>(nodes.Length);
        var occurrenceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < nodes.Length; index += 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodes[index];
            var nodeFilePath = NormalizeNodeFilePath(graph.ResolveFilePath(node), projectRoot);
            // 多项目时节点文件路径带上项目子目录前缀，与 payload 落盘位置一致。
            if (filePathPrefix is not null && nodeFilePath is not null)
            {
                nodeFilePath = nodeFilePath.StartsWith("external/", StringComparison.Ordinal)
                  ? nodeFilePath
                  : filePathPrefix + "/" + nodeFilePath;
            }

            var canonicalBase = BuildNodeCanonicalBase(graph, node, nodeFilePath);
            occurrenceCounts.TryGetValue(canonicalBase, out var ordinal);
            occurrenceCounts[canonicalBase] = ordinal + 1;
            ids[index] = CreateStableId(canonicalBase + "|ordinal=" + ordinal);
            // 未归一化（null）时与未分片实现一致地回落到 sourcePath；此处即固化该回落，
            // 使后续跨文件判定与节点记录使用同一个值，无需再查字典。
            filePaths[index] = nodeFilePath ?? sourcePath;
            if (node.NodeId is { } localNodeId)
            {
                nodeIndexByLocalId[localNodeId] = index;
            }
        }

        var shardCount = ResolveShardCount(configuredShardCount, nodes.Length);
        var shardNodeRanges = PlanShardNodeRanges(nodes.Length, shardCount);
        var shardIndexByNodeIndex = new int[nodes.Length];
        for (var shardIndex = 0; shardIndex < shardNodeRanges.Length; shardIndex += 1)
        {
            var range = shardNodeRanges[shardIndex];
            for (var index = range.Start; index < range.Start + range.Count; index += 1)
            {
                shardIndexByNodeIndex[index] = shardIndex;
            }
        }

        return new DocumentProjection(
          nodes,
          ids,
          filePaths,
          nodeIndexByLocalId,
          shardNodeRanges,
          shardIndexByNodeIndex);
    }

    /// <summary>
    /// 整篇排一次边序、解析端点，并按「边的源节点所属分片」把边下标分桶。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>边的归属规则：</b>边归其**源节点**所在的分片。这使规则唯一且与节点分片一致；
    /// 目标节点若落在别片，则被登记为该片的**边界节点**（见 <see cref="BuildShardProjection"/>）。
    /// </para>
    /// <para>
    /// 端点解析沿用未分片实现的 fail-closed 语义：任一端的 NodeId 未在图内节点表中出现，
    /// 该边整条丢弃（<c>-1</c>），而不是产出悬空引用。
    /// </para>
    /// </remarks>
    private static ShardedEdgeInputs MaterializeShardEdgeInputs(
      NLCPGGraph graph,
      DocumentProjection projection,
      CancellationToken cancellationToken)
    {
        var sortedEdges = graph.Edges
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
          .ThenBy(edge => edge.TargetNodeId)
          .ToArray();
        var sourceIndex = new int[sortedEdges.Length];
        var targetIndex = new int[sortedEdges.Length];
        var edgeIndexesByShard = new List<int>[projection.ShardCount];
        for (var shardIndex = 0; shardIndex < edgeIndexesByShard.Length; shardIndex += 1)
        {
            edgeIndexesByShard[shardIndex] = new List<int>();
        }

        var boundaryByShard = new HashSet<int>[projection.ShardCount];
        for (var shardIndex = 0; shardIndex < boundaryByShard.Length; shardIndex += 1)
        {
            boundaryByShard[shardIndex] = new HashSet<int>();
        }

        for (var edgeIndex = 0; edgeIndex < sortedEdges.Length; edgeIndex += 1)
        {
            if ((edgeIndex & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var edge = sortedEdges[edgeIndex];
            var hasSource = projection.NodeIndexByLocalId.TryGetValue(edge.SourceNodeId, out var resolvedSource);
            var hasTarget = projection.NodeIndexByLocalId.TryGetValue(edge.TargetNodeId, out var resolvedTarget);
            if (!hasSource || !hasTarget)
            {
                sourceIndex[edgeIndex] = -1;
                targetIndex[edgeIndex] = -1;
                continue;
            }

            sourceIndex[edgeIndex] = resolvedSource;
            targetIndex[edgeIndex] = resolvedTarget;
            var homeShard = projection.ShardIndexByNodeIndex[resolvedSource];
            edgeIndexesByShard[homeShard].Add(edgeIndex);
            // 目标落在别片时登记边界节点，使每片 payload 仍满足既有不变式：
            // 「该片任何一条边的两端都能在同一份 payload 内解析到节点」。
            if (projection.ShardIndexByNodeIndex[resolvedTarget] != homeShard)
            {
                boundaryByShard[homeShard].Add(resolvedTarget);
            }
        }

        var boundaryNodeIndexesByShard = new int[projection.ShardCount][];
        for (var shardIndex = 0; shardIndex < boundaryNodeIndexesByShard.Length; shardIndex += 1)
        {
            var boundary = new int[boundaryByShard[shardIndex].Count];
            boundaryByShard[shardIndex].CopyTo(boundary);
            // 升序：边界节点追加在自有节点之后，仍按 NodeId 序确定性排列。
            Array.Sort(boundary);
            boundaryNodeIndexesByShard[shardIndex] = boundary;
        }

        return new ShardedEdgeInputs(
          sortedEdges,
          sourceIndex,
          targetIndex,
          edgeIndexesByShard,
          boundaryNodeIndexesByShard);
    }

    /// <summary>
    /// 物化**单片**的 payload：该片自有节点 + 边界节点（按下标升序合并），以及归属该片的边。
    /// </summary>
    /// <remarks>
    /// 合并两个各自升序的下标序列，使节点记录始终按 NodeId 升序——与未分片实现同序，
    /// 故单分片时输出逐字节相同。
    /// </remarks>
    private static FileExportProjection BuildShardProjection(
      NLCPGGraph graph,
      string sourcePath,
      DocumentProjection projection,
      ShardedEdgeInputs shardedEdges,
      int shardIndex,
      CancellationToken cancellationToken)
    {
        var range = projection.ShardNodeRanges[shardIndex];
        var boundary = shardedEdges.BoundaryNodeIndexesByShard[shardIndex];
        var nodeProjections = new List<FileNodeEntry>(range.Count + boundary.Length);
        var ownedEnd = range.Start + range.Count;
        var ownedCursor = range.Start;
        var boundaryCursor = 0;
        while (ownedCursor < ownedEnd || boundaryCursor < boundary.Length)
        {
            var takeOwned = boundaryCursor >= boundary.Length ||
              (ownedCursor < ownedEnd && ownedCursor < boundary[boundaryCursor]);
            var nodeIndex = takeOwned ? ownedCursor++ : boundary[boundaryCursor++];
            nodeProjections.Add(CreateNodeEntry(
              graph,
              projection.Nodes[nodeIndex],
              projection.Ids[nodeIndex],
              sourcePath,
              projection.FilePaths[nodeIndex]));
        }

        var edgeIndexes = shardedEdges.EdgeIndexesByShard[shardIndex];
        var edgeProjections = new List<FileEdgeEntry>(edgeIndexes.Count);
        foreach (var edgeIndex in edgeIndexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var edge = shardedEdges.SortedEdges[edgeIndex];
            var resolvedSource = shardedEdges.SourceIndex[edgeIndex];
            var resolvedTarget = shardedEdges.TargetIndex[edgeIndex];
            edgeProjections.Add(new FileEdgeEntry(
              projection.Ids[resolvedSource],
              projection.Ids[resolvedTarget],
              edge.Kind.ToString(),
              edge.StructuredLabel?.StableKey,
              edge.ContextId?.Value,
              edge.CallSiteContext is { } callSite
                ? new CallSiteEntry(callSite.FilePath, callSite.SpanStart, callSite.SpanEnd, callSite.DisplayName)
                : null,
              !string.Equals(
                projection.FilePaths[resolvedSource],
                projection.FilePaths[resolvedTarget],
                StringComparison.Ordinal)));
        }

        return new FileExportProjection(
          1,
          sourcePath,
          nodeProjections,
          edgeProjections);
    }

    private static FileNodeEntry CreateNodeEntry(
      NLCPGGraph graph,
      NLCPGNode node,
      string id,
      string sourcePath,
      string? normalizedFilePath)
    {
        return new FileNodeEntry(
          id,
          node.NodeId?.Value,
          node.Kind.ToString(),
          graph.ResolveDisplayKind(node),
          graph.ResolveName(node),
          graph.ResolveFullName(node),
          graph.ResolveSignature(node),
          node.DispatchKind?.ToString(),
          graph.ResolveTypeFullName(node),
          normalizedFilePath ?? sourcePath,
          node.SpanStart,
          node.SpanEnd,
          node.IsImplicit);
    }

    private static string BuildNodeCanonicalBase(NLCPGGraph graph, NLCPGNode node, string? normalizedFilePath)
    {
        return string.Join(
          "|",
          node.Kind,
          normalizedFilePath,
          node.SpanStart,
          node.SpanEnd,
          graph.ResolveDisplayKind(node),
          graph.ResolveName(node),
          graph.ResolveFullName(node),
          graph.ResolveSignature(node),
          node.DispatchKind,
          graph.ResolveTypeFullName(node),
          node.IsImplicit);
    }

    private static string CreateStableId(string value)
    {
        return "node-" + Convert.ToHexString(
          SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string? NormalizeNodeFilePath(string? filePath, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (filePath.Contains("://", StringComparison.Ordinal))
        {
            return filePath.Replace('\\', '/');
        }

        var fullPath = Path.GetFullPath(filePath);
        var relativePath = Path.GetRelativePath(projectRoot, fullPath);
        return relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
          ? "external/" + fullPath.Replace('\\', '/')
          : NormalizePath(relativePath);
    }

    private static string ToRelativePath(string projectRoot, string filePath)
    {
        var relativePath = Path.GetRelativePath(projectRoot, Path.GetFullPath(filePath));
        if (relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Source file '{filePath}' is outside the project root '{projectRoot}'.");
        }

        return NormalizePath(relativePath);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string CombineRelativePath(string root, string relativePath)
    {
        var nativeRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var outputPath = Path.GetFullPath(Path.Combine(root, nativeRelativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        return outputPath;
    }

    private static ExportDiagnostic MapWorkspaceDiagnostic(WorkspaceInputDiagnostic diagnostic)
    {
        return new ExportDiagnostic(
          "workspace",
          diagnostic.Code,
          diagnostic.Message,
          diagnostic.Severity.ToString(),
          diagnostic.Path,
          diagnostic.ProjectPath,
          diagnostic.TargetFramework);
    }

    private static ExportDiagnostic MapCompilationDiagnostic(
      Diagnostic diagnostic,
      string projectRoot,
      string projectPath,
      string? targetFramework)
    {
        var path = diagnostic.Location.IsInSource && diagnostic.Location.SourceTree?.FilePath is { Length: > 0 } sourcePath
          ? NormalizeNodeFilePath(sourcePath, projectRoot)
          : null;
        return new ExportDiagnostic(
          "compilation",
          diagnostic.Id,
          diagnostic.ToString(),
          diagnostic.Severity.ToString(),
          path,
          projectPath,
          targetFramework);
    }

    private static string FormatDiagnostic(ExportDiagnostic diagnostic)
    {
        return $"[{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Message}";
    }

    // 单文档导出预算来自 ProjectExportOptions.ProjectMemoryBudgetBytes；
    // 其默认值 0 表示按物理内存自动取值（见 BuildMemoryBudget）。
    private static long BuildMemoryBudget(long configuredBytes)
    {
        if (configuredBytes > 0)
        {
            return configuredBytes;
        }

        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (available <= 0)
        {
            return 4L * 1024 * 1024 * 1024;
        }

        // 留出余量给工作区加载与 GC 本身；单文档实测峰值可达 3.8 GB（NPC.cs）。
        return Math.Max(512L * 1024 * 1024, available / 2);
    }

    // 源码字节 → 常驻内存估算。依据实测（NPC.cs：源码 2,147,283 B → payload 2.29 GB
    // 即 1066×，工作集峰值 3,799 MB 即 1856×）。取 2000× 对齐**工作集峰值**而非 payload，
    // 因为决定 OOM 的是峰值驻留，再加上 Roslyn 语义模型本身的固定开销。
    private static long EstimateDocumentMemoryBytes(int sourceLength)
    {
        // sourceLength 是 int（≤ 2.1e9），乘 2000 后约 4.3e12，远不会溢出 long。
        const long amplification = 2000;
        const long fixedOverhead = 48L * 1024 * 1024;
        return (sourceLength * amplification) + fixedOverhead;
    }

    /// <summary>
    /// 把整篇文档的内存估算按「记录数占比」分摊到单个分片，供分片级字节闸门记账。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么用分摊而不是独立拟合：</b>本项明确跳过了前置校准（用户指定不做任何前置验证），
    /// 故这里不引入任何**未校准的新常数**，而是复用
    /// <see cref="EstimateDocumentMemoryBytes"/> 已经过实测的放大系数。
    /// 分摊的绝对量级因此继承同一个口径，闸门含义也变得直白：
    /// 预算是「最多允许几个整篇当量同时在途」，即 <c>budget / documentEstimatedBytes</c>。
    /// </para>
    /// <para>
    /// <b>各片份额之和恰为整篇估算</b>（<paramref name="shardRecords"/> 覆盖自有节点 + 边界节点 + 边，
    /// 且各片互不重叠），故闸门不会因分摊而系统性放大或缩小总预算。
    /// </para>
    /// <para>
    /// ⚠ <b>这不是精确的内存模型。</b>分片之间记录宽度一致，但字符串载荷分布不均，
    /// 故单片实际驻留与其记录数占比并不严格成正比。分摊给出的是**预算口径**，
    /// 不是精确预测；若将来要用它做容量规划，须先补 §3.3 的校准。
    /// </para>
    /// </remarks>
    private static long EstimateShardMemoryBytes(
      long documentEstimatedBytes,
      long shardRecords,
      long totalRecords)
    {
        if (totalRecords <= 0 || documentEstimatedBytes <= 0)
        {
            return 1;
        }

        // 向上取整，保证每片至少记 1 字节，避免 0 额度让记账失去意义。
        var share = (documentEstimatedBytes * shardRecords) / totalRecords;
        var rounded = ((documentEstimatedBytes * shardRecords) % totalRecords) == 0 ? share : share + 1;
        return Math.Max(1, rounded);
    }

    /// <summary>
    /// 按估算内存额度放行文档导出：并发上限既受 worker 数约束，也受总内存预算约束。
    /// 小文件可以占满 worker 数；GB 级文件则被迫串行，避免多个大文件同时驻留导致 OOM。
    /// 单个文档的估算超过整个预算时也放行，否则该文件永远无法导出。
    /// </summary>
    private sealed class MemoryAdmissionGate : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly long _budget;
        private long _outstanding;
        private int _inFlight;
        private int _peakInFlight;
        private long _waitMilliseconds;

        public MemoryAdmissionGate(long budgetBytes, int maxConcurrency)
        {
            _budget = Math.Max(1, budgetBytes);
            _semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency), Math.Max(1, maxConcurrency));
        }

        /// <summary>峰值同时在途的租约数。分片闸门据此断言「实际并发不超预算允许数」。</summary>
        internal int PeakInFlight => Volatile.Read(ref _peakInFlight);

        /// <summary>为等待额度而累计阻塞的毫秒数。用于区分「闸门在起作用」与「压根没阻塞」。</summary>
        internal long WaitMilliseconds => Interlocked.Read(ref _waitMilliseconds);

        public async Task<IDisposable> AcquireAsync(long estimatedBytes, CancellationToken cancellationToken)
        {
            var waitStopwatch = Stopwatch.StartNew();
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            var charge = Math.Min(estimatedBytes, _budget);
            var waited = false;
            while (true)
            {
                var current = Interlocked.Read(ref _outstanding);
                // 优先保证至少一个文档在跑：否则一个超大文件会把自己锁死在等待里。
                // 必须用 CAS 提交，否则并发线程会各自看到 0 并同时超发预算。
                if (current == 0 || current + charge <= _budget)
                {
                    if (Interlocked.CompareExchange(ref _outstanding, current + charge, current) == current)
                    {
                        // 仅在没有立刻拿到额度时才算等待，避免把正常的记帐开销计入。
                        if (waited)
                        {
                            Interlocked.Add(ref _waitMilliseconds, waitStopwatch.ElapsedMilliseconds);
                        }

                        TrackEnter();
                        return new Lease(this, charge);
                    }

                    continue;
                }

                waited = true;
                await Task.Delay(15, cancellationToken).ConfigureAwait(false);
            }
        }

        private void TrackEnter()
        {
            var inFlight = Interlocked.Increment(ref _inFlight);
            // 无锁地把峰值抬到观测到的最大值（并发下允许保守偏低，不追求精确）。
            while (true)
            {
                var observed = Volatile.Read(ref _peakInFlight);
                if (inFlight <= observed || Interlocked.CompareExchange(ref _peakInFlight, inFlight, observed) == observed)
                {
                    return;
                }
            }
        }

        public void Dispose()
        {
            _semaphore.Dispose();
        }

        private void Release(long charge)
        {
            Interlocked.Decrement(ref _inFlight);
            Interlocked.Add(ref _outstanding, -charge);
            _semaphore.Release();
        }

        private sealed class Lease : IDisposable
        {
            private MemoryAdmissionGate? _gate;
            private readonly long _charge;

            public Lease(MemoryAdmissionGate gate, long charge)
            {
                _gate = gate;
                _charge = charge;
            }

            public void Dispose()
            {
                var gate = Interlocked.Exchange(ref _gate, null);
                gate?.Release(_charge);
            }
        }
    }

    // 返回写出到文件的**实际字节数**（含 BOM），供阶段度量记录 payload 体量。
    // 非度量的调用方忽略返回值，行为不变。
    /// <summary>
    /// 把「额外请求的能力位」并到 <see cref="NLCPGCapability.Default"/> 上，供 builder 使用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么必须在这里并，而不能直接透传：</b>
    /// <c>NLCPGBuilder.ResolveCapabilityBuildPlan</c> 对非 <c>null</c> 的请求走
    /// <c>Aggregate(None, OR)</c>，即**替换**默认集，而不是并入。直接透传
    /// <c>["InterproceduralDataFlow"]</c> 会丢掉 <c>SyntaxToken</c> / <c>Reference</c> /
    /// <c>TypeRef</c> / <c>Cfg</c> 等 <c>Default</c> 位，产出的 payload 反而比不开该开关
    /// 时**更少**边。已实测（Terraria/Collision.cs）：SyntaxToken 28109→0、
    /// Reference 9829→0、TypeRef 8043→0、CfgNext 638→0。
    /// </para>
    /// <para>
    /// 返回 <c>null</c>（而非空数组）当且仅当没有额外请求：只有 <c>null</c> 才会让 builder
    /// 走 <c>NLCPGCapability.Default</c>；空数组会被折叠成 <c>None</c>，产出几乎无边的图。
    /// </para>
    /// </remarks>
    internal static IReadOnlyCollection<NLCPGCapability>? MergeRequestedCapabilities(
      IReadOnlyCollection<NLCPGCapability>? extraCapabilities)
    {
        if (extraCapabilities is not { Count: > 0 })
        {
            return null;
        }

        var resolved = NLCPGCapability.Default;
        foreach (var capability in extraCapabilities)
        {
            resolved |= capability;
        }

        return new[] { resolved };
    }

    private static async Task<long> WriteJsonAsync<T>(
      string path,
      T value,
      CancellationToken cancellationToken)
    {
        // 必须用 SerializeAsync(Stream, ...)：**不得**改成下面任何一种
        //   - File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, opts))   （旧实现）
        //   - JsonSerializer.Serialize(writer, value, opts)
        // 这两个重载都会先把整份 payload 序列化成**一个连续的 byte[]/string**。
        // 单个文件的 CPG payload 可以超过 .NET 单数组上限，实测 Terraria\NPC.cs
        // 抛出 "Cannot allocate a buffer of size 2147487655"（≈ Array.MaxLength），
        // 使整次导出以 exit 1 收尾、且 3 个最大文件永远导不出来。
        // 已用独立探针验证（Build/worker8-utilization/Utf8Probe）：
        //   同一份含 2 千万实体的对象，Serialize(writer, ...) 抛上述异常；
        //   SerializeAsync(stream, ...) 写出 4.19 GiB、峰值内存仅 +0.01 GB。
        // 写出前补 UTF-8 BOM（Encoding.UTF8.GetPreamble），使输出与旧实现**逐字节一致**：
        // 既有 964 个 payload 都带 BOM（EF BB BF），且 Utf8JsonWriter 默认缩进/换行
        // 与 JsonSerializerOptions.WriteIndented 的产物相同（探针 new-a 已比对为 ident）。
        await using var stream = new FileStream(
          path,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 1 << 16,
          useAsync: true);
        await stream.WriteAsync(Encoding.UTF8.GetPreamble(), cancellationToken)
          .ConfigureAwait(false);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken)
          .ConfigureAwait(false);
        // Flush 后再读长度：SerializeAsync 可能仍有缓冲未落到 Length 上。
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return stream.Length;
    }

    private static async Task<long> WriteJsonAtomicallyAsync<T>(
      string path,
      T value,
      CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            var bytesWritten = await WriteJsonAsync(temporaryPath, value, cancellationToken)
              .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
            return bytesWritten;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 临时文件清理是尽力而为：导出结果已经确定，清理失败不应改变结果。
        }
        catch (UnauthorizedAccessException)
        {
            // 临时文件清理是尽力而为：导出结果已经确定，清理失败不应改变结果。
        }
    }

    // 这些 DTO 声明为 internal 而非 private：分片接缝（DocumentShard）会暴露它们，
    // 契约测试也需要按节点 id / 边端点断言「分片不改变任何记录取值」。
    internal sealed record FileExportProjection(
      int SchemaVersion,
      string SourcePath,
      IReadOnlyList<FileNodeEntry> Nodes,
      IReadOnlyList<FileEdgeEntry> Edges);

    // manifest 头部：项目维度与汇总计数。节点/边不进 manifest，只存在于各 payload 内。
    private sealed record ManifestHeader(
      int SchemaVersion,
      string Tool,
      ExportStatus Status,
      ProjectEntry Project,
      IReadOnlyList<ProjectEntry> Projects,
      string OutputRoot,
      int TotalDocumentCount,
      int WrittenFileCount,
      int FailedFileCount,
      IReadOnlyList<FileExportEntry> Files,
      IReadOnlyList<ExportDiagnostic> Diagnostics);

    // 同一项目条目既用于 manifest.project（首个项目，保持旧消费方兼容）也用于 manifest.projects。
    // Status 用字符串：JsonOptions 未启用字符串枚举转换，用枚举会写成数字，与 manifest.status 不一致。
    private sealed record ProjectEntry(
      string Path,
      string? Name,
      string? AssemblyName,
      string? TargetFramework,
      string Configuration,
      string Platform,
      string? Fingerprint,
      string Status,
      string? OutputDirectory,
      int TotalDocumentCount,
      int WrittenFileCount,
      int FailedFileCount,
      IReadOnlyList<FileExportEntry> Files);

    // 与 FileExportProjection 同因由声明为 internal：ShardWriteOutcome 经接缝暴露它，
    // 契约测试据此断言「并行与串行的 manifest 记录逐条相同」。
    internal sealed record FileExportEntry(
      string SourcePath,
      string OutputPath,
      string Status,
      int NodeCount,
      int EdgeCount,
      string? Error);

    internal sealed record FileNodeEntry(
      string Id,
      uint? LocalNodeId,
      string Kind,
      string DisplayKind,
      string? Name,
      string? FullName,
      string? Signature,
      string? DispatchKind,
      string? TypeFullName,
      string FilePath,
      int? SpanStart,
      int? SpanEnd,
      bool IsImplicit);

    internal sealed record FileEdgeEntry(
      string Source,
      string Target,
      string Kind,
      string? Label,
      string? ContextId,
      CallSiteEntry? CallSite,
      bool IsCrossFile);

    internal sealed record CallSiteEntry(
      string FilePath,
      int SpanStart,
      int SpanEnd,
      string DisplayName);

    private sealed record ExportDiagnostic(
      string Source,
      string Code,
      string Message,
      string Severity,
      string? Path = null,
      string? ProjectPath = null,
      string? TargetFramework = null);
}
