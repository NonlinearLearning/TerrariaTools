using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Layers;

namespace Logic.Analysis.Engine.X2Cpg;

/// <summary>
/// 提供 X2Cpg 默认 overlay 的纯逻辑编排。
/// </summary>
public static class X2CpgOverlays
{
    /// <summary>
    /// 对前端 CPG 应用 Joern 默认 overlay。
    /// </summary>
    /// <param name="graph">目标 CPG。</param>
    public static void ApplyDefaultOverlays(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        LayerPipeline pipeline = new(DefaultOverlayCreators());
        foreach (ILayerCreator layer in DefaultOverlayCreators())
        {
            pipeline.Apply(graph, layer.OverlayName);
        }
    }

    /// <summary>
    /// 返回 Joern 默认 overlay 列表。
    ///
    /// 顺序对齐 `X2Cpg.scala`：Base、ControlFlow、TypeRelations、CallGraph。
    /// 当前不包含 Dump/DOT 类 layer，因为它们不是本项目要求的核心 CPG 能力。
    /// </summary>
    /// <returns>默认 overlay 创建器。</returns>
    public static IReadOnlyList<ILayerCreator> DefaultOverlayCreators()
    {
        return new ILayerCreator[]
        {
            new BaseLayer(),
            new ControlFlowLayer(),
            new TypeRelationsLayer(),
            new CallGraphLayer(),
        };
    }
}
