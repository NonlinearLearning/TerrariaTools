namespace TerrariaTools.Dome.Application;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ApplicationAbstractions = TerrariaTools.Dome.Application.Abstractions;
using ModelAnalysis = TerrariaTools.Dome.Model.Analysis;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using TerrariaTools.Dome.Reporting;

/// <summary>
/// 闃舵鎵ц缁撴灉銆?/// </summary>
/// <typeparam name="T">闃舵杩斿洖鍊肩被鍨嬨€?/typeparam>
public sealed record StageResult<T>(
    bool IsSuccess,
    T? Value,
    ModelPrimitives.FailureCode FailureCode,
    string? Message)
{
    /// <summary>
    /// 鍒涘缓鎴愬姛缁撴灉銆?    /// </summary>
    /// <param name="value">鎴愬姛杩斿洖鍊笺€?/param>
    /// <returns>鎴愬姛鐨勯樁娈电粨鏋溿€?/returns>
    public static StageResult<T> Success(T value) => new(true, value, ModelPrimitives.FailureCode.None, null);

    /// <summary>
    /// 鍒涘缓澶辫触缁撴灉銆?    /// </summary>
    /// <param name="failureCode">澶辫触浠ｇ爜銆?/param>
    /// <param name="message">澶辫触娑堟伅銆?/param>
    /// <returns>澶辫触鐨勯樁娈电粨鏋溿€?/returns>
    public static StageResult<T> Failure(ModelPrimitives.FailureCode failureCode, string message) => new(false, default, failureCode, message);
}

/// <summary>
/// Terraria 杩愯鏃跺竷灞€宸ュ巶銆?/// </summary>
public interface ITerrariaRuntimeLayoutFactory
{
    /// <summary>
    /// 鏍规嵁璇锋眰鍒涘缓杩愯鏃跺竷灞€銆?    /// </summary>
    /// <param name="request">杩愯璇锋眰銆?/param>
    /// <returns>杩愯鏃跺竷灞€銆?/returns>
    ApplicationAbstractions.TerrariaRuntimeLayout Create(ApplicationAbstractions.TerrariaRuntimeRunRequest request);
}

/// <summary>
/// Terraria 杩愯鏃跺竷灞€宸ュ巶瀹炵幇銆?/// </summary>
public sealed class TerrariaRuntimeLayoutFactory : ITerrariaRuntimeLayoutFactory
{
    /// <summary>
    /// 鏍规嵁璇锋眰鍒涘缓杩愯鏃跺竷灞€銆?    /// </summary>
    /// <param name="request">杩愯璇锋眰銆?/param>
    /// <returns>杩愯鏃跺竷灞€銆?/returns>
    public ApplicationAbstractions.TerrariaRuntimeLayout Create(ApplicationAbstractions.TerrariaRuntimeRunRequest request) => ApplicationAbstractions.TerrariaRuntimeLayout.Create(request);
}

/// <summary>
/// Terraria 杩愯鏃跺伐浣滃尯棰勫鐞嗗櫒銆?/// </summary>
public interface ITerrariaRuntimeWorkspacePreparer
{
    /// <summary>
    /// 纭繚杈撳嚭鐩綍瀛樺湪銆?    /// </summary>
    /// <param name="layout">杩愯鏃跺竷灞€銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    Task EnsureOutputDirectoriesAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, CancellationToken cancellationToken);

    /// <summary>
    /// 鍒锋柊渚濊禆鐜銆?    /// </summary>
    /// <param name="layout">杩愯鏃跺竷灞€銆?/param>
    /// <param name="progressReporter">杩涘害涓婃姤鍣ㄣ€?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    Task RefreshDependencyEnvironmentAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken);

    /// <summary>
    /// 鍑嗗宸ヤ綔鍖恒€?    /// </summary>
    /// <param name="layout">杩愯鏃跺竷灞€銆?/param>
    /// <param name="progressReporter">杩涘害涓婃姤鍣ㄣ€?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    Task PrepareWorkspaceAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, ITerrariaRuntimeProgressReporter progressReporter, CancellationToken cancellationToken);
}

/// <summary>
/// 杩愯鎶ュ憡瀛樺偍鎺ュ彛銆?/// </summary>
public interface IRunReportStore
{
    /// <summary>
    /// 鍔犺浇杩愯鎶ュ憡銆?    /// </summary>
    /// <param name="path">鎶ュ憡璺緞銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>鍔犺浇缁撴灉銆?/returns>
    Task<StageResult<ApplicationAbstractions.RunReport>> LoadAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// 淇濆瓨杩愯鎶ュ憡銆?    /// </summary>
    /// <param name="path">鎶ュ憡璺緞銆?/param>
    /// <param name="report">杩愯鎶ュ憡銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    Task SaveAsync(string path, ApplicationAbstractions.RunReport report, CancellationToken cancellationToken);
}

/// <summary>
/// 鍩轰簬 JSON 鐨勮繍琛屾姤鍛婂瓨鍌ㄥ疄鐜般€?/// </summary>
public sealed class JsonRunReportStore(JsonArtifactWriter artifactWriter) : IRunReportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 浠庢寚瀹氳矾寰勫姞杞借繍琛屾姤鍛娿€?    /// </summary>
    /// <param name="path">鎶ュ憡璺緞銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>鍔犺浇缁撴灉銆?/returns>
    public async Task<StageResult<ApplicationAbstractions.RunReport>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return StageResult<ApplicationAbstractions.RunReport>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, $"Run report '{path}' was not found.");
        }

        try
        {
            var reportJson = await File.ReadAllTextAsync(path, cancellationToken);
            var report = JsonSerializer.Deserialize<ApplicationAbstractions.RunReport>(reportJson, JsonOptions);
            return report == null
                ? StageResult<ApplicationAbstractions.RunReport>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, "report.json could not be deserialized.")
                : StageResult<ApplicationAbstractions.RunReport>.Success(report);
        }
        catch (Exception ex)
        {
            return StageResult<ApplicationAbstractions.RunReport>.Failure(ModelPrimitives.FailureCode.AnalysisFailed, ex.Message);
        }
    }

    /// <summary>
    /// 灏嗚繍琛屾姤鍛婁繚瀛樺埌鎸囧畾璺緞銆?    /// </summary>
    /// <param name="path">鎶ュ憡璺緞銆?/param>
    /// <param name="report">杩愯鎶ュ憡銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    public async Task SaveAsync(string path, ApplicationAbstractions.RunReport report, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken);
    }

}

/// <summary>
/// 褰卞瓙鎻愬彇杈撳叆瑙ｆ瀽鍣ㄣ€?/// </summary>
public interface IShadowExtractionInputResolver
{
    /// <summary>
    /// 瑙ｆ瀽褰卞瓙鎻愬彇杈撳叆銆?    /// </summary>
    /// <param name="request">褰卞瓙鎻愬彇璇锋眰銆?/param>
    /// <param name="progressReporter">杩涘害涓婃姤鍣ㄣ€?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>杈撳叆瑙ｆ瀽缁撴灉銆?/returns>
    Task<StageResult<ShadowExtractionInputResolution>> ResolveAsync(
        ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 褰卞瓙鎻愬彇鍒嗘瀽闃舵銆?/// </summary>
public interface IShadowExtractionAnalysisStage
{
    /// <summary>
    /// 鎵ц褰卞瓙鎻愬彇鍒嗘瀽銆?    /// </summary>
    /// <param name="input">杈撳叆瑙ｆ瀽缁撴灉銆?/param>
    /// <param name="progressReporter">杩涘害涓婃姤鍣ㄣ€?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>鍒嗘瀽缁撴灉銆?/returns>
    Task<StageResult<ShadowExtractionAnalysis>> AnalyzeAsync(
        ShadowExtractionInputResolution input,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 褰卞瓙闂寘瑙勫垝鍣ㄣ€?/// </summary>
public interface IShadowClosurePlanner
{
    /// <summary>
    /// 鏋勫缓褰卞瓙闂寘璁″垝銆?    /// </summary>
    /// <param name="analysis">褰卞瓙鎻愬彇鍒嗘瀽缁撴灉銆?/param>
    /// <param name="progressReporter">杩涘害涓婃姤鍣ㄣ€?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>闂寘璁″垝缁撴灉銆?/returns>
    StageResult<ShadowClosurePlan> BuildPlan(
        ShadowExtractionAnalysis analysis,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 褰卞瓙宸ヤ綔鍖哄啓鍏ュ櫒銆?/// </summary>
public interface IShadowWorkspaceWriter
{
    /// <summary>
    /// 鍐欏叆褰卞瓙宸ヤ綔鍖哄唴瀹广€?    /// </summary>
    /// <param name="input">杈撳叆瑙ｆ瀽缁撴灉銆?/param>
    /// <param name="analysis">褰卞瓙鎻愬彇鍒嗘瀽缁撴灉銆?/param>
    /// <param name="closurePlan">褰卞瓙闂寘璁″垝銆?/param>
    /// <param name="progressReporter">杩涘害涓婃姤鍣ㄣ€?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    /// <returns>鍐欏叆缁撴灉銆?/returns>
    Task<StageResult<ShadowWorkspaceWriteResult>> WriteAsync(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 褰卞瓙鎻愬彇鎶ュ憡鏋勫缓鍣ㄣ€?/// </summary>
public interface IShadowExtractionReportBuilder
{
    /// <summary>
    /// 鏋勫缓褰卞瓙鎻愬彇鎶ュ憡銆?    /// </summary>
    /// <param name="input">杈撳叆瑙ｆ瀽缁撴灉銆?/param>
    /// <param name="analysis">褰卞瓙鎻愬彇鍒嗘瀽缁撴灉銆?/param>
    /// <param name="closurePlan">褰卞瓙闂寘璁″垝銆?/param>
    /// <param name="workspaceWriteResult">宸ヤ綔鍖哄啓鍏ョ粨鏋溿€?/param>
    /// <returns>褰卞瓙鎻愬彇鎶ュ憡銆?/returns>
    ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport Build(
        ShadowExtractionInputResolution input,
        ShadowExtractionAnalysis analysis,
        ShadowClosurePlan closurePlan,
        ShadowWorkspaceWriteResult workspaceWriteResult);
}

/// <summary>
/// 褰卞瓙鎻愬彇鎶ュ憡瀛樺偍鎺ュ彛銆?/// </summary>
public interface IShadowExtractionReportStore
{
    /// <summary>
    /// 淇濆瓨褰卞瓙鎻愬彇鎶ュ憡銆?    /// </summary>
    /// <param name="path">鎶ュ憡璺緞銆?/param>
    /// <param name="report">褰卞瓙鎻愬彇鎶ュ憡銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    Task SaveAsync(string path, ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport report, CancellationToken cancellationToken);
}

/// <summary>
/// 鍩轰簬 JSON 鐨勫奖瀛愭彁鍙栨姤鍛婂瓨鍌ㄥ疄鐜般€?/// </summary>
public sealed class JsonShadowExtractionReportStore(JsonArtifactWriter artifactWriter) : IShadowExtractionReportStore
{
    /// <summary>
    /// 灏嗗奖瀛愭彁鍙栨姤鍛婁繚瀛樺埌鎸囧畾璺緞銆?    /// </summary>
    /// <param name="path">鎶ュ憡璺緞銆?/param>
    /// <param name="report">褰卞瓙鎻愬彇鎶ュ憡銆?/param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝銆?/param>
    public Task SaveAsync(string path, ApplicationAbstractions.TerrariaRuntimeShadowExtractionReport report, CancellationToken cancellationToken) =>
        artifactWriter.WriteJsonAsync(path, report, cancellationToken);
}

/// <summary>
/// 褰卞瓙鎻愬彇杈撳叆瑙ｆ瀽缁撴灉銆?/// </summary>
public sealed record ShadowExtractionInputResolution(
    ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest Request,
    ApplicationAbstractions.TerrariaRuntimeShadowLayout Layout,
    ApplicationAbstractions.WorkspaceLoadResult LoadResult);

public sealed record ShadowExtractionAnalysisDocument(
    ApplicationAbstractions.SourceDocument Document,
    CompilationUnitSyntax Root,
    SemanticModel SemanticModel);

/// <summary>
/// 褰卞瓙鎻愬彇鍒嗘瀽缁撴灉銆?/// </summary>
public sealed record ShadowExtractionAnalysis(
    ShadowExtractionInputResolution Input,
    ApplicationAbstractions.AnalysisEngineResult AnalysisResult,
    ModelAnalysis.AnalysisContext AnalysisContext,
    ModelAnalysis.FunctionNodeRef SeedNode,
    IReadOnlyList<ShadowExtractionAnalysisDocument> Documents);

/// <summary>
/// 褰卞瓙闂寘璁″垝銆?/// </summary>
public sealed record ShadowClosurePlan(
    IReadOnlyList<string> IncludedDocuments,
    IReadOnlyList<ModelPrimitives.MemberId> ReachableMethods,
    IReadOnlyDictionary<string, IReadOnlySet<string>> MemberIdsByDocument,
    int SymbolClosureDocumentCount);

/// <summary>
/// 褰卞瓙宸ヤ綔鍖哄啓鍏ョ粨鏋溿€?/// </summary>
public sealed record ShadowWorkspaceWriteResult(
    IReadOnlyDictionary<string, string> RewrittenDocuments,
    ApplicationAbstractions.TerrariaRuntimeShadowRewriteSummary RewriteSummary);
