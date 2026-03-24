namespace TerrariaTools.Dome.Application.Runtime.Host;

using ModelExecution = TerrariaTools.Dome.Application.Ports;
using ModelPrimitives = TerrariaTools.Dome.Application.Ports;

/// <summary>
/// 提供运行时宿主使用的终态结果投影方法。
/// </summary>
internal static class DomeTerminalResultProjector
{
    /// <summary>
    /// 直接返回已有运行结果。
    /// </summary>
    /// <param name="result">已有运行结果。</param>
    /// <returns>同一个运行结果实例。</returns>
    internal static ModelExecution.RunResult Project(ModelExecution.RunResult result) => result;

    /// <summary>
    /// 创建成功的终态结果。
    /// </summary>
    /// <param name="outputPath">输出目录。</param>
    /// <param name="reportPath">报告路径。</param>
    /// <returns>成功结果。</returns>
    internal static ModelExecution.RunResult Success(string outputPath, string reportPath) =>
        ModelExecution.RunResult.Success(outputPath, reportPath);

    /// <summary>
    /// 创建失败的终态结果。
    /// </summary>
    /// <param name="failureCode">失败代码。</param>
    /// <param name="outputPath">输出目录。</param>
    /// <param name="message">失败消息。</param>
    /// <returns>失败结果。</returns>
    internal static ModelExecution.RunResult Failure(ModelPrimitives.FailureCode failureCode, string outputPath, string? message) =>
        ModelExecution.RunResult.Failure(failureCode, outputPath, message);
}
