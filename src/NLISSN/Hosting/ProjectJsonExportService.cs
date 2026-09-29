using NLCPG.Contracts;
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
        return await ExportAsync(configuration, reusedSnapshot: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 导出项目级 JSON；<paramref name="reusedSnapshot"/> 非空时复用它而**不再**加载一次工作区。
    /// </summary>
    /// <remarks>
    /// 复用的等价性前提：该快照必须由与分析相同的加载选项产生，且分析已用同一份快照跑完。
    /// 导出按 <c>ProjectExportOptions.IncludeGenerated</c> 选生成源、不启用 generators；
    /// 当分析侧的 <c>generatedSources</c>/<c>generators</c> 与之等价时结论一致。
    /// 不满足时调用方应传 <c>null</c> 走自加载，而不是冒险复用。
    /// </remarks>
    internal async Task<ProjectExportResult> ExportAsync(
      AnalysisConfiguration configuration,
      WorkspaceSolutionSnapshot? reusedSnapshot,
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
          settings.DocumentShardCount,
          settings.DocumentShardParallelism,
          PerformanceDiagnostics: settings.PerformanceDiagnostics,
          RequestedCapabilities: ResolveRequestedCapabilities(settings));
        return await new ProjectJsonExporter()
          .ExportAsync(options, reusedSnapshot, cancellationToken)
          .ConfigureAwait(false);
    }

    /// <summary>
    /// 把配置里的能力位**名称**解析为 <see cref="NLCPGCapability"/>。
    /// </summary>
    /// <remarks>
    /// 配置层（NLISSN.Configuration）的 ProjectReference 被
    /// <c>LayoutArchitectureTests.ProductionProjectReferences_MatchTheTargetDependencyGraph</c>
    /// 冻结，不得引用 NLCPG，故名字→枚举的解析只能落在本层。
    /// <para>
    /// 非法名**硬报错**：静默忽略会让用户以为能力已开启，而导出的图里没有对应边——
    /// 与 <c>nlcpg.view.edgeKinds</c> 的既有先例一致。
    /// </para>
    /// <para>
    /// 空集合返回 <c>null</c>（而非空数组）：<c>null</c> 才会让 builder 走
    /// <c>NLCPGCapability.Default</c>，空数组会被 <c>Aggregate</c> 折叠成 <c>None</c>
    /// 从而产出几乎无边的图。
    /// </para>
    /// </remarks>
    private static IReadOnlyCollection<NLCPGCapability>? ResolveRequestedCapabilities(
      ProjectExportSettings settings)
    {
        var names = settings.EffectiveRequestedCapabilities;
        if (names.Count == 0)
        {
            return null;
        }

        var capabilities = new List<NLCPGCapability>(names.Count);
        foreach (var name in names)
        {
            if (!Enum.TryParse<NLCPGCapability>(name, ignoreCase: true, out var capability))
            {
                throw new InvalidOperationException(
                  $"artifacts.projectJson.requestedCapabilities contains an unknown capability: '{name}'. " +
                  $"Valid values: {string.Join(", ", Enum.GetNames<NLCPGCapability>())}.");
            }

            capabilities.Add(capability);
        }

        return capabilities;
    }
}
