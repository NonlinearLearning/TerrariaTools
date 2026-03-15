namespace TerrariaTools.Dome.Application;

using System.Text.Json;
using System.Text.Json.Serialization;
using TerrariaTools.Dome.Core;
using TerrariaTools.Dome.Reporting;

/// <summary>
/// 阶段执行结果。
/// </summary>
/// <typeparam name="T">阶段返回值类型。</typeparam>
public sealed record StageResult<T>(
    bool IsSuccess,
    T? Value,
    FailureCode FailureCode,
    string? Message)
{
    /// <summary>
    /// 创建成功结果。
    /// </summary>
    /// <param name="value">成功返回值。</param>
    /// <returns>成功的阶段结果。</returns>
    public static StageResult<T> Success(T value) => new(true, value, FailureCode.None, null);

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    /// <param name="failureCode">失败代码。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>失败的阶段结果。</returns>
    public static StageResult<T> Failure(FailureCode failureCode, string message) => new(false, default, failureCode, message);
}

/// <summary>
/// Terraria 运行时布局工厂。
/// </summary>
public interface ITerrariaRuntimeLayoutFactory
{
    /// <summary>
    /// 根据请求创建运行时布局。
    /// </summary>
    /// <param name="request">运行请求。</param>
    /// <returns>运行时布局。</returns>
    TerrariaRuntimeLayout Create(TerrariaRuntimeRunRequest request);
}

/// <summary>
/// Terraria 运行时布局工厂实现。
/// </summary>
public sealed class TerrariaRuntimeLayoutFactory : ITerrariaRuntimeLayoutFactory
{
    /// <summary>
    /// 根据请求创建运行时布局。
    /// </summary>
    /// <param name="request">运行请求。</param>
    /// <returns>运行时布局。</returns>
    public TerrariaRuntimeLayout Create(TerrariaRuntimeRunRequest request) => TerrariaRuntimeLayout.Create(request);
}

/// <summary>
/// Terraria 运行时工作区预处理器。
/// </summary>
public interface ITerrariaRuntimeWorkspacePreparer
{
    /// <summary>
    /// 确保输出目录存在。
    /// </summary>
    /// <param name="layout">运行时布局。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task EnsureOutputDirectoriesAsync(TerrariaRuntimeLayout layout, CancellationToken cancellationToken);

    /// <summary>
    /// 刷新依赖环境。
    /// </summary>
    /// <param name="layout">运行时布局。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RefreshDependencyEnvironmentAsync(TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken);

    /// <summary>
    /// 准备工作区。
    /// </summary>
    /// <param name="layout">运行时布局。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PrepareWorkspaceAsync(TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken);
}

/// <summary>
/// 运行报告存储接口。
/// </summary>
public interface IRunReportStore
{
    /// <summary>
    /// 加载运行报告。
    /// </summary>
    /// <param name="path">报告路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>加载结果。</returns>
    Task<StageResult<RunReport>> LoadAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// 保存运行报告。
    /// </summary>
    /// <param name="path">报告路径。</param>
    /// <param name="report">运行报告。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveAsync(string path, RunReport report, CancellationToken cancellationToken);
}

/// <summary>
/// 基于 JSON 的运行报告存储实现。
/// </summary>
public sealed class JsonRunReportStore(JsonArtifactWriter artifactWriter) : IRunReportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 从指定路径加载运行报告。
    /// </summary>
    /// <param name="path">报告路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>加载结果。</returns>
    public async Task<StageResult<RunReport>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return StageResult<RunReport>.Failure(FailureCode.AnalysisFailed, $"Run report '{path}' was not found.");
        }

        try
        {
            var reportJson = await File.ReadAllTextAsync(path, cancellationToken);
            var report = JsonSerializer.Deserialize<RunReport>(reportJson, JsonOptions);
            return report == null
                ? StageResult<RunReport>.Failure(FailureCode.AnalysisFailed, "report.json could not be deserialized.")
                : StageResult<RunReport>.Success(report);
        }
        catch (Exception ex)
        {
            return StageResult<RunReport>.Failure(FailureCode.AnalysisFailed, ex.Message);
        }
    }

    /// <summary>
    /// 将运行报告保存到指定路径。
    /// </summary>
    /// <param name="path">报告路径。</param>
    /// <param name="report">运行报告。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task SaveAsync(string path, RunReport report, CancellationToken cancellationToken) =>
        artifactWriter.WriteReportAsync(path, report, cancellationToken);
}

/// <summary>
/// 影子提取输入解析器。
/// </summary>
public interface IShadowExtractionInputResolver
{
    /// <summary>
    /// 解析影子提取输入。
    /// </summary>
    /// <param name="request">影子提取请求。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>输入解析结果。</returns>
    Task<StageResult<ShadowExtractionInputResolution>> ResolveAsync(
        TerrariaRuntimeShadowExtractionRequest request,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 影子提取分析阶段。
/// </summary>
public interface IShadowExtractionAnalysisStage
{
    /// <summary>
    /// 执行影子提取分析。
    /// </summary>
    /// <param name="input">输入解析结果。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分析结果。</returns>
    Task<StageResult<ShadowExtractionAnalysis>> AnalyzeAsync(
        ShadowExtractionInputResolution input,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 影子闭包规划器。
/// </summary>
public interface IShadowClosurePlanner
{
    /// <summary>
    /// 构建影子闭包计划。
    /// </summary>
    /// <param name="analysis">影子提取分析结果。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>闭包计划结果。</returns>
    StageResult<ShadowClosurePlan> BuildPlan(
        ShadowExtractionAnalysis analysis,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 影子工作区写入器。
/// </summary>
public interface IShadowWorkspaceWriter
{
    /// <summary>
    /// 写入影子工作区内容。
    /// </summary>
    /// <param name="input">输入解析结果。</param>
    /// <param name="analysis">影子提取分析结果。</param>
    /// <param name="closurePlan">影子闭包计划。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入结果。</returns>
    Task<StageResult<ShadowWorkspaceWriteResult>> WriteAsync(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 影子提取报告构建器。
/// </summary>
public interface IShadowExtractionReportBuilder
{
    /// <summary>
    /// 构建影子提取报告。
    /// </summary>
    /// <param name="input">输入解析结果。</param>
    /// <param name="analysis">影子提取分析结果。</param>
    /// <param name="closurePlan">影子闭包计划。</param>
    /// <param name="workspaceWriteResult">工作区写入结果。</param>
    /// <returns>影子提取报告。</returns>
    TerrariaRuntimeShadowExtractionReport Build(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ShadowWorkspaceWriteResult workspaceWriteResult);
}

/// <summary>
/// 影子提取报告存储接口。
/// </summary>
public interface IShadowExtractionReportStore
{
    /// <summary>
    /// 保存影子提取报告。
    /// </summary>
    /// <param name="path">报告路径。</param>
    /// <param name="report">影子提取报告。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveAsync(string path, TerrariaRuntimeShadowExtractionReport report, CancellationToken cancellationToken);
}

/// <summary>
/// 基于 JSON 的影子提取报告存储实现。
/// </summary>
public sealed class JsonShadowExtractionReportStore(JsonArtifactWriter artifactWriter) : IShadowExtractionReportStore
{
    /// <summary>
    /// 将影子提取报告保存到指定路径。
    /// </summary>
    /// <param name="path">报告路径。</param>
    /// <param name="report">影子提取报告。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task SaveAsync(string path, TerrariaRuntimeShadowExtractionReport report, CancellationToken cancellationToken) =>
        artifactWriter.WriteJsonAsync(path, report, cancellationToken);
}

/// <summary>
/// 影子提取输入解析结果。
/// </summary>
public sealed record ShadowExtractionInputResolution(
    TerrariaRuntimeShadowExtractionRequest Request,
    TerrariaRuntimeShadowLayout Layout,
    WorkspaceLoadResult LoadResult);

/// <summary>
/// 影子提取分析结果。
/// </summary>
public sealed record ShadowExtractionAnalysis(
    ShadowExtractionInputResolution Input,
    AnalysisEngineResult AnalysisResult,
    AnalysisContext AnalysisContext,
    FunctionNodeRef SeedNode);

/// <summary>
/// 影子闭包计划。
/// </summary>
public sealed record ShadowClosurePlan(
    IReadOnlyList<string> IncludedDocuments,
    IReadOnlyList<MemberId> ReachableMethods,
    IReadOnlyDictionary<string, IReadOnlySet<string>> MemberIdsByDocument,
    int SymbolClosureDocumentCount);

/// <summary>
/// 影子工作区写入结果。
/// </summary>
public sealed record ShadowWorkspaceWriteResult(
    IReadOnlyDictionary<string, string> RewrittenDocuments,
    TerrariaRuntimeShadowRewriteSummary RewriteSummary);
