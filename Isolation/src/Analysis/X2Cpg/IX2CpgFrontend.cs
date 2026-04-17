using Analysis.Core;

namespace Analysis.X2Cpg;

/// <summary>
/// 表示一个可以创建 CPG 的前端。
///
/// 对应 Joern `X2CpgFrontend`。该接口只保留核心生命周期：创建图、运行图、
/// 创建图后叠加默认 overlay，以及释放前端资源。
/// </summary>
public interface IX2CpgFrontend : IDisposable
{
    /// <summary>
    /// 获取前端默认配置。
    /// </summary>
    X2CpgConfig DefaultConfig { get; }

    /// <summary>
    /// 按配置创建基础 CPG。
    /// </summary>
    /// <param name="config">前端配置。</param>
    /// <returns>创建出的 CPG。</returns>
    CpgGraph CreateCpg(X2CpgConfig config);

    /// <summary>
    /// 按配置创建基础 CPG，并应用默认 overlay。
    /// </summary>
    /// <param name="config">前端配置。</param>
    /// <returns>补齐默认 overlay 后的 CPG。</returns>
    CpgGraph CreateCpgWithOverlays(X2CpgConfig config)
    {
        CpgGraph graph = CreateCpg(config);
        X2Cpg.ApplyDefaultOverlays(graph);
        return graph;
    }

    /// <summary>
    /// 运行前端。内存态实现不需要关闭图存储，所以默认只触发建图。
    /// </summary>
    /// <param name="config">前端配置。</param>
    void Run(X2CpgConfig config)
    {
        _ = CreateCpg(config);
    }
}
