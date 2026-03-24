namespace TerrariaTools.Dome.Application.Host;

using TerrariaTools.Dome.Application.Composition;

/// <summary>
/// 提供标准 Dome 应用的工厂入口。
/// </summary>
public static class DomeApplicationFactory
{
    /// <summary>
    /// 创建使用默认依赖的 Dome 应用实例。
    /// </summary>
    /// <returns>Dome 应用实例。</returns>
    public static DomeApplication CreateDefault()
        => CreateDefault(null);

    /// <summary>
    /// 创建使用默认依赖的 Dome 应用实例。
    /// </summary>
    /// <param name="progressReporter">可选的进度上报器。</param>
    /// <returns>Dome 应用实例。</returns>
    public static DomeApplication CreateDefault(IDomeProgressReporter? progressReporter) =>
        DomeApplicationCompositionRoot.CreateDefault(progressReporter);
}
