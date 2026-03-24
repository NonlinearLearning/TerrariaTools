using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using CorePlanning = TerrariaTools.Dome.Core.Planning;

namespace TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 描述单次重写执行所需的输入。
/// </summary>
/// <param name="SourceSet">待重写的源码集合。</param>
/// <param name="Plan">应用到源码集合上的审计计划。</param>
public sealed record RewriteInput(
    CoreAnalysis.SourceDocumentSet SourceSet,
    CorePlanning.AuditPlan Plan);

/// <summary>
/// 描述单个重写后文档的输出内容。
/// </summary>
/// <param name="RelativePath">文档相对路径。</param>
/// <param name="SourceText">重写后的源码文本。</param>
public sealed record RewrittenDocument(
    string RelativePath,
    string SourceText);

/// <summary>
/// 封装一次重写执行的结果。
/// </summary>
/// <param name="IsSuccess">指示重写是否成功。</param>
/// <param name="FailureCode">失败时对应的失败代码。</param>
/// <param name="Documents">成功时产出的文档集合。</param>
/// <param name="Diagnostics">重写过程中产生的诊断信息。</param>
/// <param name="Message">可供展示的失败消息。</param>
public sealed record RewriteOutput(
    bool IsSuccess,
    FailureCode FailureCode,
    IReadOnlyList<RewrittenDocument> Documents,
    IReadOnlyList<string> Diagnostics,
    string? Message)
{
    /// <summary>
    /// 在仅生成单个文档时返回其源码文本。
    /// </summary>
    public string? RewrittenSource => Documents.Count == 1 ? Documents[0].SourceText : null;

    /// <summary>
    /// 创建一个成功的重写结果。
    /// </summary>
    /// <param name="documents">生成的文档集合。</param>
    /// <param name="diagnostics">可选的诊断信息。</param>
    /// <returns>表示成功的重写结果。</returns>
    public static RewriteOutput Success(IReadOnlyList<RewrittenDocument> documents, IReadOnlyList<string>? diagnostics = null) =>
        new(true, FailureCode.None, documents, diagnostics ?? Array.Empty<string>(), null);

    /// <summary>
    /// 创建一个失败的重写结果。
    /// </summary>
    /// <param name="failureCode">失败代码。</param>
    /// <param name="message">失败消息。</param>
    /// <param name="diagnostics">可选的诊断信息。</param>
    /// <returns>表示失败的重写结果。</returns>
    public static RewriteOutput Failure(FailureCode failureCode, string? message, IReadOnlyList<string>? diagnostics = null) =>
        new(false, failureCode, Array.Empty<RewrittenDocument>(), diagnostics ?? Array.Empty<string>(), message);
}

/// <summary>
/// 描述应用层运行的最终结果。
/// </summary>
/// <param name="IsSuccess">指示运行是否成功。</param>
/// <param name="FailureCode">失败时对应的失败代码。</param>
/// <param name="OutputPath">输出目录路径。</param>
/// <param name="ReportPath">生成的报告路径。</param>
/// <param name="Message">可供展示的失败消息。</param>
public sealed record RunResult(
    bool IsSuccess,
    FailureCode FailureCode,
    string OutputPath,
    string? ReportPath,
    string? Message)
{
    /// <summary>
    /// 创建一个成功的运行结果。
    /// </summary>
    /// <param name="outputPath">输出目录路径。</param>
    /// <param name="reportPath">报告路径。</param>
    /// <returns>表示成功的运行结果。</returns>
    public static RunResult Success(string outputPath, string? reportPath) => new(true, FailureCode.None, outputPath, reportPath, null);

    /// <summary>
    /// 创建一个失败的运行结果。
    /// </summary>
    /// <param name="code">失败代码。</param>
    /// <param name="outputPath">输出目录路径。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>表示失败的运行结果。</returns>
    public static RunResult Failure(FailureCode code, string outputPath, string? message) => new(false, code, outputPath, null, message);
}

/// <summary>
/// 概述一次失败的核心原因。
/// </summary>
/// <param name="FailureCode">失败代码。</param>
/// <param name="Message">失败描述。</param>
public sealed record FailureSummary(FailureCode FailureCode, string Message);

/// <summary>
/// 描述计划编译或执行期间发现的冲突。
/// </summary>
/// <param name="ConflictCode">冲突代码。</param>
/// <param name="TargetKey">冲突目标键。</param>
/// <param name="TargetDisplayText">目标展示文本。</param>
/// <param name="ActionKinds">参与冲突的动作集合。</param>
/// <param name="Reason">冲突原因。</param>
public sealed record ConflictSummary(string ConflictCode, string TargetKey, string TargetDisplayText, IReadOnlyList<CoreCommon.PlanActionKind> ActionKinds, string Reason);

/// <summary>
/// 描述高风险目标被跳过的统计信息。
/// </summary>
/// <param name="SkippedHighRiskTargetCount">被跳过的高风险目标数量。</param>
/// <param name="SampleTargetDisplayTexts">示例目标文本。</param>
public sealed record RiskSummary(int SkippedHighRiskTargetCount, IReadOnlyList<string> SampleTargetDisplayTexts);

/// <summary>
/// 描述计划对方法和语句的覆盖情况。
/// </summary>
/// <param name="CoveredMethodCount">被类删除动作间接覆盖的方法数量。</param>
/// <param name="CoveredStatementCount">被类删除动作间接覆盖的语句数量。</param>
/// <param name="SampleCoveredTargetDisplayTexts">示例覆盖目标文本。</param>
public sealed record PlanCoverageSummary(int CoveredMethodCount, int CoveredStatementCount, IReadOnlyList<string> SampleCoveredTargetDisplayTexts);

/// <summary>
/// 描述函数影响分析的摘要信息。
/// </summary>
/// <param name="DeletedFunctionCount">删除的函数数量。</param>
/// <param name="AffectedFunctionCount">受影响的函数数量。</param>
/// <param name="AffectedDocumentCount">受影响的文档数量。</param>
/// <param name="ExpansionDepth">调用扩展深度。</param>
/// <param name="EdgeKinds">参与扩展的边类型。</param>
/// <param name="SampleAffectedFunctionIds">示例受影响函数标识。</param>
/// <param name="SampleAffectedDocumentPaths">示例受影响文档路径。</param>
public sealed record FunctionImpactSummary(
    int DeletedFunctionCount,
    int AffectedFunctionCount,
    int AffectedDocumentCount,
    int ExpansionDepth,
    IReadOnlyList<CoreCommon.FunctionDependencyKind> EdgeKinds,
    IReadOnlyList<string> SampleAffectedFunctionIds,
    IReadOnlyList<string> SampleAffectedDocumentPaths);

/// <summary>
/// 描述引用归零预测的结果摘要。
/// </summary>
/// <param name="PredictedMethodDeleteCount">预测可删除的方法数量。</param>
/// <param name="SamplePredictedMethodIds">示例预测方法标识。</param>
public sealed record ReferenceZeroPredictionSummary(int PredictedMethodDeleteCount, IReadOnlyList<string> SamplePredictedMethodIds);

/// <summary>
/// 描述边界提升产生的方法删除摘要。
/// </summary>
/// <param name="BoundaryKind">触发提升的边界类型。</param>
/// <param name="PromotedMethodDeleteCount">被提升的方法删除数量。</param>
/// <param name="SamplePromotedMethodIds">示例提升方法标识。</param>
public sealed record BoundaryPromotionSummary(CoreCommon.BoundaryKind BoundaryKind, int PromotedMethodDeleteCount, IReadOnlyList<string> SamplePromotedMethodIds);

/// <summary>
/// 表示写入运行报告的工作区诊断项。
/// </summary>
/// <param name="Stage">产生诊断的阶段。</param>
/// <param name="Severity">诊断严重级别。</param>
/// <param name="Message">诊断消息。</param>
public sealed record WorkspaceDiagnosticInfo(
    string Stage,
    WorkspaceLoadDiagnosticSeverity Severity,
    string Message);

/// <summary>
/// 表示 Dome 应用运行后的汇总报告。
/// </summary>
/// <param name="IsSuccess">指示运行是否成功。</param>
/// <param name="FailureCode">失败时对应的失败代码。</param>
/// <param name="AnalysisTargets">分析目标数量。</param>
/// <param name="PlannedChanges">计划变更数量。</param>
/// <param name="Conflicts">冲突数量。</param>
/// <param name="RewrittenDocuments">重写文档数量。</param>
/// <param name="GeneratedArtifacts">生成的产物路径集合。</param>
/// <param name="FailureSummary">失败摘要。</param>
/// <param name="ConflictSummaries">冲突摘要集合。</param>
/// <param name="RiskSummary">风险摘要。</param>
/// <param name="PlanCoverageSummary">覆盖率摘要。</param>
/// <param name="FunctionImpactSummary">函数影响摘要。</param>
/// <param name="BoundaryPromotionSummary">边界提升摘要。</param>
/// <param name="ReferenceZeroPredictionSummary">引用归零预测摘要。</param>
/// <param name="WorkspaceLoadMode">工作区加载模式。</param>
/// <param name="WorkspaceFallbackUsed">指示是否使用了加载回退。</param>
/// <param name="WorkspaceDiagnostics">工作区诊断集合。</param>
/// <param name="Message">可供展示的附加消息。</param>
public sealed record RunReport(
    bool IsSuccess,
    FailureCode FailureCode,
    int AnalysisTargets,
    int PlannedChanges,
    int Conflicts,
    int RewrittenDocuments,
    IReadOnlyList<string> GeneratedArtifacts,
    FailureSummary? FailureSummary,
    IReadOnlyList<ConflictSummary> ConflictSummaries,
    RiskSummary RiskSummary,
    PlanCoverageSummary PlanCoverageSummary,
    FunctionImpactSummary? FunctionImpactSummary,
    BoundaryPromotionSummary? BoundaryPromotionSummary,
    ReferenceZeroPredictionSummary? ReferenceZeroPredictionSummary,
    WorkspaceLoadMode WorkspaceLoadMode,
    bool WorkspaceFallbackUsed,
    IReadOnlyList<WorkspaceDiagnosticInfo> WorkspaceDiagnostics,
    string? Message)
{
    /// <summary>
    /// 获取高级分析摘要。
    /// </summary>
    public CoreAnalysis.AdvancedAnalysisSummary? AdvancedAnalysisSummary { get; init; }
}
