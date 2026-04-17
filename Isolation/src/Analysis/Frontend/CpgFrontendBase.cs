using Analysis.Core;
using Analysis.Passes;

namespace Analysis.Frontend;

/// <summary>
/// 定义最小前端抽象。
///
/// 它对应 Joern 里的 `X2CpgFrontend` 思想，但当前只保留最小核心：
/// - 接收前端配置；
/// - 创建图；
/// - 写入元数据；
/// - 返回内存态 CPG。
///
/// 这样做可以先稳定阶段一、二的主路径，而不把 CLI、服务模式、
/// 参数解析等外围结构也一起搬进来。
/// </summary>
public abstract class CpgFrontendBase
{
    /// <summary>
    /// 创建一张新的内存态 CPG。
    /// </summary>
    /// <param name="options">前端运行配置。</param>
    /// <returns>构建完成的图。</returns>
    public CpgGraph CreateGraph(CpgFrontendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        CpgGraph graph = new();
        new BuildMetadataPass(options.Language, options.FrontendName, options.InputPath).Run(graph);
        BuildGraphCore(graph, options);
        return graph;
    }

    /// <summary>
    /// 由具体前端负责构建自己的核心图内容。
    /// </summary>
    /// <param name="graph">当前图。</param>
    /// <param name="options">前端运行配置。</param>
    protected abstract void BuildGraphCore(CpgGraph graph, CpgFrontendOptions options);
}
