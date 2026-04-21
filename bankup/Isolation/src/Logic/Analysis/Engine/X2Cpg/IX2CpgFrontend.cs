using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.X2Cpg;

/// <summary>
/// 表示一个可以创建 CPG 的前端。
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
    CpgGraph CreateCpg(X2CpgConfig config);

    /// <summary>
    /// 按配置创建基础 CPG，并应用默认 overlay。
    /// </summary>
    CpgGraph CreateCpgWithOverlays(X2CpgConfig config)
    {
        CpgGraph graph = CreateCpg(config);
        X2CpgOverlays.ApplyDefaultOverlays(graph);
        return graph;
    }

    /// <summary>
    /// 运行前端。
    /// </summary>
    void Run(X2CpgConfig config)
    {
        _ = CreateCpg(config);
    }
}
