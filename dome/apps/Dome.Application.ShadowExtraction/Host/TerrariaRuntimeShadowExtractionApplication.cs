namespace TerrariaTools.Dome.Application.ShadowExtraction.Host;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// 定义影子提取工作流的宿主运行契约。
/// </summary>
public interface ITerrariaRuntimeShadowExtractionApplicationRunner
{
    /// <summary>
    /// 执行一次影子提取工作流。
    /// </summary>
    /// <param name="request">影子提取请求。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>运行结果。</returns>
    Task<ModelExecution.RunResult> RunAsync(
        ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// 为影子提取流水线提供宿主层入口封装。
/// </summary>
public sealed class TerrariaRuntimeShadowExtractionApplication : ITerrariaRuntimeShadowExtractionApplicationRunner
{
    private readonly IPipelineRunner<ShadowExtractionPipelineContext> _pipelineRunner;

    /// <summary>
    /// 初始化影子提取应用宿主。
    /// </summary>
    /// <param name="pipelineRunner">影子提取流水线运行器。</param>
    internal TerrariaRuntimeShadowExtractionApplication(IPipelineRunner<ShadowExtractionPipelineContext> pipelineRunner)
    {
        _pipelineRunner = pipelineRunner;
    }

    /// <summary>
    /// 执行一次影子提取工作流。
    /// </summary>
    /// <param name="request">影子提取请求。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>运行结果。</returns>
    public Task<ModelExecution.RunResult> RunAsync(
        ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request,
        CancellationToken cancellationToken) =>
        RunApplicationAsync(request, cancellationToken);

    /// <summary>
    /// 创建上下文并驱动影子提取流水线执行。
    /// </summary>
    /// <param name="request">影子提取请求。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>运行结果。</returns>
    public async Task<ModelExecution.RunResult> RunApplicationAsync(ApplicationAbstractions.TerrariaRuntimeShadowExtractionRequest request, CancellationToken cancellationToken)
    {
        var context = new ShadowExtractionPipelineContext(request);
        await _pipelineRunner.RunAsync(context, cancellationToken);
        if (context.TerminalState == null)
        {
            throw new InvalidOperationException("Shadow extraction pipeline completed without producing a terminal result.");
        }

        return context.TerminalState.Result;
    }
}








