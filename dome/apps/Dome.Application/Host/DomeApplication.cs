namespace TerrariaTools.Dome.Application.Host;

using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Pipeline;

/// <summary>
/// 定义标准 Dome 工作流的宿主运行契约。
/// </summary>
public interface IDomeApplicationRunner
{
    /// <summary>
    /// 执行一次标准 Dome 工作流。
    /// </summary>
    /// <param name="request">运行请求。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>运行结果。</returns>
    Task<ModelExecution.RunResult> RunAsync(ApplicationAbstractions.RunRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// 为标准 Dome 流水线提供宿主层入口封装。
/// </summary>
public sealed class DomeApplication : IDomeApplicationRunner
{
    private readonly IPipelineRunner<DomePipelineContext> _pipelineRunner;

    /// <summary>
    /// 初始化 Dome 应用宿主。
    /// </summary>
    /// <param name="pipelineRunner">标准流水线运行器。</param>
    public DomeApplication(IPipelineRunner<DomePipelineContext> pipelineRunner)
    {
        _pipelineRunner = pipelineRunner;
    }

    /// <summary>
    /// 执行一次标准 Dome 工作流。
    /// </summary>
    /// <param name="request">运行请求。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>运行结果。</returns>
    public async Task<ModelExecution.RunResult> RunAsync(ApplicationAbstractions.RunRequest request, CancellationToken cancellationToken)
    {
        var context = new DomePipelineContext(request);
        await _pipelineRunner.RunAsync(context, cancellationToken);
        if (context.TerminalState == null)
        {
            throw new InvalidOperationException("Dome pipeline completed without producing a terminal result.");
        }

        return context.TerminalState.Result;
    }
}

