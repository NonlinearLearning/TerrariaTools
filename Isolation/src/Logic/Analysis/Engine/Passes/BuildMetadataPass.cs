using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Passes;

/// <summary>
/// 创建描述整张图的元数据节点。
///
/// 这个 pass 对应 Joern 中的元数据创建步骤，它至少要记录：
/// - 图来自哪种语言；
/// - 输入根路径是什么；
/// - 是哪个前端生成的。
///
/// 这一步之所以应该尽早做，是因为后续 pass 和未来工具层都需要
/// 一个稳定入口来读取“图级别事实”，而不是从零散节点里反推。
/// </summary>
public sealed class BuildMetadataPass : CpgPass
{
    /// <summary>
    /// 初始化元数据 pass。
    /// </summary>
    /// <param name="language">源码语言名。</param>
    /// <param name="frontendName">前端实现名。</param>
    /// <param name="inputPath">本次分析输入路径。</param>
    public BuildMetadataPass(string language, string frontendName, string inputPath)
    {
        Language = language ?? throw new ArgumentNullException(nameof(language));
        FrontendName = frontendName ?? throw new ArgumentNullException(nameof(frontendName));
        InputPath = inputPath ?? throw new ArgumentNullException(nameof(inputPath));
    }

    /// <summary>
    /// 获取要写入元数据节点的语言名。
    /// </summary>
    public string Language { get; }

    /// <summary>
    /// 获取要写入元数据节点的前端名。
    /// </summary>
    public string FrontendName { get; }

    /// <summary>
    /// 获取要写入元数据节点的输入根路径。
    /// </summary>
    public string InputPath { get; }


    protected override void Execute(CpgGraphBuilder builder)
    {
        CpgNode metaData = builder.CreateNode(CpgNodeKind.MetaData);
        metaData.SetProperty("Language", Language);
        metaData.SetProperty("Frontend", FrontendName);
        metaData.SetProperty("InputPath", InputPath);
        metaData.SetProperty("Overlays", new List<string>());
    }
}
