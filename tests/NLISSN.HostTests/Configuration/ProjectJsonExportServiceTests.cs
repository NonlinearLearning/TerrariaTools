using NLISSN.Hosting;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Infrastructure.Workspace;
using Xunit;

namespace RoslynPrototype.Tests;

/// <summary>
/// <c>artifacts.projectJson</c> 开关的第二道防线（配置层的 fail fast 见
/// <see cref="YamlConfigurationLoaderTests"/>）。
/// 项目级导出需要 <see cref="AnalysisConfiguration.Workspace"/>，而导出发生在分析**之后**；
/// 服务必须自己再判一次，否则绕过配置层时会抛出无诊断价值的异常。
/// </summary>
public sealed class ProjectJsonExportServiceTests
{
  [Fact]
  public async Task ExportAsync_WithoutProjectExportSettings_RejectsBeforeTouchingWorkspace()
  {
    var configuration = CreateConfiguration(projectExport: null, workspace: CreateWorkspace());

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
      () => new ProjectJsonExportService().ExportAsync(configuration));

    Assert.Contains("artifacts.projectJson", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_WithoutWorkspaceInput_NamesTheProjectRequirement()
  {
    // 缺少项目级输入时必须给出可诊断原因，而不是 NullReferenceException 或半个 manifest。
    var configuration = CreateConfiguration(
      new ProjectExportSettings(true, Path.Combine(Path.GetTempPath(), "unused-out"), 4),
      workspace: null);

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
      () => new ProjectJsonExportService().ExportAsync(configuration));

    Assert.Contains("project or solution input", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_WithSettingsAndWorkspace_PassesBothGuards()
  {
    // 正向：配置齐备时不再被那两条前置校验拦下，而是真正进入导出。
    // 这里指向不存在的项目，导出必然失败，但失败层级已经不同（不再是"缺少配置/工作区"）。
    var configuration = CreateConfiguration(
      new ProjectExportSettings(
        true,
        Path.Combine(Path.GetTempPath(), "projectjson-service-out"),
        4),
      workspace: CreateWorkspace());

    var exception = await Record.ExceptionAsync(
      () => new ProjectJsonExportService().ExportAsync(configuration));

    if (exception is not null)
    {
      Assert.DoesNotContain("is not configured", exception.Message, StringComparison.Ordinal);
      Assert.DoesNotContain("requires a project or solution input", exception.Message, StringComparison.Ordinal);
    }
  }

  [Fact]
  public async Task ExportAsync_UnknownRequestedCapability_RejectsWithTheValidNames()
  {
    // 非法能力名必须硬报错，而不是静默忽略——静默会让用户以为能力已开启，
    // 而导出的图里根本没有对应边（与 nlcpg.view.edgeKinds 的既有先例一致）。
    var configuration = CreateConfiguration(
      new ProjectExportSettings(
        true,
        Path.Combine(Path.GetTempPath(), "projectjson-service-out"),
        4,
        RequestedCapabilities: new[] { "NoSuchCapability" }),
      workspace: CreateWorkspace());

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
      () => new ProjectJsonExportService().ExportAsync(configuration));

    Assert.Contains("NoSuchCapability", exception.Message, StringComparison.Ordinal);
    Assert.Contains("InterproceduralDataFlow", exception.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportAsync_KnownRequestedCapability_PassesNameResolution()
  {
    // 正向对照：合法名不得被上一条的守卫误伤。
    // 这里指向不存在的项目，导出必然在更后的层级失败，但已不再是"未知能力名"。
    var configuration = CreateConfiguration(
      new ProjectExportSettings(
        true,
        Path.Combine(Path.GetTempPath(), "projectjson-service-out"),
        4,
        RequestedCapabilities: new[] { "InterproceduralDataFlow" }),
      workspace: CreateWorkspace());

    var exception = await Record.ExceptionAsync(
      () => new ProjectJsonExportService().ExportAsync(configuration));

    if (exception is not null)
    {
      Assert.DoesNotContain("unknown capability", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
  }

  private static WorkspaceInputOptions CreateWorkspace()
  {
    return new WorkspaceInputOptions(
      Path.Combine(Path.GetTempPath(), "projectjson-service-missing.csproj"),
      TargetFramework: "net10.0");
  }

  private static AnalysisConfiguration CreateConfiguration(
    ProjectExportSettings? projectExport,
    WorkspaceInputOptions? workspace)
  {
    var root = Path.Combine(Path.GetTempPath(), "projectjson-service-artifacts");
    return new AnalysisConfiguration(
      "sample.cs",
      new RulePolicySettings(
        null,
        null,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        false,
        false,
        false,
        false,
        false),
      new ExecutionSettings(false, true, 1, 1, 1, 1, 1, 1, true, false, true, false, false),
      new ArtifactSettings(
        root,
        Path.Combine(root, "Diff"),
        Path.Combine(root, "RuntimeLog", "runtime.log"),
        Path.Combine(root, "Evidence", "evidence.json"),
        Path.Combine(root, "RewritePlan"),
        ReplayPlanPath: null,
        Path.Combine(root, "resolved-configuration.json"),
        WriteDiff: false,
        WriteRuntimeLog: false,
        WriteEvidence: false,
        RewritePlanMode.None,
        DiffView: "legacy",
        RunId: "projectjson-service",
        WritePerformanceSummary: false,
        PerformanceMode: "normal",
        PerformanceSummaryPath: Path.Combine(root, "Performance", "summary.json")),
      new LoggingSettings("normal", "debug", Array.Empty<string>(), Array.Empty<string>(), "normal"),
      new ConfigurationProvenance(
        3,
        3,
        Array.Empty<string>(),
        new Dictionary<string, string>(StringComparer.Ordinal)),
      workspace,
      projectExport);
  }
}
