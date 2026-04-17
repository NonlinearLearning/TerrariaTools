using Analysis.Core;
using Analysis.Passes;

namespace Analysis.Layers;

/// <summary>
/// 定义一个可应用到 CPG 上的 overlay layer。
///
/// 对应 Joern `LayerCreator`。每个 layer 声明名字、依赖和 pass 列表，
/// pipeline 负责按依赖顺序运行。
/// </summary>
public interface ILayerCreator
{
    /// <summary>
    /// 获取 overlay 名称。
    /// </summary>
    string OverlayName { get; }

    /// <summary>
    /// 获取说明文本。
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 获取依赖的 overlay 名称。
    /// </summary>
    IReadOnlyList<string> DependsOn { get; }

    /// <summary>
    /// 获取和 Joern 对齐的 pass 名称。
    /// </summary>
    IReadOnlyList<string> PassNames();

    /// <summary>
    /// 创建当前环境可以直接运行的 pass。
    /// </summary>
    IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph);
}
