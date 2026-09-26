using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Data.Sqlite;
using NLCPG.Builder;
using NLCPG.Model;
using NLISSN.Infrastructure.Workspace;

namespace NLCPG.ProjectJson;

/// <summary>导出完成度：<c>complete</c> 表示全部项目与文档都已产出，<c>incomplete</c> 表示存在失败项或被中断。</summary>
public enum ExportStatus
{
    Complete,
    Incomplete,
}

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

    // Sequential per-document export path; the only production export schedule.
    // 覆盖工作区的全部项目；无论正常结束、个别文档失败、取消还是异常都会写出 manifest，
    // 使中断的导出仍可判定完整性，而不是留下一堆无法解释的 payload。
    private async Task<ProjectExportResult> ExportOrderedOracleAsync(
      ProjectExportOptions options,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var outputPath = options.FullOutputPath;
        Directory.CreateDirectory(outputPath);
        var manifestPath = Path.Combine(outputPath, "manifest.json");
        var diagnosticMessages = new List<string>();
        var workspaceDiagnostics = new List<ExportDiagnostic>();
        var files = new List<FileExportEntry>();
        var globalNodes = new Dictionary<string, GlobalNodeEntry>(StringComparer.Ordinal);
        var globalEdges = new Dictionary<string, GlobalEdgeAccumulator>(StringComparer.Ordinal);
        var projectEntries = new List<ProjectEntry>();
        var totalDocumentCount = 0;
        var status = ExportStatus.Incomplete;
        var manifestWritten = false;
        ResumeCatalog? catalog = null;
        var catalogPath = Path.Combine(
          Directory.GetParent(outputPath)!.FullName,
          ".nlcpg-resume-catalog-" + Guid.NewGuid().ToString("N") + ".db");

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
            // manifest 是完整性与 resume 的判定依据，因此文件与诊断都按稳定键排序：
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
            IReadOnlyList<GlobalNodeEntry>? orderedNodes = null;
            IReadOnlyList<GlobalEdgeEntry>? orderedEdges = null;
            if (catalog is null)
            {
                orderedNodes = globalNodes.Values
                  .OrderBy(node => node.Id, StringComparer.Ordinal)
                  .ToArray();
                orderedEdges = globalEdges.Values
                  .Select(accumulator => accumulator.ToEntry())
                  .OrderBy(edge => edge.Source, StringComparer.Ordinal)
                  .ThenBy(edge => edge.Target, StringComparer.Ordinal)
                  .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
                  .ToArray();
            }

            await WriteManifestFileAsync(
              manifestPath,
              header,
              orderedNodes,
              orderedEdges,
              catalog,
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

            workspaceDiagnostics.AddRange(workspaceResult.Diagnostics.Select(MapWorkspaceDiagnostic));
            diagnosticMessages.AddRange(workspaceDiagnostics.Select(FormatDiagnostic));

            var snapshot = workspaceResult.Snapshot;
            if (!workspaceResult.IsSuccess || snapshot is null)
            {
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

            if (options.ResumeExistingOutput)
            {
                TryDeleteFile(manifestPath);
                TryDeleteFile(manifestPath + ".tmp");
                catalog = await ResumeCatalog.CreateAsync(catalogPath, cancellationToken)
                  .ConfigureAwait(false);
            }

            var useCatalog = catalog is not null;
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
                var projectionNodes = useCatalog
                  ? new Dictionary<string, GlobalNodeEntry>(StringComparer.Ordinal)
                  : globalNodes;
                var projectionEdges = useCatalog
                  ? new Dictionary<string, GlobalEdgeAccumulator>(StringComparer.Ordinal)
                  : globalEdges;
                var projectFailed = false;

                try
                {
                    foreach (var document in sourceDocuments)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
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
                            FileExportProjection? reusedProjection = null;
                            if (useCatalog && File.Exists(outputFilePath))
                            {
                                try
                                {
                                    reusedProjection = await ReadFileProjectionAsync(
                                      outputFilePath,
                                      sourcePath,
                                      cancellationToken).ConfigureAwait(false);
                                }
                                catch (Exception stale) when (
                                  stale is InvalidDataException or JsonException)
                                {
                                    // 既有 payload 损坏或来自旧布局：重新生成，而不是让该文件永久失败。
                                    var staleDiagnostic = new ExportDiagnostic(
                                      "document",
                                      "NLCPGEXP005",
                                      "Rebuilt an unusable existing payload: " + stale.Message,
                                      "Warning",
                                      sourcePath,
                                      project.ProjectPath,
                                      project.TargetFramework);
                                    workspaceDiagnostics.Add(staleDiagnostic);
                                    diagnosticMessages.Add(FormatDiagnostic(staleDiagnostic));
                                }
                            }

                            FileExportProjection projection;
                            if (reusedProjection is not null)
                            {
                                projection = reusedProjection;
                            }
                            else
                            {
                                var semanticModel = project.Compilation.GetSemanticModel(document.SyntaxTree);
                                var root = await document.SyntaxTree.GetRootAsync(cancellationToken)
                                  .ConfigureAwait(false);
                                var builderOptions = NLCPGBuilderOptions.CreateDefault() with
                                {
                                    MaxDegreeOfParallelism = options.EffectiveMaxDegreeOfParallelism,
                                };
                                var graph = new NLCPGBuilder(builderOptions).BuildFromSemanticModel(
                                  semanticModel,
                                  root,
                                  document.Source,
                                  document.FilePath);
                                projection = BuildFileProjection(
                                  graph,
                                  sourcePath,
                                  projectRoot,
                                  projectionNodes,
                                  projectionEdges,
                                  cancellationToken,
                                  filePathPrefix: projectDirectoryName);
                                await WriteJsonAtomicallyAsync(outputFilePath, projection, cancellationToken)
                                  .ConfigureAwait(false);
                            }

                            if (useCatalog)
                            {
                                await catalog!.AddProjectionAsync(projection, cancellationToken)
                                  .ConfigureAwait(false);
                            }

                            projectFiles.Add(new FileExportEntry(
                              sourcePath,
                              outputRelativePath,
                              "written",
                              projection.Nodes.Count,
                              projection.Edges.Count,
                              null));
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
            if (catalog is not null)
            {
                await catalog.DisposeAsync().ConfigureAwait(false);
            }

            TryDeleteFile(catalogPath);
            TryDeleteFile(catalogPath + "-wal");
            TryDeleteFile(catalogPath + "-shm");
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
          status);
    }

    // manifest 统一写出点：文本字段先写，节点/边可在 catalog 流式导出（resume）或从内存排序写出。
    private static async Task WriteManifestFileAsync(
      string path,
      ManifestHeader header,
      IReadOnlyList<GlobalNodeEntry>? orderedNodes,
      IReadOnlyList<GlobalEdgeEntry>? orderedEdges,
      ResumeCatalog? catalog,
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
                if (catalog is not null)
                {
                    writer.WritePropertyName("nodes");
                    writer.WriteStartArray();
                    await catalog.WriteNodesAsync(writer, cancellationToken).ConfigureAwait(false);
                    writer.WriteEndArray();
                    writer.WritePropertyName("edges");
                    writer.WriteStartArray();
                    await catalog.WriteEdgesAsync(writer, cancellationToken).ConfigureAwait(false);
                    writer.WriteEndArray();
                }
                else
                {
                    writer.WritePropertyName("nodes");
                    JsonSerializer.Serialize(
                      writer,
                      orderedNodes ?? Array.Empty<GlobalNodeEntry>(),
                      JsonOptions);
                    writer.WritePropertyName("edges");
                    JsonSerializer.Serialize(
                      writer,
                      orderedEdges ?? Array.Empty<GlobalEdgeEntry>(),
                      JsonOptions);
                }

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

    private static FileExportProjection BuildFileProjection(
      NLCPGGraph graph,
      string sourcePath,
      string projectRoot,
      IDictionary<string, GlobalNodeEntry> globalNodes,
      IDictionary<string, GlobalEdgeAccumulator> globalEdges,
      CancellationToken cancellationToken,
      string? sourceFingerprint = null,
      string? filePathPrefix = null)
    {
        var nodes = graph.Nodes
          .OrderBy(node => node.NodeId?.Value)
          .ToArray();
        var occurrenceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodeProjections = new List<FileNodeEntry>(nodes.Length);
        var nodeIdsByLocalId = new Dictionary<NodeId, string>();
        var nodeFilePathsByLocalId = new Dictionary<NodeId, string>(nodes.Length);
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            var id = CreateStableId(canonicalBase + "|ordinal=" + ordinal);
            var projection = CreateNodeEntry(graph, node, id, sourcePath, nodeFilePath);
            nodeProjections.Add(projection);
            if (node.NodeId is { } localNodeId)
            {
                nodeIdsByLocalId[localNodeId] = id;
                nodeFilePathsByLocalId[localNodeId] = nodeFilePath ?? sourcePath;
            }

            if (!globalNodes.ContainsKey(id))
            {
                globalNodes.Add(id, new GlobalNodeEntry(
                  id,
                  projection.Kind,
                  projection.DisplayKind,
                  projection.Name,
                  projection.FullName,
                  projection.Signature,
                  projection.DispatchKind,
                  projection.TypeFullName,
                  projection.FilePath,
                  projection.SpanStart,
                  projection.SpanEnd,
                  projection.IsImplicit));
            }
        }

        var edgeProjections = new List<FileEdgeEntry>();
        foreach (var edge in graph.Edges
          .OrderBy(edge => edge.SourceNodeId)
          .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
          .ThenBy(edge => edge.TargetNodeId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!nodeIdsByLocalId.TryGetValue(edge.SourceNodeId, out var sourceId) ||
                !nodeIdsByLocalId.TryGetValue(edge.TargetNodeId, out var targetId))
            {
                continue;
            }

            var edgeEntry = new FileEdgeEntry(
              sourceId,
              targetId,
              edge.Kind.ToString(),
              edge.StructuredLabel?.StableKey,
              edge.ContextId?.Value,
              edge.CallSiteContext is { } callSite
                ? new CallSiteEntry(callSite.FilePath, callSite.SpanStart, callSite.SpanEnd, callSite.DisplayName)
                : null,
              !string.Equals(
                nodeFilePathsByLocalId[edge.SourceNodeId],
                nodeFilePathsByLocalId[edge.TargetNodeId],
                StringComparison.Ordinal));
            edgeProjections.Add(edgeEntry);
            var edgeKey = string.Join(
              "|",
              edgeEntry.Source,
              edgeEntry.Target,
              edgeEntry.Kind,
              edgeEntry.Label,
              edgeEntry.ContextId,
              edgeEntry.CallSite?.FilePath,
              edgeEntry.CallSite?.SpanStart,
              edgeEntry.CallSite?.SpanEnd,
              edgeEntry.CallSite?.DisplayName);
            if (!globalEdges.TryGetValue(edgeKey, out var accumulator))
            {
                accumulator = new GlobalEdgeAccumulator(edgeEntry);
                globalEdges.Add(edgeKey, accumulator);
            }

            accumulator.AddFile(sourcePath);
        }

        return new FileExportProjection(
          1,
          sourcePath,
          nodeProjections,
          edgeProjections,
          sourceFingerprint);
    }

    private static async Task<FileExportProjection> ReadFileProjectionAsync(
      string path,
      string expectedSourcePath,
      CancellationToken cancellationToken,
      string? expectedSourceFingerprint = null)
    {
        await using var stream = new FileStream(
          path,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read);
        var projection = await JsonSerializer.DeserializeAsync<FileExportProjection>(
          stream,
          JsonOptions,
          cancellationToken).ConfigureAwait(false);
        if (projection is null || projection.SchemaVersion != 1)
        {
            throw new InvalidDataException($"The existing export payload '{path}' is invalid.");
        }

        ValidateProjectionShape(projection, path);

        if (!string.Equals(projection.SourcePath, expectedSourcePath, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
              $"The existing export payload '{path}' has source path '{projection.SourcePath}' " +
              $"instead of '{expectedSourcePath}'.");
        }

        if (expectedSourceFingerprint is not null &&
            projection.SourceFingerprint is not null &&
            !StringComparer.Ordinal.Equals(projection.SourceFingerprint, expectedSourceFingerprint))
        {
            throw new InvalidDataException(
              $"The existing export payload '{path}' has a stale source fingerprint.");
        }

        return projection;
    }

    private static void ValidateProjectionShape(
      FileExportProjection projection,
      string path)
    {
        if (string.IsNullOrWhiteSpace(projection.SourcePath) ||
            projection.Nodes is null || projection.Edges is null ||
            projection.Nodes.Any(node => node is null) ||
            projection.Edges.Any(edge => edge is null))
        {
            throw new InvalidDataException($"The existing export payload '{path}' is invalid.");
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in projection.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) ||
                string.IsNullOrWhiteSpace(node.Kind) ||
                string.IsNullOrWhiteSpace(node.DisplayKind) ||
                string.IsNullOrWhiteSpace(node.FilePath) ||
                !nodeIds.Add(node.Id))
            {
                throw new InvalidDataException(
                  $"The existing export payload '{path}' contains an invalid node.");
            }
        }

        foreach (var edge in projection.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.Source) ||
                string.IsNullOrWhiteSpace(edge.Target) ||
                string.IsNullOrWhiteSpace(edge.Kind) ||
                 (edge.CallSite is not null &&
                  (string.IsNullOrWhiteSpace(edge.CallSite.FilePath) ||
                   string.IsNullOrWhiteSpace(edge.CallSite.DisplayName))) ||
                 !nodeIds.Contains(edge.Source) ||
                 !nodeIds.Contains(edge.Target))
            {
                throw new InvalidDataException(
                  $"The existing export payload '{path}' contains an invalid edge or endpoint.");
            }
        }
    }

    private static string BuildEdgeKey(FileEdgeEntry edge)
    {
        return string.Join(
          "|",
          edge.Source,
          edge.Target,
          edge.Kind,
          edge.Label,
          edge.ContextId,
          edge.CallSite?.FilePath,
          edge.CallSite?.SpanStart,
          edge.CallSite?.SpanEnd,
          edge.CallSite?.DisplayName);
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

    private static async Task WriteJsonAsync<T>(
      string path,
      T value,
      CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
          path,
          JsonSerializer.Serialize(value, JsonOptions),
          Encoding.UTF8,
          cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
      string path,
      T value,
      CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            await WriteJsonAsync(temporaryPath, value, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
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
            // A temporary catalog is best-effort cleanup after the export result exists.
        }
        catch (UnauthorizedAccessException)
        {
            // A temporary catalog is best-effort cleanup after the export result exists.
        }
    }

    private sealed class ResumeCatalog : IAsyncDisposable
    {
        private const char FileSeparator = '\u001f';
        private readonly SqliteConnection _connection;
        private int _disposed;

        private ResumeCatalog(SqliteConnection connection)
        {
            _connection = connection;
        }

        public static async Task<ResumeCatalog> CreateAsync(
          string path,
          CancellationToken cancellationToken)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                // 关闭连接池：这是用完即删的临时 catalog，池化会让 Dispose 后文件句柄仍被占用而删不掉。
                Pooling = false,
            }.ToString());
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (var pragma = connection.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA journal_mode = DELETE; PRAGMA synchronous = NORMAL;";
                    await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using (var schema = connection.CreateCommand())
                {
                    schema.CommandText = """
                    CREATE TABLE nodes (
                      id TEXT NOT NULL PRIMARY KEY,
                      kind TEXT NOT NULL,
                      display_kind TEXT NOT NULL,
                      name TEXT,
                      full_name TEXT,
                      signature TEXT,
                      dispatch_kind TEXT,
                      type_full_name TEXT,
                      file_path TEXT NOT NULL,
                      span_start INTEGER,
                      span_end INTEGER,
                      is_implicit INTEGER NOT NULL
                    );
                    CREATE TABLE edges (
                      edge_key TEXT NOT NULL PRIMARY KEY,
                      source TEXT NOT NULL,
                      target TEXT NOT NULL,
                      kind TEXT NOT NULL,
                      label TEXT,
                      context_id TEXT,
                      call_site_file_path TEXT,
                      call_site_span_start INTEGER,
                      call_site_span_end INTEGER,
                      call_site_display_name TEXT,
                      is_cross_file INTEGER NOT NULL
                    );
                    CREATE TABLE edge_files (
                      edge_key TEXT NOT NULL,
                      file_path TEXT NOT NULL,
                      PRIMARY KEY (edge_key, file_path)
                    );
                    """;
                    await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                return new ResumeCatalog(connection);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public async Task AddProjectionAsync(
          FileExportProjection projection,
          CancellationToken cancellationToken)
        {
            await using var transaction = (SqliteTransaction)await _connection
              .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var node in projection.Nodes)
            {
                await using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT OR IGNORE INTO nodes (
                      id, kind, display_kind, name, full_name, signature, dispatch_kind,
                      type_full_name, file_path, span_start, span_end, is_implicit)
                    VALUES ($id, $kind, $display_kind, $name, $full_name, $signature,
                      $dispatch_kind, $type_full_name, $file_path, $span_start, $span_end,
                      $is_implicit);
                    """;
                AddNodeParameters(command, node);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var edge in projection.Edges)
            {
                var edgeKey = BuildEdgeKey(edge);
                await using (var edgeCommand = _connection.CreateCommand())
                {
                    edgeCommand.Transaction = transaction;
                    edgeCommand.CommandText = """
                        INSERT OR IGNORE INTO edges (
                          edge_key, source, target, kind, label, context_id,
                          call_site_file_path, call_site_span_start, call_site_span_end,
                          call_site_display_name, is_cross_file)
                        VALUES ($edge_key, $source, $target, $kind, $label, $context_id,
                          $call_site_file_path, $call_site_span_start, $call_site_span_end,
                          $call_site_display_name, $is_cross_file);
                        """;
                    AddEdgeParameters(edgeCommand, edgeKey, edge);
                    await edgeCommand.ExecuteNonQueryAsync(cancellationToken)
                      .ConfigureAwait(false);
                }

                await using var fileCommand = _connection.CreateCommand();
                fileCommand.Transaction = transaction;
                fileCommand.CommandText = """
                    INSERT OR IGNORE INTO edge_files(edge_key, file_path)
                    VALUES ($edge_key, $file_path);
                    """;
                fileCommand.Parameters.AddWithValue("$edge_key", edgeKey);
                fileCommand.Parameters.AddWithValue("$file_path", projection.SourcePath);
                await fileCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task WriteNodesAsync(
          Utf8JsonWriter writer,
          CancellationToken cancellationToken)
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, kind, display_kind, name, full_name, signature, dispatch_kind,
                       type_full_name, file_path, span_start, span_end, is_implicit
                FROM nodes
                ORDER BY id;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
              .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                writer.WriteStartObject();
                writer.WriteString("id", reader.GetString(0));
                writer.WriteString("kind", reader.GetString(1));
                writer.WriteString("displayKind", reader.GetString(2));
                WriteNullableString(writer, "name", reader, 3);
                WriteNullableString(writer, "fullName", reader, 4);
                WriteNullableString(writer, "signature", reader, 5);
                WriteNullableString(writer, "dispatchKind", reader, 6);
                WriteNullableString(writer, "typeFullName", reader, 7);
                writer.WriteString("filePath", reader.GetString(8));
                WriteNullableInt(writer, "spanStart", reader, 9);
                WriteNullableInt(writer, "spanEnd", reader, 10);
                writer.WriteBoolean("isImplicit", reader.GetInt64(11) != 0);
                writer.WriteEndObject();
            }
        }

        public async Task WriteEdgesAsync(
          Utf8JsonWriter writer,
          CancellationToken cancellationToken)
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT edge_key, source, target, kind, label, context_id,
                       call_site_file_path, call_site_span_start, call_site_span_end,
                       call_site_display_name, is_cross_file,
                       (SELECT group_concat(file_path, char(31))
                          FROM (SELECT file_path FROM edge_files
                                WHERE edge_key = edges.edge_key ORDER BY file_path))
                FROM edges
                ORDER BY edge_key;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
              .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                writer.WriteStartObject();
                writer.WriteString("source", reader.GetString(1));
                writer.WriteString("target", reader.GetString(2));
                writer.WriteString("kind", reader.GetString(3));
                WriteNullableString(writer, "label", reader, 4);
                WriteNullableString(writer, "contextId", reader, 5);
                if (reader.IsDBNull(6))
                {
                    writer.WriteNull("callSite");
                }
                else
                {
                    writer.WritePropertyName("callSite");
                    writer.WriteStartObject();
                    writer.WriteString("filePath", reader.GetString(6));
                    writer.WriteNumber("spanStart", reader.GetInt64(7));
                    writer.WriteNumber("spanEnd", reader.GetInt64(8));
                    writer.WriteString("displayName", reader.GetString(9));
                    writer.WriteEndObject();
                }

                writer.WritePropertyName("files");
                writer.WriteStartArray();
                if (!reader.IsDBNull(11))
                {
                    foreach (var file in reader.GetString(11).Split(FileSeparator))
                    {
                        writer.WriteStringValue(file);
                    }
                }

                writer.WriteEndArray();
                writer.WriteBoolean("isCrossFile", reader.GetInt64(10) != 0);
                writer.WriteEndObject();
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            _connection.Close();
            _connection.Dispose();
            return ValueTask.CompletedTask;
        }

        private static void AddNodeParameters(SqliteCommand command, FileNodeEntry node)
        {
            command.Parameters.AddWithValue("$id", node.Id);
            command.Parameters.AddWithValue("$kind", node.Kind);
            command.Parameters.AddWithValue("$display_kind", node.DisplayKind);
            command.Parameters.AddWithValue("$name", (object?)node.Name ?? DBNull.Value);
            command.Parameters.AddWithValue("$full_name", (object?)node.FullName ?? DBNull.Value);
            command.Parameters.AddWithValue("$signature", (object?)node.Signature ?? DBNull.Value);
            command.Parameters.AddWithValue("$dispatch_kind", (object?)node.DispatchKind ?? DBNull.Value);
            command.Parameters.AddWithValue("$type_full_name", (object?)node.TypeFullName ?? DBNull.Value);
            command.Parameters.AddWithValue("$file_path", node.FilePath);
            command.Parameters.AddWithValue("$span_start", (object?)node.SpanStart ?? DBNull.Value);
            command.Parameters.AddWithValue("$span_end", (object?)node.SpanEnd ?? DBNull.Value);
            command.Parameters.AddWithValue("$is_implicit", node.IsImplicit ? 1 : 0);
        }

        private static void AddEdgeParameters(
          SqliteCommand command,
          string edgeKey,
          FileEdgeEntry edge)
        {
            command.Parameters.AddWithValue("$edge_key", edgeKey);
            command.Parameters.AddWithValue("$source", edge.Source);
            command.Parameters.AddWithValue("$target", edge.Target);
            command.Parameters.AddWithValue("$kind", edge.Kind);
            command.Parameters.AddWithValue("$label", (object?)edge.Label ?? DBNull.Value);
            command.Parameters.AddWithValue("$context_id", (object?)edge.ContextId ?? DBNull.Value);
            command.Parameters.AddWithValue(
              "$call_site_file_path",
              (object?)edge.CallSite?.FilePath ?? DBNull.Value);
            command.Parameters.AddWithValue(
              "$call_site_span_start",
              (object?)edge.CallSite?.SpanStart ?? DBNull.Value);
            command.Parameters.AddWithValue(
              "$call_site_span_end",
              (object?)edge.CallSite?.SpanEnd ?? DBNull.Value);
            command.Parameters.AddWithValue(
              "$call_site_display_name",
              (object?)edge.CallSite?.DisplayName ?? DBNull.Value);
            command.Parameters.AddWithValue("$is_cross_file", edge.IsCrossFile ? 1 : 0);
        }

        private static void WriteNullableString(
          Utf8JsonWriter writer,
          string propertyName,
          SqliteDataReader reader,
          int ordinal)
        {
            if (reader.IsDBNull(ordinal))
            {
                writer.WriteNull(propertyName);
            }
            else
            {
                writer.WriteString(propertyName, reader.GetString(ordinal));
            }
        }

        private static void WriteNullableInt(
          Utf8JsonWriter writer,
          string propertyName,
          SqliteDataReader reader,
          int ordinal)
        {
            if (reader.IsDBNull(ordinal))
            {
                writer.WriteNull(propertyName);
            }
            else
            {
                writer.WriteNumber(propertyName, reader.GetInt64(ordinal));
            }
        }
    }

    private sealed class GlobalEdgeAccumulator
    {
        private readonly HashSet<string> _files = new(StringComparer.Ordinal);

        public GlobalEdgeAccumulator(FileEdgeEntry entry)
        {
            Entry = entry;
        }

        private FileEdgeEntry Entry { get; }

        public void AddFile(string file)
        {
            _files.Add(file);
        }

        public GlobalEdgeEntry ToEntry()
        {
            return new GlobalEdgeEntry(
              Entry.Source,
              Entry.Target,
              Entry.Kind,
              Entry.Label,
              Entry.ContextId,
              Entry.CallSite,
              _files.OrderBy(file => file, StringComparer.Ordinal).ToArray(),
              Entry.IsCrossFile);
        }
    }

    private sealed record FileExportProjection(
      int SchemaVersion,
      string SourcePath,
      IReadOnlyList<FileNodeEntry> Nodes,
      IReadOnlyList<FileEdgeEntry> Edges,
      string? SourceFingerprint = null);

    // manifest 头部：项目维度与汇总计数；节点/边由 WriteManifestFileAsync 决定流式或内存写出。
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

    private sealed record FileExportEntry(
      string SourcePath,
      string OutputPath,
      string Status,
      int NodeCount,
      int EdgeCount,
      string? Error);

    private sealed record FileNodeEntry(
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

    private sealed record FileEdgeEntry(
      string Source,
      string Target,
      string Kind,
      string? Label,
      string? ContextId,
      CallSiteEntry? CallSite,
      bool IsCrossFile);

    private sealed record GlobalNodeEntry(
      string Id,
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

    private sealed record GlobalEdgeEntry(
      string Source,
      string Target,
      string Kind,
      string? Label,
      string? ContextId,
      CallSiteEntry? CallSite,
      IReadOnlyList<string> Files,
      bool IsCrossFile);

    private sealed record CallSiteEntry(
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
