namespace TerrariaTools.Dome.Application.Runtime.Host;

using TerrariaTools.Dome.Application.Composition;

/// <summary>
/// 提供运行时应用的工厂入口。
/// </summary>
public static class TerrariaRuntimeApplicationFactory
{
    /// <summary>
    /// 创建使用默认依赖的运行时应用实例。
    /// </summary>
    /// <returns>运行时应用实例。</returns>
    public static TerrariaRuntimeApplication CreateDefaultRuntimeApplication() =>
        TerrariaRuntimeCompositionRoot.CreateDefault();
}
