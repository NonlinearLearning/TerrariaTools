namespace TerrariaTools.Dome.Application.UseCases.Runtime;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// 定义运行时工作区布局的创建能力。
/// </summary>
public interface ITerrariaRuntimeLayoutFactory
{
    /// <summary>
    /// 根据运行请求创建工作区布局。
    /// </summary>
    /// <param name="request">运行请求。</param>
    /// <returns>运行时工作区布局。</returns>
    ApplicationAbstractions.TerrariaRuntimeLayout Create(ApplicationAbstractions.TerrariaRuntimeRunRequest request);
}

/// <summary>
/// 定义运行时工作区准备能力。
/// </summary>
public interface ITerrariaRuntimeWorkspacePreparer
{
    /// <summary>
    /// 确保输出目录结构已经准备就绪。
    /// </summary>
    /// <param name="layout">运行时工作区布局。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task EnsureOutputDirectoriesAsync(ApplicationAbstractions.TerrariaRuntimeLayout layout, CancellationToken cancellationToken);

    /// <summary>
    /// 刷新运行时所需的依赖环境目录。
    /// </summary>
    /// <param name="layout">运行时工作区布局。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task RefreshDependencyEnvironmentAsync(
        ApplicationAbstractions.TerrariaRuntimeLayout layout,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);

    /// <summary>
    /// 准备运行时工作区内容。
    /// </summary>
    /// <param name="layout">运行时工作区布局。</param>
    /// <param name="progressReporter">进度上报器。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task PrepareWorkspaceAsync(
        ApplicationAbstractions.TerrariaRuntimeLayout layout,
        ITerrariaRuntimeProgressReporter progressReporter,
        CancellationToken cancellationToken);
}

/// <summary>
/// 定义运行报告的读写能力。
/// </summary>
public interface IRunReportStore
{
    /// <summary>
    /// 从指定路径读取运行报告。
    /// </summary>
    /// <param name="path">报告路径。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>读取结果。</returns>
    Task<StageResult<ModelExecution.RunReport>> LoadAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// 将运行报告保存到指定路径。
    /// </summary>
    /// <param name="path">报告路径。</param>
    /// <param name="report">待保存的报告。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    Task SaveAsync(string path, ModelExecution.RunReport report, CancellationToken cancellationToken);
}
