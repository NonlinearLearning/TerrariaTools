namespace TerrariaTools.Dome.Application;

using System.Text.Json;
using System.Text.Json.Serialization;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Reporting;

/// <summary>
/// Terraria Runtime 运行入口应用。
/// </summary>
public sealed class TerrariaRuntimeApplication(
    DomeApplication domeApplication,
    TerrariaRuntimeEnvironmentBuilder runtimeEnvironmentBuilder,
    ITerrariaRuntimeBuildExecutor buildExecutor,
    JsonArtifactWriter artifactWriter,
    ITerrariaRuntimeProgressReporter progressReporter)
{
    /// <summary>
    /// 执行 TR 运行流程并回写构建结果到报告。
    /// </summary>
    /// <param name="request">TR 运行请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行结果。</returns>
    public async Task<RunResult> RunAsync(TerrariaRuntimeRunRequest request, CancellationToken cancellationToken)
    {
        var layout = TerrariaRuntimeLayout.Create(request);
        Directory.CreateDirectory(layout.OutputRootPath);
        Directory.CreateDirectory(layout.ArtifactsPath);

        await runtimeEnvironmentBuilder.RefreshDependencyEnvironmentAsync(layout, progressReporter, cancellationToken);

        progressReporter.Report("[tr-run] 开始执行 dome 分析、计划和改写...");
        var runResult = await domeApplication.RunAsync(
            new RunRequest(layout.SolutionPath, layout.ArtifactsPath, Array.Empty<string>(), RunMode.Standard),
            cancellationToken);

        var reportPath = Path.Combine(layout.ArtifactsPath, "report.json");
        if (!runResult.IsSuccess || !File.Exists(reportPath))
        {
            return runResult;
        }

        await runtimeEnvironmentBuilder.PrepareWorkspaceAsync(layout, progressReporter, cancellationToken);

        var buildSummary = await buildExecutor.ExecuteAsync(layout, progressReporter, cancellationToken);
        var reportJson = await File.ReadAllTextAsync(reportPath, cancellationToken);
        var report = JsonSerializer.Deserialize<RunReport>(
            reportJson,
            new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            })
            ?? throw new InvalidOperationException("report.json could not be deserialized.");
        report = report with { TrBuildSummary = buildSummary };
        await artifactWriter.WriteReportAsync(reportPath, report, cancellationToken);

        if (!buildSummary.BuildSucceeded)
        {
            return RunResult.Failure(FailureCode.BuildFailed, layout.OutputRootPath, buildSummary.StandardError);
        }

        progressReporter.Report("[tr-run] TR 专用运行流程已成功完成。");
        return RunResult.Success(layout.OutputRootPath, reportPath);
    }
}
