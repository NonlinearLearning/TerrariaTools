namespace TerrariaTools.Dome.Application.Host;

/// <summary>
/// 定义标准 Dome 工作流的进度上报能力。
/// </summary>
public interface IDomeProgressReporter
{
    /// <summary>
    /// 上报一条进度消息。
    /// </summary>
    /// <param name="message">进度消息文本。</param>
    void Report(string message);
}

/// <summary>
/// 不执行任何输出的空进度上报器。
/// </summary>
public sealed class NullDomeProgressReporter : IDomeProgressReporter
{
    /// <summary>
    /// 获取共享的空进度上报器实例。
    /// </summary>
    public static NullDomeProgressReporter Instance { get; } = new();

    /// <summary>
    /// 忽略传入的进度消息。
    /// </summary>
    /// <param name="message">进度消息文本。</param>
    public void Report(string message)
    {
    }
}




