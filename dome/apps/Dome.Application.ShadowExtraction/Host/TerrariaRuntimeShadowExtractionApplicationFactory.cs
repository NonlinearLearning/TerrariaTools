namespace TerrariaTools.Dome.Application.ShadowExtraction.Host;

using TerrariaTools.Dome.Application.Composition;

/// <summary>
/// 提供影子提取应用的工厂入口。
/// </summary>
public static class TerrariaRuntimeShadowExtractionApplicationFactory
{
    /// <summary>
    /// 创建使用默认依赖的影子提取应用实例。
    /// </summary>
    /// <returns>影子提取应用实例。</returns>
    public static TerrariaRuntimeShadowExtractionApplication CreateDefaultShadowExtractionApplication() =>
        TerrariaRuntimeShadowExtractionCompositionRoot.CreateDefault();
}
