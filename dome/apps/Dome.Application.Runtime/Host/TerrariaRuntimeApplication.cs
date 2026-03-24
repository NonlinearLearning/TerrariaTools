namespace TerrariaTools.Dome.Application.Runtime.Host;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 定义 Terraria 运行时工作流的宿主运行契约。
/// </summary>
public interface ITerrariaRuntimeApplicationRunner
{
    /// <summary>
    /// 执行一次 Terraria 运行时工作流。
    /// </summary>
    /// <param name="request">运行时请求。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>运行结果。</returns>
    Task<ModelExecution.RunResult> RunAsync(
        ApplicationAbstractions.TerrariaRuntimeRunRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// 为运行时流水线提供宿主层入口封装。
/// </summary>
public sealed class TerrariaRuntimeApplication : ITerrariaRuntimeApplicationRunner
{
    private readonly IPipelineRunner<TerrariaRuntimePipelineContext> _pipelineRunner;

    /// <summary>
    /// 初始化运行时应用宿主。
    /// </summary>
    /// <param name="pipelineRunner">运行时流水线运行器。</param>
    internal TerrariaRuntimeApplication(IPipelineRunner<TerrariaRuntimePipelineContext> pipelineRunner)
    {
        _pipelineRunner = pipelineRunner;
    }

    /// <summary>
    /// 执行一次 Terraria 运行时工作流。
    /// </summary>
    /// <param name="request">运行时请求。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>运行结果。</returns>
    public Task<ModelExecution.RunResult> RunAsync(
        ApplicationAbstractions.TerrariaRuntimeRunRequest request,
        CancellationToken cancellationToken) =>
        RunApplicationAsync(request, cancellationToken);

    /// <summary>
    /// 创建上下文并驱动运行时流水线执行。
    /// </summary>
    /// <param name="request">运行时请求。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>运行结果。</returns>
    public async Task<ModelExecution.RunResult> RunApplicationAsync(ApplicationAbstractions.TerrariaRuntimeRunRequest request, CancellationToken cancellationToken)
    {
        var context = new TerrariaRuntimePipelineContext(request);
        await _pipelineRunner.RunAsync(context, cancellationToken);
        if (context.TerminalState == null)
        {
            throw new InvalidOperationException("Terraria runtime pipeline completed without producing a terminal result.");
        }

        return context.TerminalState.Result;
    }
}








