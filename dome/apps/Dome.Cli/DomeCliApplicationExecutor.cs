using ApplicationAbstractions = TerrariaTools.Dome.Application.Ports;
using ModelExecution = TerrariaTools.Dome.Application.Ports;
using TerrariaTools.Dome.Application.Host;
using TerrariaTools.Dome.Application.Runtime.Host;
using TerrariaTools.Dome.Application.ShadowExtraction.Host;

namespace TerrariaTools.Dome.Cli;

/// <summary>
/// 按命令类型分发 CLI 调用到对应的应用入口。
/// </summary>
/// <param name="standardRunner">标准 Dome 工作流入口。</param>
/// <param name="runtimeRunner">运行时工作流入口。</param>
/// <param name="shadowRunner">影子提取工作流入口。</param>
public sealed class DomeCliApplicationExecutor(
    IDomeApplicationRunner standardRunner,
    ITerrariaRuntimeApplicationRunner runtimeRunner,
    ITerrariaRuntimeShadowExtractionApplicationRunner shadowRunner)
{
    /// <summary>
    /// 执行一次 CLI 调用。
    /// </summary>
    /// <param name="invocation">已解析的 CLI 调用。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>运行结果。</returns>
    public Task<ModelExecution.RunResult> RunAsync(DomeCliInvocation invocation, CancellationToken cancellationToken) =>
        invocation.Kind switch
        {
            DomeCliCommandKind.Standard => standardRunner.RunAsync(
                invocation.StandardRequest ?? throw new InvalidOperationException("Standard invocation is missing its request."),
                cancellationToken),
            DomeCliCommandKind.Runtime => runtimeRunner.RunAsync(
                invocation.RuntimeRequest ?? throw new InvalidOperationException("Runtime invocation is missing its request."),
                cancellationToken),
            DomeCliCommandKind.ShadowExtraction => shadowRunner.RunAsync(
                invocation.ShadowRequest ?? throw new InvalidOperationException("Shadow extraction invocation is missing its request."),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported CLI command kind '{invocation.Kind}'.")
        };

    /// <summary>
    /// 创建使用默认应用工厂的 CLI 执行器。
    /// </summary>
    /// <returns>CLI 执行器实例。</returns>
    public static DomeCliApplicationExecutor CreateDefault() =>
        new(
            DomeApplicationFactory.CreateDefault(),
            TerrariaRuntimeApplicationFactory.CreateDefaultRuntimeApplication(),
            TerrariaRuntimeShadowExtractionApplicationFactory.CreateDefaultShadowExtractionApplication());
}

