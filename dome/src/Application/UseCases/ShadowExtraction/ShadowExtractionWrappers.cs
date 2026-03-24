namespace TerrariaTools.Dome.Application.UseCases.ShadowExtraction;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Runtime.Process;
using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCpg = TerrariaTools.Dome.Core.Cpg;

/// <summary>
/// 协调影子提取输入解析逻辑。
/// </summary>
internal sealed class ShadowExtractionInputResolver(
    ApplicationAbstractions.IWorkspaceLoader workspaceLoader,
    TerrariaRuntimeShadowLayoutFactory layoutFactory) : IShadowExtractionInputResolver
{
    /// <summary>
    /// 解析影子提取请求并加载工作区输入。
    /// </summary>
    /// <param name="request">影子提取请求。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>输入解析结果。</returns>
    public async Task<StageResult<ShadowExtractionInputResolution>> ResolveAsync(
        ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        var layout = layoutFactory.Create(request);
        progressReporter.Report($"[tr-shadow] Loading solution: {request.SolutionPath}");
        var loadResult = await workspaceLoader.LoadAsync(request.SolutionPath, ApplicationAbstractions.WorkspaceLoadOptions.Default, cancellationToken);
        if (!loadResult.IsSuccess || loadResult.Documents.Count == 0)
        {
            var message = loadResult.Diagnostics.FirstOrDefault()?.Message ?? "No C# input files were found for shadow extraction.";
            return StageResult<ShadowExtractionInputResolution>.Failure(ModelPrimitives.FailureCode.WorkspaceLoadFailed, message);
        }

        return StageResult<ShadowExtractionInputResolution>.Success(new ShadowExtractionInputResolution(request, layout, loadResult));
    }
}

/// <summary>
/// 协调影子提取分析逻辑。
/// </summary>
internal sealed class ShadowExtractionAnalysisStage(
    ApplicationAbstractions.IAnalysisEngine analysisEngine) : IShadowExtractionAnalysisStage
{
    /// <summary>
    /// 执行影子提取分析阶段。
    /// </summary>
    /// <param name="input">输入解析结果。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>分析阶段结果。</returns>
    public async Task<StageResult<ShadowExtractionAnalysis>> AnalyzeAsync(
        ShadowExtractionInputResolution input,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        try
        {
            progressReporter.Report("[tr-shadow] Starting shadow analysis...");
            var analysis = await ShadowExtractionSupport.AnalyzeAsync(analysisEngine, input, cancellationToken);
            return StageResult<ShadowExtractionAnalysis>.Success(analysis);
        }
        catch (Exception ex)
        {
            return StageResult<ShadowExtractionAnalysis>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, ex.Message);
        }
    }
}

/// <summary>
/// 基于种子闭包分析器构建影子闭包计划。
/// </summary>
internal sealed class ShadowClosurePlanner(ApplicationAbstractions.ISeedClosureAnalyzer seedClosureAnalyzer) : IShadowClosurePlanner
{
    /// <summary>
    /// 构建影子闭包计划。
    /// </summary>
    /// <param name="analysis">分析结果。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>闭包计划结果。</returns>
    public StageResult<ShadowClosurePlan> BuildPlan(
        ShadowExtractionAnalysis analysis,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken)
    {
        try
        {
            progressReporter.Report("[tr-shadow] Building closure plan...");
            var seedResult = seedClosureAnalyzer.Analyze(
                analysis.AnalysisResult,
                analysis.Input.Request.SeedMemberName,
                ApplicationAbstractions.SeedClosureAnalysisOptions.Default,
                cancellationToken);

            return StageResult<ShadowClosurePlan>.Success(new ShadowClosurePlan(
                seedResult.SeedNode,
                seedResult.IncludedDocuments,
                seedResult.ReachableMethods,
                seedResult.MemberIdsByDocument,
                seedResult.SymbolClosureDocumentCount));
        }
        catch (Exception ex)
        {
            return StageResult<ShadowClosurePlan>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, ex.Message);
        }
    }
}

/// <summary>
/// 协调影子工作区写入逻辑。
/// </summary>
internal sealed class ShadowWorkspaceWriter(
    TerrariaRuntimeShadowProjectBuilder shadowProjectBuilder,
    TerrariaRuntimeShadowSourceRewriter shadowSourceRewriter) : IShadowWorkspaceWriter
{
    /// <summary>
    /// 将闭包计划写入影子工作区。
    /// </summary>
    /// <param name="input">输入解析结果。</param>
    /// <param name="analysis">分析结果。</param>
    /// <param name="closurePlan">闭包计划。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>工作区写入结果。</returns>
    public Task<StageResult<ShadowWorkspaceWriteResult>> WriteAsync(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken) =>
        ShadowExtractionSupport.WriteWorkspaceAsync(
            shadowProjectBuilder,
            shadowSourceRewriter,
            input,
            analysis,
            closurePlan,
            progressReporter,
            cancellationToken);
}

/// <summary>
/// 根据影子提取各阶段结果生成最终报告。
/// </summary>
internal sealed class ShadowExtractionReportBuilder : IShadowExtractionReportBuilder
{
    /// <summary>
    /// 构建影子提取报告。
    /// </summary>
    /// <param name="input">输入解析结果。</param>
    /// <param name="analysis">分析结果。</param>
    /// <param name="closurePlan">闭包计划。</param>
    /// <param name="workspaceWriteResult">工作区写入结果。</param>
    /// <returns>影子提取报告。</returns>
    public ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport Build(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ShadowWorkspaceWriteResult workspaceWriteResult)
    {
        return new ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport(
            input.Request.SeedMemberName,
            closurePlan.SeedNode.MemberId.Value,
            closurePlan.IncludedDocuments,
            closurePlan.ReachableMethods.Select(static memberId => memberId.Value).ToArray(),
            BuildAdvancedAnalysisSummary(analysis.AnalysisResult),
            workspaceWriteResult.RewrittenDocuments.Count,
            workspaceWriteResult.RewriteSummary);
    }

    private static CoreAnalysis.AdvancedAnalysisSummary BuildAdvancedAnalysisSummary(CoreAnalysis.AnalysisOutput analysisOutput)
    {
        var summary = analysisOutput.Services.AdvancedAnalysis.BuildSummary();
        var notes = (summary.Notes ?? Array.Empty<string>())
            .Concat(BuildCpgFingerprintNotes(analysisOutput.CodePropertyGraph))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return summary with { Notes = notes };
    }

    private static IReadOnlyList<string> BuildCpgFingerprintNotes(CoreCpg.DomeCpg codePropertyGraph)
    {
        var callEdgeCount = codePropertyGraph.Edges.Count(edge => edge.Label == CoreCpg.EdgeKinds.Call);
        var overlays = codePropertyGraph.MetaData.Overlays.Count == 0
            ? "none"
            : string.Join("|", codePropertyGraph.MetaData.Overlays.OrderBy(static overlay => overlay, StringComparer.Ordinal));

        return
        [
            $"CpgCallEdges={callEdgeCount}",
            $"CpgOverlays={overlays}"
        ];
    }
}
