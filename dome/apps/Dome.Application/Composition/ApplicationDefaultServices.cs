namespace TerrariaTools.Dome.Application.Composition;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Adapters.Analysis.Roslyn;
using TerrariaTools.Dome.Adapters.Reporting.Json;
using TerrariaTools.Dome.Adapters.Rewrite.Roslyn;
using TerrariaTools.Dome.Application.Pipeline;
using TerrariaTools.Dome.Core.Rules.Services;

/// <summary>
/// 提供标准宿主默认使用的服务实例工厂。
/// </summary>
public static class ApplicationDefaultServices
{
    /// <summary>
    /// 创建默认的工作区加载器。
    /// </summary>
    /// <returns>工作区加载器实例。</returns>
    public static ApplicationAbstractions.IWorkspaceLoader CreateWorkspaceLoader() =>
        new WorkspaceLoadCoordinator(
            new CodeAnalysisWorkspaceLoader(),
            new SourceOnlyLoader());

    /// <summary>
    /// 创建默认的分析引擎。
    /// </summary>
    /// <returns>分析引擎实例。</returns>
    public static ApplicationAbstractions.IAnalysisEngine CreateAnalysisEngine() => new RoslynAnalysisEngine();

    /// <summary>
    /// 创建默认的函数影响分析器。
    /// </summary>
    /// <returns>函数影响分析器实例。</returns>
    public static ApplicationAbstractions.IFunctionImpactAnalyzer CreateFunctionImpactAnalyzer() => new FunctionImpactAnalyzer();

    /// <summary>
    /// 创建默认的引用归零预测分析器。
    /// </summary>
    /// <returns>引用归零预测分析器实例。</returns>
    public static ApplicationAbstractions.IReferenceZeroPredictionAnalyzer CreateReferenceZeroPredictionAnalyzer() => new ReferenceZeroPredictionAnalyzer();

    /// <summary>
    /// 创建默认的种子闭包分析器。
    /// </summary>
    /// <returns>种子闭包分析器实例。</returns>
    public static ApplicationAbstractions.ISeedClosureAnalyzer CreateSeedClosureAnalyzer() => new SeedClosureAnalyzer();

    /// <summary>
    /// 创建默认的重写执行器。
    /// </summary>
    /// <returns>重写执行器实例。</returns>
    public static ApplicationAbstractions.IRewriteExecutor CreateRewriteExecutor() => new RoslynRewriteExecutor();

    /// <summary>
    /// 创建默认的产物写入器。
    /// </summary>
    /// <returns>产物写入器实例。</returns>
    public static ApplicationAbstractions.IArtifactWriter CreateArtifactWriter() => new JsonArtifactWriter();

    /// <summary>
    /// 创建 JSON 产物写入器。
    /// </summary>
    /// <returns>JSON 产物写入器实例。</returns>
    public static JsonArtifactWriter CreateJsonArtifactWriter() => new();

    /// <summary>
    /// 创建默认的标记规则引擎。
    /// </summary>
    /// <returns>标记规则引擎实例。</returns>
    public static MarkingRuleEngine CreateMarkingRuleEngine() => new(MarkingRuleRegistry.CreateDefault());

    /// <summary>
    /// 创建运行报告构建器。
    /// </summary>
    /// <returns>运行报告构建器实例。</returns>
    public static RunReportBuilder CreateRunReportBuilder() => new();

    /// <summary>
    /// 创建产物计划构建器。
    /// </summary>
    /// <returns>产物计划构建器实例。</returns>
    public static ArtifactPlanBuilder CreateArtifactPlanBuilder() => new();
}
