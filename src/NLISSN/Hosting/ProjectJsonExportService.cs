using NLCPG.ProjectJson;
using NLISSN.Core.Pipeline;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Infrastructure.Workspace;

namespace NLISSN.Hosting;

/// <summary>
/// 在 NLISSN 运行内联执行 CPG 项目级 JSON 导出（<c>artifacts.projectJson</c>）。
/// 导出实现位于 <c>NLCPG.ProjectJson</c> 组件；
/// 本服务只负责把 NLISSN 已解析的配置投影为 <see cref="ProjectExportOptions"/>。
/// </summary>
internal sealed class ProjectJsonExportService
{
    internal async Task<ProjectExportResult> ExportAsync(
      AnalysisConfiguration configuration,
      CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var settings = configuration.ProjectExport
          ?? throw new InvalidOperationException(
            "ProjectJson export was requested but artifacts.projectJson is not configured.");
        var workspace = configuration.Workspace
          ?? throw new InvalidOperationException(
            "artifacts.projectJson.enabled requires a project or solution input; " +
            "a single source file has no project-level export.");
        var options = new ProjectExportOptions(
          workspace.ProjectPath ?? workspace.Path,
          settings.OutputPath,
          workspace.TargetFramework,
          workspace.Configuration,
          workspace.Platform,
          workspace.RestoreMode,
          workspace.GeneratedSourceMode == WorkspaceGeneratedSourceMode.Include,
          settings.ProjectWorkerCount,
          settings.ResumeExistingOutput);
        return await new ProjectJsonExporter()
          .ExportAsync(options, cancellationToken)
          .ConfigureAwait(false);
    }
}
