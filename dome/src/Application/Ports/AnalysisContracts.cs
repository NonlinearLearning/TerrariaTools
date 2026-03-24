using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using CorePlanning = TerrariaTools.Dome.Core.Planning;
using CoreRules = TerrariaTools.Dome.Core.Rules.Model;

namespace TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 定义应用层触发分析引擎的入口。
/// </summary>
public interface IAnalysisEngine
{
    /// <summary>
    /// 使用完整分析输入执行分析。
    /// </summary>
    /// <param name="input">分析输入。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>分析输出。</returns>
    Task<CoreAnalysis.AnalysisOutput> AnalyzeAsync(CoreAnalysis.AnalysisInput input, CancellationToken cancellationToken);

    /// <summary>
    /// 使用仅包含源码集合的输入执行分析。
    /// </summary>
    /// <param name="sourceSet">待分析的源码文档集合。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>分析输出。</returns>
    Task<CoreAnalysis.AnalysisOutput> AnalyzeAsync(CoreAnalysis.SourceDocumentSet sourceSet, CancellationToken cancellationToken) =>
        AnalyzeAsync(
            new CoreAnalysis.AnalysisInput(
                sourceSet,
                CoreAnalysis.AnalysisInputMode.SourceOnly,
                new CoreAnalysis.AnalysisEnvironmentInfo("SourceOnly")),
            cancellationToken);
}

/// <summary>
/// 描述种子闭包分析的可选行为。
/// </summary>
/// <param name="ExpandIncludedDocumentsByImportedNamespaces">是否根据导入命名空间扩展纳入文档集合。</param>
public sealed record SeedClosureAnalysisOptions(
    bool ExpandIncludedDocumentsByImportedNamespaces)
{
    /// <summary>
    /// 获取默认的种子闭包分析选项。
    /// </summary>
    public static SeedClosureAnalysisOptions Default { get; } = new(true);
}

/// <summary>
/// 封装种子成员闭包分析的结果。
/// </summary>
/// <param name="SeedNode">命中的种子函数节点。</param>
/// <param name="IncludedDocuments">闭包内包含的文档路径。</param>
/// <param name="ReachableMethods">从种子可达的方法标识集合。</param>
/// <param name="MemberIdsByDocument">按文档归类的方法标识集合。</param>
/// <param name="SymbolClosureDocumentCount">符号闭包覆盖的文档数量。</param>
public sealed record SeedClosureAnalysisResult(
    CoreAnalysis.FunctionNodeRef SeedNode,
    IReadOnlyList<string> IncludedDocuments,
    IReadOnlyList<CoreCommon.MemberId> ReachableMethods,
    IReadOnlyDictionary<string, IReadOnlySet<string>> MemberIdsByDocument,
    int SymbolClosureDocumentCount);

/// <summary>
/// 定义按种子成员名构建闭包分析结果的能力。
/// </summary>
public interface ISeedClosureAnalyzer
{
    /// <summary>
    /// 基于分析输出计算指定成员的闭包。
    /// </summary>
    /// <param name="analysis">上游分析输出。</param>
    /// <param name="seedMemberName">种子成员名。</param>
    /// <param name="options">闭包分析选项。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>闭包分析结果。</returns>
    SeedClosureAnalysisResult Analyze(
        CoreAnalysis.AnalysisOutput analysis,
        string seedMemberName,
        SeedClosureAnalysisOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// 定义函数影响范围分析能力。
/// </summary>
public interface IFunctionImpactAnalyzer
{
    /// <summary>
    /// 根据审计计划和分析输出计算函数影响集。
    /// </summary>
    /// <param name="plan">已编译的审计计划。</param>
    /// <param name="analysis">分析输出。</param>
    /// <returns>函数影响分析结果。</returns>
    CorePlanning.FunctionImpactSet Analyze(CorePlanning.AuditPlan plan, CoreAnalysis.AnalysisOutput analysis);
}

/// <summary>
/// 定义引用归零预测能力。
/// </summary>
public interface IReferenceZeroPredictionAnalyzer
{
    /// <summary>
    /// 基于当前决策集合推导额外的预测性决策。
    /// </summary>
    /// <param name="context">分析上下文。</param>
    /// <param name="decisions">现有决策集合。</param>
    /// <returns>预测得到的附加决策列表。</returns>
    IReadOnlyList<CoreRules.MarkDecision> Predict(
        CoreAnalysis.AnalysisContext context,
        IReadOnlyList<CoreRules.MarkDecision> decisions);
}
