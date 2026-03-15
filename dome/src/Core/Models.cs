using Microsoft.CodeAnalysis;

namespace TerrariaTools.Dome.Core;

/// <summary>
/// 杩愯妯″紡鏋氫妇銆?
/// </summary>
public enum RunMode
{
    /// <summary>
    /// 鏍囧噯妯″紡锛屾墽琛屽畬鏁存祦绋嬨€?
    /// </summary>
    Standard,
    /// <summary>
    /// 浠呭垎鏋愭ā寮忋€?
    /// </summary>
    AnalyzeOnly,
    /// <summary>
    /// 浠呰鍒掓ā寮忋€?
    /// </summary>
    PlanOnly
}

/// <summary>
/// 宸ヤ綔鍖哄姞杞藉亸濂芥灇涓俱€?
/// </summary>
public enum WorkspaceLoaderPreference
{
    /// <summary>
    /// 鑷姩閫夋嫨銆?
    /// </summary>
    Auto,
    /// <summary>
    /// 浼樺厛浣跨敤 CodeAnalysis銆?
    /// </summary>
    CodeAnalysisFirst,
    /// <summary>
    /// 浠呬娇鐢ㄦ簮鐮佹壂鎻忋€?
    /// </summary>
    SourceOnly
}

/// <summary>
/// 宸ヤ綔鍖哄姞杞芥ā寮忔灇涓俱€?
/// </summary>
public enum WorkspaceLoadMode
{
    CodeAnalysis,
    SourceOnly,
    CodeAnalysisFallbackToSourceOnly
}

/// <summary>
/// 宸ヤ綔鍖哄姞杞借瘖鏂骇鍒€?
/// </summary>
public enum WorkspaceLoadDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// 澶辫触浠ｇ爜鏋氫妇銆?
/// </summary>
public enum FailureCode
{
    /// <summary>
    /// 鏃犲け璐ャ€?
    /// </summary>
    None,
    /// <summary>
    /// 宸ヤ綔鍖哄姞杞藉け璐ャ€?
    /// </summary>
    WorkspaceLoadFailed,
    /// <summary>
    /// 鍒嗘瀽澶辫触銆?
    /// </summary>
    AnalysisFailed,
    /// <summary>
    /// 璁″垝缂栬瘧澶辫触銆?
    /// </summary>
    PlanCompileFailed,
    /// <summary>
    /// 閲嶅啓澶辫触銆?
    /// </summary>
    RewriteFailed,
    /// <summary>
    /// 鏋勫缓澶辫触銆?
    /// </summary>
    BuildFailed,
    /// <summary>
    /// 鎶ュ憡鐢熸垚澶辫触銆?
    /// </summary>
    ReportFailed
}

/// <summary>
/// 鎴愬憳绫诲瀷鏋氫妇銆?
/// </summary>
public enum MemberKind
{
    /// <summary>
    /// 鏈煡绫诲瀷銆?
    /// </summary>
    Unknown,
    /// <summary>
    /// 绫汇€?
    /// </summary>
    Class,
    /// <summary>
    /// 瀛楁銆?
    /// </summary>
    Field,
    /// <summary>
    /// 鏂规硶銆?
    /// </summary>
    Method,
    /// <summary>
    /// 鏋勯€犲嚱鏁般€?
    /// </summary>
    Constructor,
    /// <summary>
    /// 灞炴€с€?
    /// </summary>
    Property,
    /// <summary>
    /// 璁块棶鍣ㄣ€?
    /// </summary>
    Accessor
}

/// <summary>
/// 鐩爣绫诲瀷鏋氫妇銆?
/// </summary>
public enum TargetKind
{
    /// <summary>
    /// 璇彞銆?
    /// </summary>
    Statement,
    /// <summary>
    /// 鏂规硶銆?
    /// </summary>
    Method,
    /// <summary>
    /// Internal field target.
    /// </summary>
    Field,
    /// <summary>
    /// Internal property target.
    /// </summary>
    Property,
    /// <summary>
    /// 绫汇€?
    /// </summary>
    Class
}

/// <summary>
/// 璇彞绫诲瀷寮曠敤鏋氫妇銆?
/// 鐢ㄤ簬鍖哄垎鎺у埗娴佽鍙ワ紙If, While, For, Return锛夊拰鍏朵粬璇彞锛?
/// 浠ヤ究鍦ㄨ鍒欏紩鎿庝腑搴旂敤鐗瑰畾鐨勫垎鏋愰€昏緫锛堝 DirectiveSeedRule 鍜?ExpressionProjectionRule锛夈€?
/// </summary>
public enum StatementKindRef
{
    /// <summary>
    /// 鏈煡绫诲瀷銆?
    /// </summary>
    Unknown,
    /// <summary>
    /// 鍒濆鍖栧櫒銆?
    /// </summary>
    Initializer,
    /// <summary>
    /// 澹版槑銆?
    /// </summary>
    Declaration,
    /// <summary>
    /// 璧嬪€笺€?
    /// </summary>
    Assignment,
    /// <summary>
    /// If 璇彞銆?
    /// </summary>
    If,
    /// <summary>
    /// While 寰幆銆?
    /// </summary>
    While,
    /// <summary>
    /// For 寰幆銆?
    /// </summary>
    For,
    /// <summary>
    /// 杩斿洖璇彞銆?
    /// </summary>
    Return,
    /// <summary>
    /// 瀵硅薄鍒濆鍖栧櫒璧嬪€笺€?
    /// </summary>
    ObjectInitializerAssignment
}

/// <summary>
/// 璇彞鍒嗘瀽浣滅敤鍩熸ā寮忋€?
/// </summary>
public enum StatementScopeMode
{
    /// <summary>
    /// 鏈€灏忓潡鑼冨洿銆?
    /// </summary>
    MinimalBlock,
    /// <summary>
    /// 绌块€忕埗绾у潡鑼冨洿銆?
    /// </summary>
    ParentBlockPiercing
}

/// <summary>
/// StatementGraph 鐗╁寲鐘舵€併€?
/// </summary>
public enum StatementGraphMaterialization
{
    None,
    SnapshotOnly,
    Full
}

/// <summary>
/// FunctionGraph 鐗╁寲鐘舵€併€?
/// </summary>
public enum FunctionGraphMaterialization
{
    None,
    WholeProject,
    ExpandedMembers
}

/// <summary>
/// 杈圭晫鎻愬崌绫诲瀷銆?
/// </summary>
public enum BoundaryKind
{
    Invocation
}

/// <summary>
/// 璇彞鍒嗘瀽浣滅敤鍩熼€夐」銆?
/// </summary>
public sealed record ScopeAnalysisOptions(
    StatementScopeMode StatementScopeMode)
{
    /// <summary>
    /// 鑾峰彇榛樿浣滅敤鍩熷垎鏋愰€夐」銆?
    /// </summary>
    public static ScopeAnalysisOptions Default { get; } =
        new(StatementScopeMode.MinimalBlock);
}

/// <summary>
/// 璁″垝鎿嶄綔绫诲瀷鏋氫妇銆?
/// 娣诲姞杩斿洖璇彞鏃跺悓鏃舵坊鍔犻粯璁ゅ€?
/// 瑕佸拷鐣ュ睘鎬ц繖绉嶄笢瑗?
/// </summary>
public enum PlanActionKind
{
    /// <summary>
    /// 鍒犻櫎銆?
    /// </summary>
    Delete,
    /// <summary>
    /// 娉ㄩ噴鎺夈€?
    /// </summary>
    CommentOut,
    /// <summary>
    /// 鏇挎崲涓洪粯璁ゅ€笺€?
    /// </summary>
    ReplaceWithDefault,
    /// <summary>
    /// 娣诲姞杩斿洖璇彞銆?
    /// </summary>
    AddReturn,
    /// <summary>
    /// Convert method visibility to private.
    /// </summary>
    ChangeVisibilityToPrivate,
    /// <summary>
    /// Reorder public methods within a type.
    /// </summary>
    ReorderPublicMethods
}

public enum DecisionOrigin
{
    Rule,
    Seed,
    Projection,
    Propagation,
    BoundaryPromotion,
    Prediction,
    Cleanup
}

public enum DecisionCategory
{
    Delete,
    CommentOut,
    ReplaceWithDefault,
    AddReturn,
    VisibilityChange,
    Reorder
}

/// <summary>
/// 鎴愬憳 ID 缁撴瀯銆?
/// </summary>
/// <param name="Value">ID 鍊笺€?/param>
public readonly record struct MemberId(string Value)
{
    /// <summary>
    /// 杩斿洖 ID 鐨勫瓧绗︿覆琛ㄧず銆?
    /// </summary>
    public override string ToString() => Value;
}

/// <summary>
/// 宸ヤ綔鍖哄姞杞介€夐」銆?
/// </summary>
/// <param name="PreferredLoader">棣栭€夊姞杞藉櫒銆?/param>
/// <param name="AllowFallbackToSourceOnly">鏄惁鍏佽鍥為€€鍒版簮鐮佹壂鎻忋€?/param>
public sealed record WorkspaceLoadOptions(
    WorkspaceLoaderPreference PreferredLoader,
    bool AllowFallbackToSourceOnly)
{
    /// <summary>
    /// 鑾峰彇榛樿宸ヤ綔鍖哄姞杞介€夐」銆?
    /// </summary>
    public static WorkspaceLoadOptions Default { get; } =
        new(WorkspaceLoaderPreference.Auto, true);
}

/// <summary>
/// 宸ヤ綔鍖哄姞杞借瘖鏂€?
/// </summary>
/// <param name="Stage">璇婃柇闃舵銆?/param>
/// <param name="Severity">璇婃柇绾у埆銆?/param>
/// <param name="Message">璇婃柇娑堟伅銆?/param>
public sealed record WorkspaceLoadDiagnostic(
    string Stage,
    WorkspaceLoadDiagnosticSeverity Severity,
    string Message);

/// <summary>
/// 婧愮爜鏂囨。璁板綍銆?
/// </summary>
/// <param name="SourcePath">婧愮爜缁濆璺緞銆?/param>
/// <param name="RelativePath">鐩稿璺緞銆?/param>
/// <param name="SourceText">婧愮爜鍐呭銆?/param>
public sealed record SourceDocument(
    string SourcePath,
    string RelativePath,
    string SourceText);

/// <summary>
/// 鍒嗘瀽杈撳叆鎶借薄銆?
/// </summary>
public abstract record AnalysisInput(string RootPath);

/// <summary>
/// 绾簮鐮佸垎鏋愯緭鍏ャ€?
/// </summary>
public sealed record SourceOnlyAnalysisInput(
    string RootPath,
    IReadOnlyList<SourceDocument> Documents) : AnalysisInput(RootPath);

/// <summary>
/// Workspace 鏂囨。涓婁笅鏂囥€?
/// </summary>
public sealed record WorkspaceAnalysisDocumentContext(
    Document Document,
    SourceDocument SourceDocument,
    Compilation Compilation,
    SemanticModel SemanticModel,
    SyntaxNode Root);

/// <summary>
/// Workspace 鍒嗘瀽杈撳叆銆?
/// </summary>
public sealed record WorkspaceAnalysisContextInput(
    Solution Solution,
    Project? Project,
    string RootPath,
    IReadOnlyList<WorkspaceAnalysisDocumentContext> Documents) : AnalysisInput(RootPath);

/// <summary>
/// Rewrite 鏂囨。涓婁笅鏂囥€?
/// </summary>
public sealed record RewriteExecutionDocumentContext(
    SourceDocument Document,
    SyntaxNode Root,
    SemanticModel? SemanticModel);

public sealed record AnalysisDocumentContext(
    SourceDocument Document,
    SyntaxNode Root,
    SemanticModel SemanticModel,
    IReadOnlyList<AnalysisTarget> Targets);

public sealed record AnalysisPerformanceSummary(
    int DocumentCount,
    TimeSpan SyntaxIndexTime,
    TimeSpan TypeGraphTime,
    TimeSpan FunctionNodeTime,
    TimeSpan TypeBodyGraphTime,
    TimeSpan TargetAnalysisTime,
    TimeSpan FunctionFactsTime,
    TimeSpan MergeTime);

/// <summary>
/// 杩愯璇锋眰璁板綍銆?
/// </summary>
/// <param name="InputPath">杈撳叆璺緞銆?/param>
/// <param name="OutputPath">杈撳嚭璺緞銆?/param>
/// <param name="RuleSet">瑙勫垯闆嗐€?/param>
/// <param name="Mode">杩愯妯″紡銆?/param>
public sealed record RunRequest(
    string InputPath,
    string OutputPath,
    IReadOnlyList<string> RuleSet,
    RunMode Mode,
    WorkspaceLoadOptions WorkspaceLoadOptions)
{
    /// <summary>
    /// 浣跨敤榛樿宸ヤ綔鍖哄姞杞介€夐」鍒濆鍖栬繍琛岃姹傘€?
    /// </summary>
    public RunRequest(string inputPath, string outputPath, IReadOnlyList<string> ruleSet, RunMode mode)
        : this(inputPath, outputPath, ruleSet, mode, WorkspaceLoadOptions.Default)
    {
    }
}

/// <summary>
/// TR 涓撶敤杩愯璇锋眰璁板綍銆?
/// </summary>
/// <param name="SolutionPath">TR 瑙ｅ喅鏂规璺緞銆?/param>
/// <param name="OutputRootPath">杩愯鏃惰緭鍑烘牴鐩綍銆?/param>
public sealed record TerrariaRuntimeRunRequest(
    string SolutionPath,
    string OutputRootPath);

/// <summary>
/// TR shadow extraction request.
/// </summary>
public sealed record TerrariaRuntimeShadowExtractionRequest(
    string SolutionPath,
    string OutputRootPath,
    string SeedMemberName);

/// <summary>
/// TR 杩愯鏃剁洰褰曞竷灞€銆?
/// </summary>
public sealed record TerrariaRuntimeLayout(
    string SolutionPath,
    string SourceRootPath,
    string OutputRootPath,
    string DependencyEnvironmentPath,
    string WorkspacePath,
    string ArtifactsPath,
    string WorkspaceSolutionPath)
{
    /// <summary>
    /// 鐢?TR 杩愯璇锋眰鍒涘缓鐩綍甯冨眬銆?
    /// </summary>
    /// <param name="request">TR 杩愯璇锋眰銆?/param>
    /// <returns>杩愯鏃剁洰褰曞竷灞€銆?/returns>
    public static TerrariaRuntimeLayout Create(TerrariaRuntimeRunRequest request)
    {
        var sourceRootPath = Path.GetDirectoryName(request.SolutionPath)
            ?? throw new InvalidOperationException("TR solution path must have a parent directory.");
        var solutionFileName = Path.GetFileName(request.SolutionPath);
        var dependencyEnvironmentPath = Path.Combine(request.OutputRootPath, "dependency-env");
        var workspacePath = Path.Combine(request.OutputRootPath, "workspace");
        var artifactsPath = Path.Combine(request.OutputRootPath, "artifacts");
        var workspaceSolutionPath = Path.Combine(workspacePath, solutionFileName);
        return new TerrariaRuntimeLayout(
            request.SolutionPath,
            sourceRootPath,
            request.OutputRootPath,
            dependencyEnvironmentPath,
            workspacePath,
            artifactsPath,
            workspaceSolutionPath);
    }
}

/// <summary>
/// TR shadow extraction layout.
/// </summary>
public sealed record TerrariaRuntimeShadowLayout(
    string SolutionPath,
    string SourceRootPath,
    string OutputRootPath,
    string WorkspacePath,
    string ArtifactsPath,
    string DependencyEnvironmentPath,
    string WorkspaceSolutionPath)
{
    public static TerrariaRuntimeShadowLayout Create(TerrariaRuntimeShadowExtractionRequest request)
    {
        var sourceRootPath = Path.GetDirectoryName(request.SolutionPath)
            ?? throw new InvalidOperationException("TR solution path must have a parent directory.");
        var solutionFileName = Path.GetFileName(request.SolutionPath);
        var workspacePath = Path.Combine(request.OutputRootPath, "workspace");
        var artifactsPath = Path.Combine(request.OutputRootPath, "artifacts");
        var dependencyEnvironmentPath = Path.Combine(request.OutputRootPath, "dependency-env");
        var workspaceSolutionPath = Path.Combine(workspacePath, solutionFileName);
        return new TerrariaRuntimeShadowLayout(
            request.SolutionPath,
            sourceRootPath,
            request.OutputRootPath,
            workspacePath,
            artifactsPath,
            dependencyEnvironmentPath,
            workspaceSolutionPath);
    }
}

/// <summary>
/// TR 澶栭儴杩涚▼鎵ц缁撴灉銆?
/// </summary>
public sealed record TerrariaRuntimeProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

/// <summary>
/// TR shadow extraction report.
/// </summary>
public sealed record TerrariaRuntimeShadowExtractionReport(
    string SeedMemberName,
    string SeedMemberId,
    IReadOnlyList<string> IncludedDocuments,
    IReadOnlyList<string> ReachableMethods,
    AdvancedAnalysisSummary AdvancedAnalysisSummary,
    int RewrittenDocuments,
    TerrariaRuntimeShadowRewriteSummary RewriteSummary)
{
    public TerrariaRuntimeBuildSummary? TrBuildSummary { get; init; }
}

/// <summary>
/// TR shadow rewrite summary.
/// </summary>
public sealed record TerrariaRuntimeShadowRewriteSummary(
    int PreservedMembers,
    int DefaultedMembers,
    int EmptiedMembers,
    IReadOnlyList<string> SamplePreservedMembers,
    IReadOnlyList<string> SampleDefaultedMembers,
    IReadOnlyList<string> SampleEmptiedMembers);

/// <summary>
/// TR 鏋勫缓鎽樿銆?
/// </summary>
public sealed record TerrariaRuntimeBuildSummary(
    bool BuildSucceeded,
    int BuildExitCode,
    string BuildCommand,
    string RuntimeWorkspacePath,
    string DependencyEnvironmentPath,
    string SolutionPath,
    string StandardOutput,
    string StandardError);

/// <summary>
/// 杩愯缁撴灉璁板綍銆?
/// </summary>
/// <param name="IsSuccess">鏄惁鎴愬姛銆?/param>
/// <param name="FailureCode">澶辫触浠ｇ爜銆?/param>
/// <param name="OutputPath">杈撳嚭璺緞銆?/param>
/// <param name="ReportPath">鎶ュ憡璺緞銆?/param>
/// <param name="Message">娑堟伅銆?/param>
public sealed record RunResult(
    bool IsSuccess,
    FailureCode FailureCode,
    string OutputPath,
    string? ReportPath,
    string? Message)
{
    /// <summary>
    /// 鍒涘缓鎴愬姛缁撴灉銆?
    /// </summary>
    public static RunResult Success(string outputPath, string? reportPath) =>
        new(true, FailureCode.None, outputPath, reportPath, null);

    /// <summary>
    /// 鍒涘缓澶辫触缁撴灉銆?
    /// </summary>
    public static RunResult Failure(FailureCode code, string outputPath, string? message) =>
        new(false, code, outputPath, null, message);
}

/// <summary>
/// 璁″垝鍏冩暟鎹褰曘€?
/// </summary>
/// <param name="ToolName">宸ュ叿鍚嶇О銆?/param>
/// <param name="PlanVersion">璁″垝鐗堟湰銆?/param>
/// <param name="InputPath">杈撳叆璺緞銆?/param>
/// <param name="OutputPath">杈撳嚭璺緞銆?/param>
/// <param name="RunMode">杩愯妯″紡銆?/param>
public sealed record PlanMetadata(
    string ToolName,
    string PlanVersion,
    string InputPath,
    string OutputPath,
    RunMode RunMode)
{
    /// <summary>
    /// 鐢熸垚鏃堕棿锛圲TC锛夈€?
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 璁″垝鐩爣璁板綍銆?
/// </summary>
/// <param name="DocumentPath">鏂囨。璺緞銆?/param>
/// <param name="MemberId">鎴愬憳 ID銆?/param>
/// <param name="MemberKind">鎴愬憳绫诲瀷銆?/param>
/// <param name="TargetKind">鐩爣绫诲瀷銆?/param>
/// <param name="SpanStart">璺ㄥ害璧峰浣嶇疆銆?/param>
/// <param name="SpanLength">璺ㄥ害闀垮害銆?/param>
/// <param name="DisplayText">鏄剧ず鏂囨湰銆?/param>
public sealed record PlanTarget(
    string DocumentPath,
    MemberId MemberId,
    MemberKind MemberKind,
    TargetKind TargetKind,
    int SpanStart,
    int SpanLength,
    string DisplayText,
    TargetResolutionKey? ResolutionKey = null)
{
    /// <summary>
    /// 鐩爣鍞竴閿€?
    /// </summary>
    public string TargetKey => $"{DocumentPath}|{MemberId.Value}|{TargetKind}|{SpanStart}|{SpanLength}";

    public TargetResolutionKey EffectiveResolutionKey => ResolutionKey ?? new(SpanStart, SpanLength);
}

public sealed record TargetResolutionKey(
    int SpanStart,
    int SpanLength);

/// <summary>
/// 璁″垝鎿嶄綔璁板綍銆?
/// </summary>
/// <param name="Kind">鎿嶄綔绫诲瀷銆?/param>
/// <param name="Payload">璐熻浇鏁版嵁銆?/param>
public sealed record PlanAction(
    PlanActionKind Kind,
    string? Payload = null);

/// <summary>
/// 璁″垝鍘熷洜璁板綍銆?
/// </summary>
/// <param name="RuleId">瑙勫垯 ID銆?/param>
/// <param name="ReasonText">鍘熷洜鏂囨湰銆?/param>
/// <param name="SourceTargetKey">婧愮洰鏍囬敭銆?/param>
/// <param name="SourceTargetDisplayText">婧愮洰鏍囨樉绀烘枃鏈€?/param>
/// <param name="RelatedSymbolKeys">鐩稿叧绗﹀彿閿€?/param>
/// <param name="RelatedSymbolNames">鐩稿叧绗﹀彿鍚嶇О銆?/param>
/// <param name="Severity">涓ラ噸绋嬪害銆?/param>
public sealed record PlanReason(
    string RuleId,
    string ReasonText,
    string? SourceTargetKey = null,
    string? SourceTargetDisplayText = null,
    IReadOnlyList<string>? RelatedSymbolKeys = null,
    IReadOnlyList<string>? RelatedSymbolNames = null,
    string? Severity = null,
    string? SourceMemberId = null,
    BoundaryKind? BoundaryKind = null,
    IReadOnlyList<string>? TriggeredSymbolKeys = null,
    DecisionOrigin Origin = DecisionOrigin.Rule,
    DecisionCategory Category = DecisionCategory.Delete);

/// <summary>
/// 浼犳挱璇佹嵁璁板綍銆?
/// </summary>
/// <param name="RelatedSymbolKeys">鐩稿叧绗﹀彿閿垪琛ㄣ€?/param>
/// <param name="RelatedSymbolNames">鐩稿叧绗﹀彿鍚嶇О鍒楄〃銆?/param>
public sealed record PropagationEvidence(
    IReadOnlyList<string> RelatedSymbolKeys,
    IReadOnlyList<string> RelatedSymbolNames);

/// <summary>
/// 浼犳挱璺宠穬璁板綍銆?
/// </summary>
/// <param name="FromTargetKey">璧峰鐩爣閿€?/param>
/// <param name="FromTargetDisplayText">璧峰鐩爣鏄剧ず鏂囨湰銆?/param>
/// <param name="ToTargetKey">鐩爣鐩爣閿€?/param>
/// <param name="ToTargetDisplayText">鐩爣鐩爣鏄剧ず鏂囨湰銆?/param>
/// <param name="RuleId">瑙勫垯 ID銆?/param>
/// <param name="ActionKind">鎿嶄綔绫诲瀷銆?/param>
/// <param name="Evidence">璇佹嵁銆?/param>
public sealed record PropagationHop(
    string FromTargetKey,
    string FromTargetDisplayText,
    string ToTargetKey,
    string ToTargetDisplayText,
    string RuleId,
    PlanActionKind ActionKind,
    PropagationEvidence Evidence);

/// <summary>
/// 浼犳挱閾捐褰曘€?
/// </summary>
/// <param name="RootTargetKey">鏍圭洰鏍囬敭銆?/param>
/// <param name="RootTargetDisplayText">鏍圭洰鏍囨樉绀烘枃鏈€?/param>
/// <param name="Hops">璺宠穬鍒楄〃銆?/param>
public sealed record PropagationChain(
    string RootTargetKey,
    string RootTargetDisplayText,
    IReadOnlyList<PropagationHop> Hops);

/// <summary>
/// 鏍囪鍐崇瓥璁板綍銆?
/// </summary>
/// <param name="Target">璁″垝鐩爣銆?/param>
/// <param name="Action">璁″垝鎿嶄綔銆?/param>
/// <param name="Reason">璁″垝鍘熷洜銆?/param>
/// <param name="Chain">浼犳挱閾俱€?/param>
public sealed record MarkDecision(
    PlanTarget Target,
    PlanAction Action,
    PlanReason Reason,
    PropagationChain? Chain = null)
{
    /// <summary>
    /// 涓虹洰鏍囧垱寤烘爣璁板喅绛栥€?
    /// </summary>
    public static MarkDecision ForTarget(
        PlanTarget target,
        PlanActionKind actionKind,
        string ruleId,
        string reasonText,
        string? payload = null,
        string? sourceTargetKey = null,
        string? sourceTargetDisplayText = null,
        IReadOnlyList<string>? relatedSymbolKeys = null,
        IReadOnlyList<string>? relatedSymbolNames = null,
        string? severity = null,
        string? sourceMemberId = null,
        BoundaryKind? boundaryKind = null,
        IReadOnlyList<string>? triggeredSymbolKeys = null,
        DecisionOrigin origin = DecisionOrigin.Rule,
        DecisionCategory? category = null,
        PropagationChain? chain = null) =>
        new(
            target,
            new PlanAction(actionKind, payload),
            new PlanReason(
                ruleId,
                reasonText,
                sourceTargetKey,
                sourceTargetDisplayText,
                relatedSymbolKeys ?? Array.Empty<string>(),
                relatedSymbolNames ?? Array.Empty<string>(),
                severity,
                sourceMemberId,
                boundaryKind,
                triggeredSymbolKeys ?? Array.Empty<string>(),
                origin,
                category ?? ToDecisionCategory(actionKind)),
            chain);

    private static DecisionCategory ToDecisionCategory(PlanActionKind actionKind) =>
        actionKind switch
        {
            PlanActionKind.Delete => DecisionCategory.Delete,
            PlanActionKind.CommentOut => DecisionCategory.CommentOut,
            PlanActionKind.ReplaceWithDefault => DecisionCategory.ReplaceWithDefault,
            PlanActionKind.AddReturn => DecisionCategory.AddReturn,
            PlanActionKind.ChangeVisibilityToPrivate => DecisionCategory.VisibilityChange,
            PlanActionKind.ReorderPublicMethods => DecisionCategory.Reorder,
            _ => throw new ArgumentOutOfRangeException(nameof(actionKind), actionKind, null)
        };
}

/// <summary>
/// 璁″垝鍙樻洿璁板綍銆?
/// </summary>
/// <param name="ExecutionOrder">鎵ц椤哄簭銆?/param>
/// <param name="Target">璁″垝鐩爣銆?/param>
/// <param name="Action">璁″垝鎿嶄綔銆?/param>
/// <param name="Reason">璁″垝鍘熷洜銆?/param>
/// <param name="Chain">浼犳挱閾俱€?/param>
public sealed record PlannedChange(
    int ExecutionOrder,
    PlanTarget Target,
    PlanAction Action,
    PlanReason Reason,
    PropagationChain? Chain = null);

/// <summary>
/// 璁″垝鍐茬獊璁板綍銆?
/// </summary>
/// <param name="ConflictCode">鍐茬獊浠ｇ爜銆?/param>
/// <param name="Target">璁″垝鐩爣銆?/param>
/// <param name="ActionKinds">鎿嶄綔绫诲瀷鍒楄〃銆?/param>
/// <param name="Reason">鍘熷洜銆?/param>
public sealed record PlanConflict(
    string ConflictCode,
    PlanTarget Target,
    IReadOnlyList<PlanActionKind> ActionKinds,
    string Reason);

/// <summary>
/// 瀹¤璁″垝璁板綍銆?
/// </summary>
/// <param name="Metadata">璁″垝鍏冩暟鎹€?/param>
/// <param name="Changes">璁″垝鍙樻洿鍒楄〃銆?/param>
/// <param name="Conflicts">璁″垝鍐茬獊鍒楄〃銆?/param>
public sealed record AuditPlan(
    PlanMetadata Metadata,
    IReadOnlyList<PlannedChange> Changes,
    IReadOnlyList<PlanConflict> Conflicts);

/// <summary>
/// 绫诲瀷渚濊禆绫诲瀷鏋氫妇銆?
/// 鎻忚堪绫诲瀷涔嬮棿鐨勯潤鎬佷緷璧栧叧绯伙紝濡傜户鎵裤€佸疄鐜般€佸瓧娈电被鍨嬬瓑銆?
/// </summary>
public enum TypeDependencyKind
{
    /// <summary>
    /// 缁ф壙銆?
    /// </summary>
    Inherits,
    /// <summary>
    /// 瀹炵幇銆?
    /// </summary>
    Implements,
    /// <summary>
    /// 瀛楁绫诲瀷銆?
    /// </summary>
    FieldType,
    /// <summary>
    /// 灞炴€х被鍨嬨€?
    /// </summary>
    PropertyType,
    /// <summary>
    /// 鍙傛暟绫诲瀷銆?
    /// </summary>
    ParameterType,
    /// <summary>
    /// 杩斿洖绫诲瀷銆?
    /// </summary>
    ReturnType,
    /// <summary>
    /// 瀵硅薄鍒涘缓銆?
    /// </summary>
    ObjectCreation,
    /// <summary>
    /// 闈欐€佹垚鍛樿闂€?
    /// </summary>
    StaticMemberAccess,
    /// <summary>
    /// 鎴愬憳浣撳紩鐢ㄣ€?
    /// </summary>
    MemberBodyReference
}

/// <summary>
/// 鍑芥暟渚濊禆绫诲瀷鏋氫妇銆?
/// 鎻忚堪鍑芥暟涔嬮棿鐨勫姩鎬佷緷璧栧叧绯汇€?
/// 娉ㄦ剰锛氱洰鍓?FunctionGraphProvider 涓昏鏀寔 Calls 绫诲瀷銆?
/// 鍏朵粬绫诲瀷锛圕reates, ReadsMember, WritesMember 绛夛級鍦ㄥ垎鏋愰樁娈垫湁鐢熸垚閫昏緫锛?
/// 浣嗗彲鑳芥湭琚畬鍏ㄦ寔涔呭寲鎴栧湪瑙嗗浘涓埄鐢ㄣ€?
/// </summary>
public enum FunctionDependencyKind
{
    /// <summary>
    /// 璋冪敤銆?
    /// </summary>
    Calls,
    /// <summary>
    /// 鍒涘缓銆?
    /// </summary>
    Creates,
    /// <summary>
    /// 璇诲彇鎴愬憳銆?
    /// </summary>
    ReadsMember,
    /// <summary>
    /// 鍐欏叆鎴愬憳銆?
    /// </summary>
    WritesMember,
    /// <summary>
    /// 浣跨敤灞炴€ц闂櫒銆?
    /// </summary>
    UsesPropertyAccessor
}

/// <summary>
/// 璇彞渚濊禆绫诲瀷鏋氫妇銆?
/// </summary>
public enum StatementDependencyKind
{
    /// <summary>
    /// 瀹氫箟銆?
    /// </summary>
    Defines,
    /// <summary>
    /// 浣跨敤銆?
    /// </summary>
    Uses,
    /// <summary>
    /// 鍏堜簬銆?
    /// </summary>
    Precedes
}

/// <summary>
/// 绫诲瀷鑺傜偣寮曠敤璁板綍銆?
/// </summary>
/// <param name="TypeId">绫诲瀷 ID銆?/param>
/// <param name="DisplayName">鏄剧ず鍚嶇О銆?/param>
/// <param name="DocumentPath">鏂囨。璺緞銆?/param>
public sealed record TypeNodeRef(
    string TypeId,
    string DisplayName,
    string DocumentPath);

/// <summary>
/// 绫诲瀷渚濊禆杈硅褰曘€?
/// </summary>
/// <param name="SourceTypeId">婧愮被鍨?ID銆?/param>
/// <param name="TargetTypeId">鐩爣绫诲瀷 ID銆?/param>
/// <param name="Kind">渚濊禆绫诲瀷銆?/param>
/// <param name="MemberId">鎴愬憳 ID銆?/param>
/// <param name="SymbolKey">绗﹀彿閿€?/param>
public sealed record TypeDependencyEdge(
    string SourceTypeId,
    string TargetTypeId,
    TypeDependencyKind Kind,
    string? MemberId = null,
    string? SymbolKey = null);

/// <summary>
/// 绫诲瀷渚濊禆鍥捐褰曘€?
/// </summary>
/// <param name="Nodes">鑺傜偣鍒楄〃銆?/param>
/// <param name="Edges">杈瑰垪琛ㄣ€?/param>
public sealed record TypeDependencyGraph(
    IReadOnlyList<TypeNodeRef> Nodes,
    IReadOnlyList<TypeDependencyEdge> Edges);

/// <summary>
/// 鍑芥暟鑺傜偣寮曠敤璁板綍銆?
/// </summary>
/// <param name="MemberId">鎴愬憳 ID銆?/param>
/// <param name="MemberKind">鎴愬憳绫诲瀷銆?/param>
/// <param name="DeclaringTypeId">澹版槑绫诲瀷 ID銆?/param>
/// <param name="DisplayName">鏄剧ず鍚嶇О銆?/param>
/// <param name="DocumentPath">鏂囨。璺緞銆?/param>
/// <param name="SpanStart">璺ㄥ害璧峰浣嶇疆銆?/param>
/// <param name="SpanLength">璺ㄥ害闀垮害銆?/param>
/// <param name="IsPrivate">鏄惁绉佹湁銆?/param>
/// <param name="ReturnsVoid">鏄惁杩斿洖 Void銆?/param>
/// <param name="HasBody">鏄惁鏈夋柟娉曚綋銆?/param>
/// <param name="HasStatements">鏄惁鏈夎鍙ャ€?/param>
/// <param name="ReturnTypeDisplay">杩斿洖绫诲瀷鏄剧ず鏂囨湰銆?/param>
public sealed record FunctionNodeRef(
    MemberId MemberId,
    MemberKind MemberKind,
    string DeclaringTypeId,
    string DisplayName,
    string DocumentPath,
    int SpanStart,
    int SpanLength,
    bool IsPrivate,
    bool ReturnsVoid,
    bool HasBody,
    bool HasStatements,
    string ReturnTypeDisplay);

/// <summary>
/// 鍑芥暟渚濊禆杈硅褰曘€?
/// </summary>
/// <param name="SourceMemberId">婧愭垚鍛?ID銆?/param>
/// <param name="TargetMemberId">鐩爣鎴愬憳 ID銆?/param>
/// <param name="Kind">渚濊禆绫诲瀷銆?/param>
/// <param name="SymbolKey">绗﹀彿閿€?/param>
public sealed record FunctionDependencyEdge(
    MemberId SourceMemberId,
    MemberId TargetMemberId,
    FunctionDependencyKind Kind,
    string? SymbolKey = null);

/// <summary>
/// 鍑芥暟渚濊禆鍥捐褰曘€?
/// </summary>
/// <param name="Nodes">鑺傜偣鍒楄〃銆?/param>
/// <param name="Edges">杈瑰垪琛ㄣ€?/param>
public sealed record FunctionDependencyGraph(
    IReadOnlyList<FunctionNodeRef> Nodes,
    IReadOnlyList<FunctionDependencyEdge> Edges);

/// <summary>
/// 鍑芥暟鍒犻櫎褰卞搷鑼冨洿闆嗗悎銆?
/// </summary>
public sealed record FunctionImpactSet(
    IReadOnlyList<string> DeletedFunctionIds,
    IReadOnlyList<string> AffectedFunctionIds,
    IReadOnlyList<string> AffectedDocumentPaths,
    int ExpansionDepth,
    IReadOnlyList<FunctionDependencyKind> EdgeKinds);

/// <summary>
/// 璇彞渚濊禆杈硅褰曘€?
/// </summary>
/// <param name="SourceTargetKey">婧愮洰鏍囬敭銆?/param>
/// <param name="TargetTargetKey">鐩爣鐩爣閿€?/param>
/// <param name="Kind">渚濊禆绫诲瀷銆?/param>
/// <param name="SymbolKey">绗﹀彿閿€?/param>
public sealed record StatementDependencyEdge(
    string SourceTargetKey,
    string TargetTargetKey,
    StatementDependencyKind Kind,
    string? SymbolKey = null);

/// <summary>
/// 璇彞渚濊禆鍥捐褰曘€?
/// </summary>
/// <param name="Nodes">鑺傜偣鍒楄〃銆?/param>
/// <param name="Edges">杈瑰垪琛ㄣ€?/param>
public sealed record StatementDependencyGraph(
    IReadOnlyList<string> Nodes,
    IReadOnlyList<StatementDependencyEdge> Edges);

/// <summary>
/// 鍑芥暟绱㈠紩銆?
/// </summary>
public sealed record FunctionIndex(
    IReadOnlyDictionary<string, FunctionNodeRef> NodesByMemberId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MemberIdsByDocumentPath)
{
    /// <summary>
    /// 绌哄嚱鏁扮储寮曞疄渚嬨€?
    /// </summary>
    public static FunctionIndex Empty { get; } = new(
        new Dictionary<string, FunctionNodeRef>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
}

/// <summary>
/// 鍑芥暟浜嬪疄璁板綍銆?
/// </summary>
public sealed record FunctionFact(
    FunctionNodeRef Node,
    IReadOnlyList<MemberId> CalledMemberIds);

/// <summary>
/// 鍑芥暟浜嬪疄绱㈠紩銆?
/// </summary>
public sealed record FunctionFactsIndex(
    IReadOnlyDictionary<string, FunctionFact> FactsByMemberId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MemberIdsByDocumentPath,
    IReadOnlyDictionary<string, IReadOnlyList<MemberId>> IncomingCallersByMemberId)
{
    /// <summary>
    /// 绌哄嚱鏁颁簨瀹炵储寮曞疄渚嬨€?
    /// </summary>
    public static FunctionFactsIndex Empty { get; } = new(
        new Dictionary<string, FunctionFact>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<MemberId>>(StringComparer.Ordinal));
}

/// <summary>
/// 鍑芥暟鍥捐寖鍥淬€?
/// </summary>
public enum FunctionGraphScope
{
    WholeProject,
    ExpandedMembers
}

/// <summary>
/// 鍑芥暟鍥惧揩鐓с€?
/// </summary>
public sealed record FunctionGraphSnapshot(
    FunctionGraphScope Scope,
    IReadOnlyList<MemberId> RootMemberIds,
    IReadOnlyList<string> IncludedDocumentPaths,
    FunctionDependencyGraph Graph);

/// <summary>
/// 鍑芥暟鍥捐姹傘€?
/// </summary>
public sealed record FunctionGraphRequest(
    FunctionGraphScope Scope,
    IReadOnlyList<MemberId> RootMemberIds,
    int Depth,
    IReadOnlyList<FunctionDependencyKind> EdgeKinds,
    string Requester,
    string Reason);

/// <summary>
/// 褰撳墠鍒嗘瀽闃舵鍙敤鐨勫嚱鏁板浘璇锋眰宸ュ巶銆?
/// </summary>
public static class FunctionGraphRequests
{
    /// <summary>
    /// 鍒涘缓鍏ㄩ」鐩皟鐢ㄥ浘璇锋眰銆?
    /// </summary>
    public static FunctionGraphRequest WholeProjectCalls(string requester, string reason) =>
        new(
            FunctionGraphScope.WholeProject,
            Array.Empty<MemberId>(),
            0,
            new[] { FunctionDependencyKind.Calls },
            requester,
            reason);

    /// <summary>
    /// 鍒涘缓鎵╁睍鎴愬憳璋冪敤鍥捐姹傘€?
    /// </summary>
    public static FunctionGraphRequest ExpandedMembersCalls(
        IReadOnlyList<MemberId> rootMemberIds,
        string requester,
        string reason) =>
        new(
            FunctionGraphScope.ExpandedMembers,
            rootMemberIds,
            1,
            new[] { FunctionDependencyKind.Calls },
            requester,
            reason);
}

/// <summary>
/// 鍑芥暟鍥炬彁渚涘櫒銆?
/// </summary>
public interface IFunctionGraphProvider
{
    /// <summary>
    /// 鑾峰彇鍑芥暟鍥惧揩鐓с€?
    /// </summary>
    FunctionGraphSnapshot GetSnapshot(FunctionGraphRequest request);

    /// <summary>
    /// 鑾峰彇鍏ㄩ」鐩嚱鏁板浘蹇収銆?
    /// </summary>
    FunctionGraphSnapshot GetWholeProjectSnapshot() =>
        GetSnapshot(FunctionGraphRequests.WholeProjectCalls("IFunctionGraphProvider", "Whole-project function graph snapshot"));

    /// <summary>
    /// 鑾峰彇鎵╁睍鎴愬憳鍑芥暟鍥惧揩鐓с€?
    /// </summary>
    FunctionGraphSnapshot GetExpandedMembersSnapshot(IReadOnlyList<MemberId> rootMemberIds, int depth = 1)
    {
        if (depth != 1)
        {
            throw new NotSupportedException("ExpandedMembers snapshots currently only support depth = 1.");
        }

        return GetSnapshot(FunctionGraphRequests.ExpandedMembersCalls(
            rootMemberIds,
            "IFunctionGraphProvider",
            "Expanded-members function graph snapshot"));
    }
}

/// <summary>
/// 绗﹀彿渚濊禆鑺傜偣绫诲瀷銆?/// </summary>
public enum SymbolDependencyNodeKind
{
    Unknown,
    Type,
    Method,
    Property,
    Field,
    Event
}

/// <summary>
/// 绗﹀彿渚濊禆杈圭被鍨嬨€?/// </summary>
public enum SymbolDependencyEdgeKind
{
    ContainsType,
    BaseType,
    InterfaceImplementation,
    ReturnType,
    ParameterType,
    FieldType,
    PropertyType,
    EventType,
    Override,
    ExplicitInterfaceImplementation,
    ConstructorInitializer,
    Invocation,
    ObjectCreation,
    InitializerReference,
    MemberReference,
    Conversion,
    CollectionInitializer
}

/// <summary>
/// 绗﹀彿渚濊禆鑺傜偣銆?/// </summary>
public sealed record SymbolDependencyNode(
    string SymbolId,
    SymbolDependencyNodeKind Kind,
    string DisplayName,
    string? DocumentPath);

/// <summary>
/// 绗﹀彿渚濊禆杈广€?/// </summary>
public sealed record SymbolDependencyEdge(
    string SourceSymbolId,
    string TargetSymbolId,
    SymbolDependencyEdgeKind Kind);

/// <summary>
/// 绗﹀彿渚濊禆鍥俱€?/// </summary>
public sealed record SymbolDependencyGraph(
    IReadOnlyList<SymbolDependencyNode> Nodes,
    IReadOnlyList<SymbolDependencyEdge> Edges);

/// <summary>
/// 绗﹀彿渚濊禆鏌ヨ閫夐」銆?/// </summary>
public sealed record SymbolDependencyQueryOptions(
    int? MaxDepth = null,
    IReadOnlyList<SymbolDependencyEdgeKind>? AllowedEdgeKinds = null,
    IReadOnlyList<SymbolDependencyNodeKind>? AllowedNodeKinds = null,
    bool IncludeRoots = true);

/// <summary>
/// 绗﹀彿渚濊禆璺緞銆?/// </summary>
public sealed record SymbolDependencyPath(
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<SymbolDependencyEdge> Edges);

/// <summary>
/// 绗﹀彿渚濊禆鍒囩墖缁撴灉銆?/// </summary>
public sealed record SymbolDependencySlice(
    SymbolDependencyGraph Graph,
    IReadOnlyList<SymbolDependencyPath> Paths);

/// <summary>
/// 绗﹀彿渚濊禆鍥炬彁渚涘櫒銆?/// </summary>
public interface ISymbolDependencyGraphProvider
{
    SymbolDependencyGraph GetWholeGraph();

    SymbolDependencyGraph GetBackwardSlice(string symbolId);

    SymbolDependencyGraph GetForwardSlice(IReadOnlyList<string> rootSymbolIds);

    SymbolDependencyGraph GetBackwardSlice(string symbolId, SymbolDependencyQueryOptions options);

    SymbolDependencyGraph GetForwardSlice(IReadOnlyList<string> rootSymbolIds, SymbolDependencyQueryOptions options);
}

/// <summary>
/// 鏂规硶璋冪敤鏌ヨ鏈嶅姟銆?/// </summary>
public interface IMethodCallQueryService
{
    IReadOnlyList<MemberId> GetCallees(MemberId memberId);

    IReadOnlyList<MemberId> GetCallers(MemberId memberId);

    IReadOnlyList<MemberId> GetReachableMethods(IReadOnlyList<MemberId> rootMemberIds);

    FunctionDependencyGraph GetWholeGraph();

    IReadOnlyList<MemberId> GetShortestPath(IReadOnlyList<MemberId> rootMemberIds, MemberId targetMemberId);

    MethodReachabilityExplanation ExplainReachability(MemberId rootMemberId, MemberId targetMemberId);
}

/// <summary>
/// 鏂规硶鍙揪鎬цВ閲娿€?/// </summary>
public sealed record MethodReachabilityExplanation(
    MemberId RootMemberId,
    MemberId TargetMemberId,
    bool IsReachable,
    IReadOnlyList<MemberId> Path);

/// <summary>
/// 鏁版嵁娴佹憳瑕併€?/// </summary>
public sealed record DataFlowSummary(
    MemberId MemberId,
    IReadOnlyList<string> DefinedSymbols,
    IReadOnlyList<string> UsedSymbols,
    IReadOnlyList<MemberId> InvokedMemberIds);

/// <summary>
/// 鏁版嵁娴佹憳瑕佹湇鍔°€?/// </summary>
public interface IDataFlowSummaryService
{
    DataFlowSummary Analyze(MemberId memberId);
}

/// <summary>
/// switch case 鎽樿銆?/// </summary>
public sealed record SwitchCaseSummary(
    string Label,
    IReadOnlyList<string> ReferencedSymbols,
    IReadOnlyList<MemberId> InvokedMemberIds);

/// <summary>
/// switch 娴佹憳瑕併€?/// </summary>
public sealed record SwitchFlowSummary(
    MemberId MemberId,
    IReadOnlyList<SwitchCaseSummary> Cases);

/// <summary>
/// switch 娴佹憳瑕佹湇鍔°€?/// </summary>
public interface ISwitchFlowSummaryService
{
    IReadOnlyList<SwitchFlowSummary> Analyze(MemberId memberId);
}

/// <summary>
/// 璋冪敤閾炬棩蹇楁潯鐩€?/// </summary>
public sealed record CallChainEntry(
    string Timestamp,
    string MethodName);

/// <summary>
/// 璋冪敤閾惧垎鏋愭憳瑕併€?/// </summary>
public sealed record CallChainAnalysisSummary(
    int TotalCalls,
    IReadOnlyList<MemberId> MappedMemberIds,
    IReadOnlyList<string> UnmappedMethods,
    IReadOnlyList<MemberId> PotentialStaticOnlyMemberIds);

/// <summary>
/// 璋冪敤閾惧垎鏋愭湇鍔°€?/// </summary>
public interface ICallChainAnalysisService
{
    IReadOnlyList<CallChainEntry> Parse(string logText);

    CallChainAnalysisSummary Analyze(string logText);
}

/// <summary>
/// 楂樺眰浠ｇ爜鍒嗘瀽姹囨€汇€?/// </summary>
public sealed record AdvancedAnalysisSummary(
    int MethodNodeCount,
    int MethodEdgeCount,
    IReadOnlyList<MemberId> MethodRoots,
    IReadOnlyList<string> SymbolRoots,
    int MethodSccCount,
    int SymbolSccCount,
    IReadOnlyList<IReadOnlyList<string>> LargestMethodComponents,
    IReadOnlyList<IReadOnlyList<string>> LargestSymbolComponents,
    IReadOnlyList<string> HighlyConnectedMethods,
    IReadOnlyList<string> HighlyConnectedSymbols,
    int InterfaceBridgeCount,
    int OverrideBridgeCount,
    int SymbolNodeCount,
    int SymbolEdgeCount)
{
    public int MethodRootCount => MethodRoots.Count;

    public int SymbolRootCount => SymbolRoots.Count;

    public IReadOnlyList<MemberId> RootMethods => MethodRoots;

    public IReadOnlyList<IReadOnlyList<string>> CyclicMethodComponents => LargestMethodComponents;
}

/// <summary>
/// 楂樺眰浠ｇ爜鍒嗘瀽姹囨€绘湇鍔°€?/// </summary>
public interface IAdvancedAnalysisSummaryService
{
    AdvancedAnalysisSummary BuildSummary();
}

/// <summary>
/// Internal member-cleanup symbol metadata.
/// </summary>
public sealed record MemberCleanupSymbolInfo(
    string SymbolId,
    MemberKind MemberKind,
    string DeclaringTypeId,
    string DocumentPath,
    string Name,
    bool IsPublic,
    bool IsPrivate,
    bool IsStatic,
    bool IsAbstract,
    bool IsVirtual,
    bool IsOverride,
    bool IsExtern,
    bool IsOrdinaryMethod,
    bool IsPartialType,
    bool IsNestedType,
    bool IsInInterfaceType,
    bool IsEntryPointLike);

/// <summary>
/// Internal member-cleanup type metadata.
/// </summary>
public sealed record MemberCleanupTypeInfo(
    string TypeId,
    string DocumentPath,
    string Name,
    bool IsPublic,
    bool IsAbstract,
    bool IsStatic,
    bool IsPartial,
    bool IsNested,
    bool IsInterface,
    bool IsInInheritanceChain);

/// <summary>
/// Internal member-cleanup query service.
/// </summary>
public interface IMemberCleanupQueryService
{
    MemberCleanupSymbolInfo? GetSymbolInfo(string symbolOrMemberId);

    MemberCleanupTypeInfo? GetTypeInfo(string typeId);

    bool HasAnyReferences(string symbolOrMemberId);

    bool HasInternalMethodReferences(MemberId memberId);

    bool HasExternalMethodReferences(MemberId memberId);

    IReadOnlyList<MemberId> GetReorderablePublicMethods(string typeId);
}

/// <summary>
/// 璇彞浜嬪疄璁板綍銆?
/// </summary>
public sealed record StatementFact(
    string TargetKey,
    MemberId MemberId,
    StatementKindRef StatementKind,
    IReadOnlyList<SymbolRef> DefinesSymbols,
    IReadOnlyList<SymbolRef> UsesSymbols,
    IReadOnlyList<MemberId> InvokedMemberIds,
    StatementScopeMode ScopeMode,
    string? ScopeId,
    string? ParentScopeId,
    int SpanStart,
    int SpanLength);

/// <summary>
/// 璇彞浜嬪疄绱㈠紩銆?
/// </summary>
public sealed record StatementFactsIndex(
    IReadOnlyDictionary<string, IReadOnlyList<StatementFact>> FactsByMemberId)
{
    /// <summary>
    /// 绌鸿鍙ヤ簨瀹炵储寮曞疄渚嬨€?
    /// </summary>
    public static StatementFactsIndex Empty { get; } =
        new(new Dictionary<string, IReadOnlyList<StatementFact>>(StringComparer.Ordinal));
}

/// <summary>
/// 灞€閮ㄨ鍙ヤ緷璧栧浘蹇収銆?
/// </summary>
public sealed record StatementGraphSnapshot(
    string SeedTargetKey,
    StatementScopeMode ScopeMode,
    MemberId BoundaryMemberId,
    IReadOnlyList<string> Nodes,
    IReadOnlyList<StatementDependencyEdge> Edges);

/// <summary>
/// 璇彞绾у垎鏋愭湇鍔°€?
/// </summary>
public interface IStatementAnalysisService
{
    StatementGraphSnapshot Analyze(PlanTarget seedTarget, StatementScopeMode scopeMode);
}

/// <summary>
/// 鍒嗘瀽瑙嗗浘璁板綍銆?
/// </summary>
/// <param name="Targets">鍒嗘瀽鐩爣鍒楄〃銆?/param>
/// <param name="Edges">鍒嗘瀽杈瑰垪琛ㄣ€?/param>
/// <param name="TypeGraph">绫诲瀷渚濊禆鍥俱€?/param>
/// <param name="FunctionGraph">鍑芥暟渚濊禆鍥俱€?/param>
/// <param name="StatementGraph">璇彞渚濊禆鍥俱€?/param>
public sealed record AnalysisResultModel(
    IReadOnlyList<AnalysisTarget> Targets,
    IReadOnlyList<AnalysisEdge> Edges,
    TypeDependencyGraph TypeGraph,
    FunctionDependencyGraph FunctionGraph,
    StatementDependencyGraph StatementGraph,
    StatementGraphMaterialization StatementGraphMaterialization,
    FunctionGraphMaterialization FunctionGraphMaterialization);

/// <summary>
/// 鍏ㄥ眬鍒嗘瀽浜嬪疄鐩綍銆?
/// </summary>
public sealed record AnalysisExecutionSnapshot(
    AnalysisResultModel View,
    FunctionIndex FunctionIndex,
    FunctionFactsIndex FunctionFacts,
    StatementFactsIndex StatementFacts);

public sealed record AnalysisEngineResult(
    AnalysisResultModel View,
    IReadOnlyList<AnalysisDocumentContext> Documents,
    AnalysisExecutionSnapshot Snapshot,
    AnalysisServices Services,
    AnalysisPerformanceSummary PerformanceSummary)
{
    public FunctionIndex FunctionIndex => Snapshot.FunctionIndex;

    public FunctionFactsIndex FunctionFacts => Snapshot.FunctionFacts;

    public AnalysisContext CreateContext() => AnalysisContext.Create(Snapshot, Services);
}

public interface IAnalysisEngine
{
    Task<AnalysisEngineResult> AnalyzeAsync(
        IReadOnlyList<SourceDocument> documents,
        CancellationToken cancellationToken);

    Task<AnalysisEngineResult> AnalyzeAsync(
        AnalysisInput input,
        CancellationToken cancellationToken);
}

public interface IRewriteExecutor
{
    Task<RewriteExecutionResult> ExecuteAsync(RewriteExecutionDocumentContext documentContext, AuditPlan plan, CancellationToken cancellationToken);
}

public interface IArtifactWriter
{
    Task WritePlanAsync(string path, AuditPlan plan, CancellationToken cancellationToken);

    Task WriteAnalysisAsync(string path, AnalysisResultModel view, CancellationToken cancellationToken);

    Task WriteReportAsync(string path, RunReport report, CancellationToken cancellationToken);
}

public interface IFunctionImpactAnalyzer
{
    FunctionImpactSet Analyze(
        AuditPlan plan,
        AnalysisServices services,
        FunctionGraphRequest request);

    FunctionImpactSet Analyze(AuditPlan plan, FunctionGraphSnapshot snapshot);
}

public interface IReferenceZeroPredictionAnalyzer
{
    IReadOnlyList<MarkDecision> Predict(
        AnalysisExecutionSnapshot snapshot,
        AnalysisServices services,
        RuleExecutionContext executionContext,
        IReadOnlyList<MarkDecision> decisions);

    IReadOnlyList<MarkDecision> Predict(AnalysisContext context, IReadOnlyList<MarkDecision> decisions);
}

/// <summary>
/// 鍒嗘瀽鏌ヨ鏈嶅姟闆嗗悎銆?
/// </summary>
public sealed record AnalysisServices(
    IInheritanceQueryService Inheritance,
    IReferenceQueryService References,
    IStatementAnalysisService Statements,
    IFunctionGraphProvider FunctionGraphs,
    ISymbolDependencyGraphProvider SymbolDependencies,
    IMethodCallQueryService MethodCalls,
    IDataFlowSummaryService DataFlow,
    ISwitchFlowSummaryService SwitchFlows,
    ICallChainAnalysisService CallChains,
    IAdvancedAnalysisSummaryService AdvancedAnalysis,
    IMemberCleanupQueryService MemberCleanup);

/// <summary>
/// 鍗曟瑙勫垯鎴栭娴嬫墽琛屼笂涓嬫枃銆?
/// </summary>
public sealed record RuleExecutionContext(
    string Requester,
    PlanTarget? SeedTarget,
    StatementScopeMode StatementScopeMode,
    CancellationToken CancellationToken,
    string? Reason = null);

/// <summary>
/// 鍒嗘瀽鐩爣璁板綍銆?
/// </summary>
/// <param name="Target">璁″垝鐩爣銆?/param>
/// <param name="IsHighRisk">鏄惁楂橀闄┿€?/param>
/// <param name="Directives">鎸囦护鍒楄〃銆?/param>
/// <param name="DefinesSymbols">瀹氫箟绗﹀彿鍒楄〃銆?/param>
/// <param name="UsesSymbols">浣跨敤绗﹀彿鍒楄〃銆?/param>
/// <param name="StatementKind">璇彞绫诲瀷銆?/param>
/// <param name="IsSanitizingAssignment">鏄惁涓哄噣鍖栬祴鍊笺€?/param>
/// <param name="IsObjectInitializerAssignment">鏄惁涓哄璞″垵濮嬪寲鍣ㄨ祴鍊笺€?/param>
/// <param name="HasMarkedExpressionSeed">鏄惁鏈夋爣璁扮殑琛ㄨ揪寮忕瀛愩€?/param>
/// <param name="MarkedExpressionKinds">鏍囪鐨勮〃杈惧紡绫诲瀷銆?/param>
public sealed record AnalysisTarget(
    PlanTarget Target,
    bool IsHighRisk,
    IReadOnlyList<DirectiveAction> Directives,
    IReadOnlyList<SymbolRef> DefinesSymbols,
    IReadOnlyList<SymbolRef> UsesSymbols,
    IReadOnlyList<MemberId> InvokedMemberIds,
    StatementKindRef StatementKind,
    bool IsSanitizingAssignment,
    bool IsObjectInitializerAssignment,
    bool HasMarkedExpressionSeed,
    IReadOnlyList<string> MarkedExpressionKinds,
    StatementScopeMode ScopeMode,
    string? ScopeId,
    string? ParentScopeId);

/// <summary>
/// 鍒嗘瀽杈圭被鍨嬫灇涓俱€?
/// </summary>
public enum AnalysisEdgeKind
{
    /// <summary>
    /// 瀹氫箟銆?
    /// </summary>
    Defines,
    /// <summary>
    /// 浣跨敤銆?
    /// </summary>
    Uses,
    /// <summary>
    /// 鍏堜簬銆?
    /// </summary>
    Precedes
}

/// <summary>
/// 绗﹀彿绫诲瀷寮曠敤鏋氫妇銆?
/// </summary>
public enum SymbolKindRef
{
    /// <summary>
    /// 鏈煡銆?
    /// </summary>
    Unknown,
    /// <summary>
    /// 灞€閮ㄥ彉閲忋€?
    /// </summary>
    Local,
    /// <summary>
    /// 鍙傛暟銆?
    /// </summary>
    Parameter,
    /// <summary>
    /// 瀛楁銆?
    /// </summary>
    Field,
    /// <summary>
    /// 灞炴€с€?
    /// </summary>
    Property
}

/// <summary>
/// 绗﹀彿寮曠敤璁板綍銆?
/// </summary>
/// <param name="SymbolKey">绗﹀彿閿€?/param>
/// <param name="DisplayName">鏄剧ず鍚嶇О銆?/param>
/// <param name="SymbolKind">绗﹀彿绫诲瀷銆?/param>
/// <param name="DeclaringMemberId">澹版槑鎴愬憳 ID銆?/param>
/// <param name="DeclarationSpanStart">澹版槑璺ㄥ害璧峰浣嶇疆銆?/param>
/// <param name="DeclarationSpanLength">澹版槑璺ㄥ害闀垮害銆?/param>
public sealed record SymbolRef(
    string SymbolKey,
    string DisplayName,
    SymbolKindRef SymbolKind,
    MemberId DeclaringMemberId,
    int DeclarationSpanStart,
    int DeclarationSpanLength);

/// <summary>
/// 鍒嗘瀽杈硅褰曘€?
/// </summary>
/// <param name="SourceTargetKey">婧愮洰鏍囬敭銆?/param>
/// <param name="TargetTargetKey">鐩爣鐩爣閿€?/param>
/// <param name="Kind">杈圭被鍨嬨€?/param>
/// <param name="SymbolKey">绗﹀彿閿€?/param>
public sealed record AnalysisEdge(
    string SourceTargetKey,
    string TargetTargetKey,
    AnalysisEdgeKind Kind,
    string? SymbolKey = null);

/// <summary>
/// 缁ф壙鏌ヨ鏈嶅姟鎺ュ彛銆?
/// </summary>
public interface IInheritanceQueryService
{
    /// <summary>
    /// 妫€鏌ユ垚鍛樻槸鍚︿负閲嶅啓鎴愬憳銆?
    /// </summary>
    bool IsOverrideMember(string memberId);
    /// <summary>
    /// 妫€鏌ユ垚鍛樻槸鍚﹀疄鐜版帴鍙ｆ垚鍛樸€?
    /// </summary>
    bool ImplementsInterfaceMember(string memberId);
    /// <summary>
    /// 妫€鏌ョ被鍨嬫槸鍚﹀湪缁ф壙閾句腑銆?
    /// </summary>
    bool IsInInheritanceChain(string typeId);
}

/// <summary>
/// 寮曠敤鏌ヨ鏈嶅姟鎺ュ彛銆?
/// </summary>
public interface IReferenceQueryService
{
    /// <summary>
    /// 妫€鏌ョ鍙锋垨鎴愬憳鏄惁鏈夊紩鐢ㄣ€?
    /// </summary>
    bool HasReferences(string symbolOrMemberId);
    /// <summary>
    /// 鑾峰彇寮曠敤璇ョ鍙风殑鍑芥暟鍒楄〃銆?
    /// </summary>
    IReadOnlyList<MemberId> GetReferencingFunctions(string symbolOrMemberId);
    /// <summary>
    /// 鑾峰彇寮曠敤璇ョ鍙风殑绫诲瀷鍒楄〃銆?
    /// </summary>
    IReadOnlyList<string> GetReferencingTypes(string symbolOrMemberId);
}

/// <summary>
/// 鎸囦护鎿嶄綔璁板綍銆?
/// </summary>
/// <param name="ActionKind">鎿嶄綔绫诲瀷銆?/param>
/// <param name="Payload">璐熻浇鏁版嵁銆?/param>
/// <param name="RuleId">瑙勫垯 ID銆?/param>
/// <param name="ReasonText">鍘熷洜鏂囨湰銆?/param>
public sealed record DirectiveAction(
    PlanActionKind ActionKind,
    string? Payload,
    string RuleId,
    string ReasonText);

/// <summary>
/// 宸ヤ綔鍖哄姞杞界粨鏋溿€?
/// </summary>
/// <param name="IsSuccess">鏄惁鎴愬姛銆?/param>
/// <param name="Documents">鍔犺浇鍚庣殑婧愮爜鏂囨。銆?/param>
/// <param name="LoadMode">瀹為檯鍔犺浇妯″紡銆?/param>
/// <param name="RequestedPrimaryLoader">璇锋眰鐨勪富鍔犺浇鍣ㄥ悕绉般€?/param>
/// <param name="FallbackUsed">鏄惁鍙戠敓浜嗗洖閫€銆?/param>
/// <param name="Diagnostics">鍔犺浇璇婃柇銆?/param>
public sealed record WorkspaceLoadResult(
    bool IsSuccess,
    AnalysisInput? AnalysisInput,
    IReadOnlyList<SourceDocument> Documents,
    WorkspaceLoadMode LoadMode,
    string RequestedPrimaryLoader,
    bool FallbackUsed,
    IReadOnlyList<WorkspaceLoadDiagnostic> Diagnostics)
{
    /// <summary>
    /// 鍒涘缓鎴愬姛鐨勫伐浣滃尯鍔犺浇缁撴灉銆?
    /// </summary>
    public static WorkspaceLoadResult Success(
        AnalysisInput analysisInput,
        WorkspaceLoadMode loadMode,
        string requestedPrimaryLoader,
        bool fallbackUsed = false,
        IReadOnlyList<WorkspaceLoadDiagnostic>? diagnostics = null) =>
        new(
            true,
            analysisInput,
            ExtractDocuments(analysisInput),
            loadMode,
            requestedPrimaryLoader,
            fallbackUsed,
            diagnostics ?? Array.Empty<WorkspaceLoadDiagnostic>());

    /// <summary>
    /// 鍩轰簬婧愮爜鏂囨。鍒涘缓鎴愬姛鐨勫伐浣滃尯鍔犺浇缁撴灉銆?
    /// </summary>
    public static WorkspaceLoadResult Success(
        IReadOnlyList<SourceDocument> documents,
        WorkspaceLoadMode loadMode,
        string requestedPrimaryLoader,
        bool fallbackUsed = false,
        IReadOnlyList<WorkspaceLoadDiagnostic>? diagnostics = null) =>
        new(
            true,
            new SourceOnlyAnalysisInput(ResolveRootPath(documents), documents),
            documents,
            loadMode,
            requestedPrimaryLoader,
            fallbackUsed,
            diagnostics ?? Array.Empty<WorkspaceLoadDiagnostic>());

    /// <summary>
    /// 鍒涘缓澶辫触鐨勫伐浣滃尯鍔犺浇缁撴灉銆?
    /// </summary>
    public static WorkspaceLoadResult Failure(
        WorkspaceLoadMode loadMode,
        string requestedPrimaryLoader,
        IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics) =>
        new(false, null, Array.Empty<SourceDocument>(), loadMode, requestedPrimaryLoader, false, diagnostics);

    /// <summary>
    /// 浠庡垎鏋愯緭鍏ユ彁鍙栨簮鐮佹枃妗ｉ泦鍚堛€?
    /// </summary>
    private static IReadOnlyList<SourceDocument> ExtractDocuments(AnalysisInput analysisInput)
    {
        return analysisInput switch
        {
            SourceOnlyAnalysisInput sourceOnly => sourceOnly.Documents,
            WorkspaceAnalysisContextInput workspace => workspace.Documents
                .Select(document => document.SourceDocument)
                .ToArray(),
            _ => Array.Empty<SourceDocument>()
        };
    }

    /// <summary>
    /// 瑙ｆ瀽婧愮爜鏂囨。闆嗗悎瀵瑰簲鐨勬牴璺緞銆?
    /// </summary>
    private static string ResolveRootPath(IReadOnlyList<SourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            return string.Empty;
        }

        if (documents.Count == 1)
        {
            return Path.GetDirectoryName(documents[0].SourcePath) ?? string.Empty;
        }

        return Path.GetDirectoryName(documents[0].SourcePath) ?? string.Empty;
    }
}

/// <summary>
/// 璁″垝缂栬瘧缁撴灉璁板綍銆?
/// </summary>
/// <param name="IsSuccess">鏄惁鎴愬姛銆?/param>
/// <param name="Plan">瀹¤璁″垝銆?/param>
/// <param name="FailureCode">澶辫触浠ｇ爜銆?/param>
/// <param name="Conflicts">鍐茬獊鍒楄〃銆?/param>
/// <param name="Message">娑堟伅銆?/param>
public sealed record PlanCompilationResult(
    bool IsSuccess,
    AuditPlan? Plan,
    FailureCode FailureCode,
    IReadOnlyList<PlanConflict> Conflicts,
    string? Message)
{
    /// <summary>
    /// 鍒涘缓鎴愬姛缁撴灉銆?
    /// </summary>
    public static PlanCompilationResult Success(AuditPlan plan) =>
        new(true, plan, FailureCode.None, Array.Empty<PlanConflict>(), null);

    /// <summary>
    /// 鍒涘缓澶辫触缁撴灉銆?
    /// </summary>
    public static PlanCompilationResult Failure(string? message, IReadOnlyList<PlanConflict> conflicts) =>
        new(false, null, FailureCode.PlanCompileFailed, conflicts, message);
}

/// <summary>
/// 閲嶅啓鎵ц缁撴灉璁板綍銆?
/// </summary>
/// <param name="IsSuccess">鏄惁鎴愬姛銆?/param>
/// <param name="FailureCode">澶辫触浠ｇ爜銆?/param>
/// <param name="RewrittenSource">閲嶅啓鍚庣殑婧愪唬鐮併€?/param>
/// <param name="Message">娑堟伅銆?/param>
public sealed record RewriteExecutionResult(
    bool IsSuccess,
    FailureCode FailureCode,
    string? RewrittenSource,
    string? Message)
{
    /// <summary>
    /// 鍒涘缓鎴愬姛缁撴灉銆?
    /// </summary>
    public static RewriteExecutionResult Success(string rewrittenSource) =>
        new(true, FailureCode.None, rewrittenSource, null);

    /// <summary>
    /// 鍒涘缓澶辫触缁撴灉銆?
    /// </summary>
    public static RewriteExecutionResult Failure(string? message) =>
        new(false, FailureCode.RewriteFailed, null, message);
}

/// <summary>
/// 澶辫触鎽樿璁板綍銆?
/// </summary>
/// <param name="FailureCode">澶辫触浠ｇ爜銆?/param>
/// <param name="Message">娑堟伅銆?/param>
public sealed record FailureSummary(
    FailureCode FailureCode,
    string Message);

/// <summary>
/// 鍐茬獊鎽樿璁板綍銆?
/// </summary>
/// <param name="ConflictCode">鍐茬獊浠ｇ爜銆?/param>
/// <param name="TargetKey">鐩爣閿€?/param>
/// <param name="TargetDisplayText">鐩爣鏄剧ず鏂囨湰銆?/param>
/// <param name="ActionKinds">鎿嶄綔绫诲瀷鍒楄〃銆?/param>
/// <param name="Reason">鍘熷洜銆?/param>
public sealed record ConflictSummary(
    string ConflictCode,
    string TargetKey,
    string TargetDisplayText,
    IReadOnlyList<PlanActionKind> ActionKinds,
    string Reason);

/// <summary>
/// 椋庨櫓鎽樿璁板綍銆?
/// </summary>
/// <param name="SkippedHighRiskTargetCount">璺宠繃鐨勯珮椋庨櫓鐩爣鏁伴噺銆?/param>
/// <param name="SampleTargetDisplayTexts">绀轰緥鐩爣鏄剧ず鏂囨湰鍒楄〃銆?/param>
public sealed record RiskSummary(
    int SkippedHighRiskTargetCount,
    IReadOnlyList<string> SampleTargetDisplayTexts);

/// <summary>
/// 璁″垝瑕嗙洊鐜囨憳瑕佽褰曘€?
/// </summary>
/// <param name="CoveredMethodCount">瑕嗙洊鐨勬柟娉曟暟閲忋€?/param>
/// <param name="CoveredStatementCount">瑕嗙洊鐨勮鍙ユ暟閲忋€?/param>
/// <param name="SampleCoveredTargetDisplayTexts">绀轰緥瑕嗙洊鐩爣鏄剧ず鏂囨湰鍒楄〃銆?/param>
public sealed record PlanCoverageSummary(
    int CoveredMethodCount,
    int CoveredStatementCount,
    IReadOnlyList<string> SampleCoveredTargetDisplayTexts);

/// <summary>
/// 鍑芥暟鍒犻櫎褰卞搷鎽樿銆?
/// </summary>
public sealed record FunctionImpactSummary(
    int DeletedFunctionCount,
    int AffectedFunctionCount,
    int AffectedDocumentCount,
    int ExpansionDepth,
    IReadOnlyList<FunctionDependencyKind> EdgeKinds,
    IReadOnlyList<string> SampleAffectedFunctionIds,
    IReadOnlyList<string> SampleAffectedDocumentPaths);

/// <summary>
/// 寮曠敤褰掗浂棰勬祴鎽樿銆?
/// </summary>
public sealed record ReferenceZeroPredictionSummary(
    int PredictedMethodDeleteCount,
    IReadOnlyList<string> SamplePredictedMethodIds);

/// <summary>
/// 杈圭晫鎻愬崌鎽樿銆?
/// </summary>
public sealed record BoundaryPromotionSummary(
    BoundaryKind BoundaryKind,
    int PromotedMethodDeleteCount,
    IReadOnlyList<string> SamplePromotedMethodIds);

/// <summary>
/// 杩愯鎶ュ憡璁板綍銆?
/// </summary>
/// <param name="IsSuccess">鏄惁鎴愬姛銆?/param>
/// <param name="FailureCode">澶辫触浠ｇ爜銆?/param>
/// <param name="AnalysisTargets">鍒嗘瀽鐩爣鏁伴噺銆?/param>
/// <param name="PlannedChanges">璁″垝鍙樻洿鏁伴噺銆?/param>
/// <param name="Conflicts">鍐茬獊鏁伴噺銆?/param>
/// <param name="RewrittenDocuments">閲嶅啓鏂囨。鏁伴噺銆?/param>
/// <param name="GeneratedArtifacts">鐢熸垚鐨勫埗鍝佸垪琛ㄣ€?/param>
/// <param name="FailureSummary">澶辫触鎽樿銆?/param>
/// <param name="ConflictSummaries">鍐茬獊鎽樿鍒楄〃銆?/param>
/// <param name="RiskSummary">椋庨櫓鎽樿銆?/param>
/// <param name="PlanCoverageSummary">璁″垝瑕嗙洊鐜囨憳瑕併€?/param>
/// <param name="Message">娑堟伅銆?/param>
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
    /// <summary>
    /// 楂樺眰鍒嗘瀽鎽樿銆?    /// </summary>
    public AdvancedAnalysisSummary? AdvancedAnalysisSummary { get; init; }

    /// <summary>
    /// TR 鏋勫缓鎽樿淇℃伅銆?    /// </summary>
    public TerrariaRuntimeBuildSummary? TrBuildSummary { get; init; }
}
