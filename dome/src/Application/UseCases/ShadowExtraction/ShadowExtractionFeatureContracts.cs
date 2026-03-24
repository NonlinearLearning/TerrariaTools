using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Pipeline;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;

namespace TerrariaTools.Dome.Application.UseCases.ShadowExtraction;

/// <summary>
/// 定义影子提取输入解析能力。
/// </summary>
internal interface IShadowExtractionInputResolver
{
    /// <summary>
    /// 解析影子提取请求并加载输入。
    /// </summary>
    /// <param name="request">影子提取请求。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>输入解析结果。</returns>
    Task<StageResult<ShadowExtractionInputResolution>> ResolveAsync(
        ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 定义影子提取分析阶段能力。
/// </summary>
internal interface IShadowExtractionAnalysisStage
{
    /// <summary>
    /// 基于输入解析结果执行分析。
    /// </summary>
    /// <param name="input">输入解析结果。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>分析阶段结果。</returns>
    Task<StageResult<ShadowExtractionAnalysis>> AnalyzeAsync(
        ShadowExtractionInputResolution input,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 定义影子闭包计划构建能力。
/// </summary>
internal interface IShadowClosurePlanner
{
    /// <summary>
    /// 构建影子闭包计划。
    /// </summary>
    /// <param name="analysis">分析结果。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>闭包计划结果。</returns>
    StageResult<ShadowClosurePlan> BuildPlan(
        ShadowExtractionAnalysis analysis,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 定义影子工作区写入能力。
/// </summary>
internal interface IShadowWorkspaceWriter
{
    /// <summary>
    /// 按闭包计划写入影子工作区。
    /// </summary>
    /// <param name="input">输入解析结果。</param>
    /// <param name="analysis">分析结果。</param>
    /// <param name="closurePlan">闭包计划。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>工作区写入结果。</returns>
    Task<StageResult<ShadowWorkspaceWriteResult>> WriteAsync(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 定义影子提取报告构建能力。
/// </summary>
internal interface IShadowExtractionReportBuilder
{
    /// <summary>
    /// 根据影子提取各阶段结果构建最终报告。
    /// </summary>
    /// <param name="input">输入解析结果。</param>
    /// <param name="analysis">分析结果。</param>
    /// <param name="closurePlan">闭包计划。</param>
    /// <param name="workspaceWriteResult">工作区写入结果。</param>
    /// <returns>影子提取报告。</returns>
    ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport Build(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ShadowWorkspaceWriteResult workspaceWriteResult);
}

/// <summary>
/// 定义影子提取报告存储能力。
/// </summary>
internal interface IShadowExtractionReportStore
{
    /// <summary>
    /// 将影子提取报告保存到指定路径。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="report">待保存的报告。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task SaveAsync(string path, ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport report, CancellationToken cancellationToken);
}

/// <summary>
/// 汇总影子提取输入解析结果。
/// </summary>
/// <param name="Request">原始影子提取请求。</param>
/// <param name="Layout">解析得到的工作区布局。</param>
/// <param name="LoadResult">工作区加载结果。</param>
internal sealed record ShadowExtractionInputResolution(
    ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest Request,
    ApplicationAbstractions.TerrariaRuntimeShadowLayout Layout,
    ApplicationAbstractions.WorkspaceLoadResult LoadResult);

/// <summary>
/// 汇总影子提取分析结果。
/// </summary>
/// <param name="Input">输入解析结果。</param>
/// <param name="AnalysisResult">分析输出。</param>
internal sealed record ShadowExtractionAnalysis(
    ShadowExtractionInputResolution Input,
    CoreAnalysis.AnalysisOutput AnalysisResult);

/// <summary>
/// 描述影子提取使用的闭包计划。
/// </summary>
/// <param name="SeedNode">命中的种子函数节点。</param>
/// <param name="IncludedDocuments">纳入影子工作区的文档路径。</param>
/// <param name="ReachableMethods">可达方法集合。</param>
/// <param name="MemberIdsByDocument">按文档归类的方法标识集合。</param>
/// <param name="SymbolClosureDocumentCount">符号闭包覆盖的文档数量。</param>
internal sealed record ShadowClosurePlan(
    CoreAnalysis.FunctionNodeRef SeedNode,
    IReadOnlyList<string> IncludedDocuments,
    IReadOnlyList<CoreCommon.MemberId> ReachableMethods,
    IReadOnlyDictionary<string, IReadOnlySet<string>> MemberIdsByDocument,
    int SymbolClosureDocumentCount);

/// <summary>
/// 汇总影子工作区写入结果。
/// </summary>
/// <param name="RewrittenDocuments">按路径索引的重写文档内容。</param>
/// <param name="RewriteSummary">成员重写摘要。</param>
internal sealed record ShadowWorkspaceWriteResult(
    IReadOnlyDictionary<string, string> RewrittenDocuments,
    ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary RewriteSummary);
