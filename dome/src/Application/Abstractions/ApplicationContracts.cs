using TerrariaTools.Dome.Model.Analysis;
using TerrariaTools.Dome.Model.Planning;
using TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Model.Rules;

namespace TerrariaTools.Dome.Application.Abstractions;

public enum WorkspaceLoaderPreference
{
    Auto,
    CodeAnalysisFirst,
    SourceOnly
}

public sealed record WorkspaceLoadOptions(
    WorkspaceLoaderPreference PreferredLoader,
    bool AllowFallbackToSourceOnly)
{
    public static WorkspaceLoadOptions Default { get; } = new(WorkspaceLoaderPreference.Auto, true);
}

public sealed record WorkspaceLoadDiagnostic(
    string Stage,
    WorkspaceLoadDiagnosticSeverity Severity,
    string Message);

public sealed record SourceDocument(
    string SourcePath,
    string RelativePath,
    string SourceText);

public sealed record SourceDocumentSet(
    string EntryPath,
    string RootPath,
    IReadOnlyList<SourceDocument> Documents);

public sealed record WorkspaceLoadResult(
    bool IsSuccess,
    SourceDocumentSet? SourceSet,
    WorkspaceLoadMode LoadMode,
    string RequestedPrimaryLoader,
    bool FallbackUsed,
    IReadOnlyList<WorkspaceLoadDiagnostic> Diagnostics)
{
    public IReadOnlyList<SourceDocument> Documents => SourceSet?.Documents ?? Array.Empty<SourceDocument>();

    public static WorkspaceLoadResult Success(
        SourceDocumentSet sourceSet,
        WorkspaceLoadMode loadMode,
        string requestedPrimaryLoader,
        bool fallbackUsed = false,
        IReadOnlyList<WorkspaceLoadDiagnostic>? diagnostics = null) =>
        new(true, sourceSet, loadMode, requestedPrimaryLoader, fallbackUsed, diagnostics ?? Array.Empty<WorkspaceLoadDiagnostic>());

    public static WorkspaceLoadResult Failure(
        WorkspaceLoadMode loadMode,
        string requestedPrimaryLoader,
        IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics) =>
        new(false, null, loadMode, requestedPrimaryLoader, false, diagnostics);
}

public sealed record AnalysisEngineResult(
    AnalysisResultModel View,
    AnalysisExecutionSnapshot Snapshot,
    AnalysisServices Services,
    AnalysisPerformanceSummary PerformanceSummary)
{
    public FunctionIndex FunctionIndex => Snapshot.FunctionIndex;
    public FunctionFactsIndex FunctionFacts => Snapshot.FunctionFacts;
    public AnalysisContext CreateContext() => AnalysisContext.Create(Snapshot, Services);
}

public interface IWorkspaceLoader
{
    Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken);
}

public interface IAnalysisEngine
{
    Task<AnalysisEngineResult> AnalyzeAsync(SourceDocumentSet sourceSet, CancellationToken cancellationToken);
}

public interface IRewriteExecutor
{
    Task<RewriteExecutionResult> ExecuteAsync(SourceDocumentSet sourceSet, AuditPlan plan, CancellationToken cancellationToken);
}

public interface IArtifactWriter
{
    Task WritePlanAsync(string path, AuditPlan plan, CancellationToken cancellationToken);
    Task WriteAnalysisAsync(string path, AnalysisResultModel view, CancellationToken cancellationToken);
    Task WriteReportAsync(string path, RunReport report, CancellationToken cancellationToken);
}

public interface IFunctionImpactAnalyzer
{
    FunctionImpactSet Analyze(AuditPlan plan, AnalysisServices services, FunctionGraphRequest request);
    FunctionImpactSet Analyze(AuditPlan plan, FunctionGraphSnapshot snapshot);
}

public interface IReferenceZeroPredictionAnalyzer
{
    IReadOnlyList<MarkDecision> Predict(AnalysisExecutionSnapshot snapshot, AnalysisServices services, RuleExecutionContext executionContext, IReadOnlyList<MarkDecision> decisions);
    IReadOnlyList<MarkDecision> Predict(AnalysisContext context, IReadOnlyList<MarkDecision> decisions);
}

public sealed record RunRequest(
    string InputPath,
    string OutputPath,
    IReadOnlyList<string> RuleSet,
    RunMode Mode,
    WorkspaceLoadOptions WorkspaceLoadOptions)
{
    public RunRequest(string inputPath, string outputPath, IReadOnlyList<string> ruleSet, RunMode mode)
        : this(inputPath, outputPath, ruleSet, mode, WorkspaceLoadOptions.Default)
    {
    }
}

public sealed record TerrariaRuntimeRunRequest(string SolutionPath, string OutputRootPath);
public sealed record TerrariaRuntimeShadowExtractionRequest(string SolutionPath, string OutputRootPath, string SeedMemberName);

public sealed record TerrariaRuntimeLayout(string SolutionPath, string SourceRootPath, string OutputRootPath, string DependencyEnvironmentPath, string WorkspacePath, string ArtifactsPath, string WorkspaceSolutionPath)
{
    public static TerrariaRuntimeLayout Create(TerrariaRuntimeRunRequest request)
    {
        var sourceRootPath = Path.GetDirectoryName(request.SolutionPath) ?? throw new InvalidOperationException("TR solution path must have a parent directory.");
        var solutionFileName = Path.GetFileName(request.SolutionPath);
        var dependencyEnvironmentPath = Path.Combine(request.OutputRootPath, "dependency-env");
        var workspacePath = Path.Combine(request.OutputRootPath, "workspace");
        var artifactsPath = Path.Combine(request.OutputRootPath, "artifacts");
        var workspaceSolutionPath = Path.Combine(workspacePath, solutionFileName);
        return new(request.SolutionPath, sourceRootPath, request.OutputRootPath, dependencyEnvironmentPath, workspacePath, artifactsPath, workspaceSolutionPath);
    }
}

public sealed record TerrariaRuntimeShadowLayout(string SolutionPath, string SourceRootPath, string OutputRootPath, string WorkspacePath, string ArtifactsPath, string DependencyEnvironmentPath, string WorkspaceSolutionPath)
{
    public static TerrariaRuntimeShadowLayout Create(TerrariaRuntimeShadowExtractionRequest request)
    {
        var sourceRootPath = Path.GetDirectoryName(request.SolutionPath) ?? throw new InvalidOperationException("TR solution path must have a parent directory.");
        var solutionFileName = Path.GetFileName(request.SolutionPath);
        var workspacePath = Path.Combine(request.OutputRootPath, "workspace");
        var artifactsPath = Path.Combine(request.OutputRootPath, "artifacts");
        var dependencyEnvironmentPath = Path.Combine(request.OutputRootPath, "dependency-env");
        var workspaceSolutionPath = Path.Combine(workspacePath, solutionFileName);
        return new(request.SolutionPath, sourceRootPath, request.OutputRootPath, workspacePath, artifactsPath, dependencyEnvironmentPath, workspaceSolutionPath);
    }
}

public sealed record TerrariaRuntimeProcessResult(int ExitCode, string StandardOutput, string StandardError);
public sealed record TerrariaRuntimeShadowRewriteSummary(int PreservedMembers, int DefaultedMembers, int EmptiedMembers, IReadOnlyList<string> SamplePreservedMembers, IReadOnlyList<string> SampleDefaultedMembers, IReadOnlyList<string> SampleEmptiedMembers);
public sealed record TerrariaRuntimeBuildSummary(bool BuildSucceeded, int BuildExitCode, string BuildCommand, string RuntimeWorkspacePath, string DependencyEnvironmentPath, string SolutionPath, string StandardOutput, string StandardError);
public sealed record TerrariaRuntimeShadowExtractionReport(string SeedMemberName, string SeedMemberId, IReadOnlyList<string> IncludedDocuments, IReadOnlyList<string> ReachableMethods, AdvancedAnalysisSummary AdvancedAnalysisSummary, int RewrittenDocuments, TerrariaRuntimeShadowRewriteSummary RewriteSummary)
{
    public TerrariaRuntimeBuildSummary? TrBuildSummary { get; init; }
}

public sealed record RewriteExecutionResult(bool IsSuccess, FailureCode FailureCode, string? RewrittenSource, string? Message)
{
    public static RewriteExecutionResult Success(string rewrittenSource) => new(true, FailureCode.None, rewrittenSource, null);
    public static RewriteExecutionResult Failure(string? message) => new(false, FailureCode.RewriteFailed, null, message);
}

public sealed record RunResult(bool IsSuccess, FailureCode FailureCode, string OutputPath, string? ReportPath, string? Message)
{
    public static RunResult Success(string outputPath, string? reportPath) => new(true, FailureCode.None, outputPath, reportPath, null);
    public static RunResult Failure(FailureCode code, string outputPath, string? message) => new(false, code, outputPath, null, message);
}

public sealed record FailureSummary(FailureCode FailureCode, string Message);
public sealed record ConflictSummary(string ConflictCode, string TargetKey, string TargetDisplayText, IReadOnlyList<PlanActionKind> ActionKinds, string Reason);
public sealed record RiskSummary(int SkippedHighRiskTargetCount, IReadOnlyList<string> SampleTargetDisplayTexts);
public sealed record PlanCoverageSummary(int CoveredMethodCount, int CoveredStatementCount, IReadOnlyList<string> SampleCoveredTargetDisplayTexts);
public sealed record ReferenceZeroPredictionSummary(int PredictedMethodDeleteCount, IReadOnlyList<string> SamplePredictedMethodIds);
public sealed record BoundaryPromotionSummary(BoundaryKind BoundaryKind, int PromotedMethodDeleteCount, IReadOnlyList<string> SamplePromotedMethodIds);

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
    IReadOnlyList<WorkspaceLoadDiagnostic> WorkspaceDiagnostics,
    string? Message)
{
    public AdvancedAnalysisSummary? AdvancedAnalysisSummary { get; init; }
    public TerrariaRuntimeBuildSummary? TrBuildSummary { get; init; }
}
