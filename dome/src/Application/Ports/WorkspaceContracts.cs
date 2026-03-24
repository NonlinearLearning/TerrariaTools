using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;

namespace TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 指定优先尝试的工作区加载器。
/// </summary>
public enum WorkspaceLoaderPreference
{
    /// <summary>
    /// 由加载器自行选择最合适的实现。
    /// </summary>
    Auto,

    /// <summary>
    /// 优先使用基于代码分析的加载器。
    /// </summary>
    CodeAnalysisFirst,

    /// <summary>
    /// 直接使用源码模式加载。
    /// </summary>
    SourceOnly
}

/// <summary>
/// 描述工作区加载阶段的策略选项。
/// </summary>
/// <param name="PreferredLoader">优先尝试的加载器类型。</param>
/// <param name="AllowFallbackToSourceOnly">失败时是否允许回退到源码模式。</param>
public sealed record WorkspaceLoadOptions(
    WorkspaceLoaderPreference PreferredLoader,
    bool AllowFallbackToSourceOnly)
{
    /// <summary>
    /// 获取默认的工作区加载选项。
    /// </summary>
    public static WorkspaceLoadOptions Default { get; } = new(WorkspaceLoaderPreference.Auto, true);
}

/// <summary>
/// 描述工作区加载过程中的单条诊断信息。
/// </summary>
/// <param name="Stage">产生诊断的阶段名称。</param>
/// <param name="Severity">诊断严重级别。</param>
/// <param name="Message">可供展示的诊断消息。</param>
public sealed record WorkspaceLoadDiagnostic(
    string Stage,
    WorkspaceLoadDiagnosticSeverity Severity,
    string Message);

/// <summary>
/// 封装工作区加载结果及其诊断信息。
/// </summary>
/// <param name="IsSuccess">指示加载是否成功。</param>
/// <param name="Input">成功时生成的分析输入。</param>
/// <param name="LoadMode">实际使用的加载模式。</param>
/// <param name="RequestedPrimaryLoader">调用方请求的主加载器名称。</param>
/// <param name="FallbackUsed">指示是否发生了加载器回退。</param>
/// <param name="Diagnostics">加载过程中的诊断集合。</param>
public sealed record WorkspaceLoadResult(
    bool IsSuccess,
    CoreAnalysis.AnalysisInput? Input,
    WorkspaceLoadMode LoadMode,
    string RequestedPrimaryLoader,
    bool FallbackUsed,
    IReadOnlyList<WorkspaceLoadDiagnostic> Diagnostics)
{
    /// <summary>
    /// 获取当前输入中携带的源码文档集合。
    /// </summary>
    public IReadOnlyList<CoreAnalysis.SourceDocument> Documents => Input?.SourceSet.Documents ?? Array.Empty<CoreAnalysis.SourceDocument>();

    /// <summary>
    /// 创建一个成功的工作区加载结果。
    /// </summary>
    /// <param name="input">生成的分析输入。</param>
    /// <param name="loadMode">实际使用的加载模式。</param>
    /// <param name="requestedPrimaryLoader">调用方请求的主加载器名称。</param>
    /// <param name="fallbackUsed">指示是否使用了回退路径。</param>
    /// <param name="diagnostics">可选的加载诊断。</param>
    /// <returns>表示成功的加载结果。</returns>
    public static WorkspaceLoadResult Success(
        CoreAnalysis.AnalysisInput input,
        WorkspaceLoadMode loadMode,
        string requestedPrimaryLoader,
        bool fallbackUsed = false,
        IReadOnlyList<WorkspaceLoadDiagnostic>? diagnostics = null) =>
        new(true, input, loadMode, requestedPrimaryLoader, fallbackUsed, diagnostics ?? Array.Empty<WorkspaceLoadDiagnostic>());

    /// <summary>
    /// 创建一个失败的工作区加载结果。
    /// </summary>
    /// <param name="loadMode">尝试使用的加载模式。</param>
    /// <param name="requestedPrimaryLoader">调用方请求的主加载器名称。</param>
    /// <param name="diagnostics">导致失败的诊断集合。</param>
    /// <returns>表示失败的加载结果。</returns>
    public static WorkspaceLoadResult Failure(
        WorkspaceLoadMode loadMode,
        string requestedPrimaryLoader,
        IReadOnlyList<WorkspaceLoadDiagnostic> diagnostics) =>
        new(false, null, loadMode, requestedPrimaryLoader, false, diagnostics);
}

/// <summary>
/// 定义从输入路径加载分析工作区的能力。
/// </summary>
public interface IWorkspaceLoader
{
    /// <summary>
    /// 按指定选项加载工作区输入。
    /// </summary>
    /// <param name="inputPath">待加载的输入路径。</param>
    /// <param name="options">加载选项。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>加载结果及诊断信息。</returns>
    Task<WorkspaceLoadResult> LoadAsync(string inputPath, WorkspaceLoadOptions options, CancellationToken cancellationToken);
}
