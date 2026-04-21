namespace Logic.Analysis.Engine.Frontend;

/// <summary>
/// 表示 CPG 前端运行时的最小配置。
///
/// 这里不照搬 Joern 那一整套配置对象，只保留当前阶段真正需要的核心输入。
/// 这样做是为了先把“最小 CPG 构建路径”跑通，再决定要不要扩展成平台级配置。
/// </summary>
public sealed class CpgFrontendOptions
{
    /// <summary>
    /// 获取或设置输入路径。
    /// </summary>
    public string InputPath { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置前端名称。
    /// </summary>
    public string FrontendName { get; init; } = "RoslynCpgFrontend";

    /// <summary>
    /// 获取或设置语言名称。
    /// </summary>
    public string Language { get; init; } = "CSHARP";

    /// <summary>
    /// 获取或设置类型桩文件路径。
    /// </summary>
    public string? TypeStubsFilePath { get; init; }
}
