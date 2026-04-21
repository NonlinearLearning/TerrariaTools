using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Layers;

/// <summary>
/// 按依赖关系应用 layer。
///
/// 对应 Joern 运行 overlay 的高层机制。这里使用简单拓扑递归，保证依赖先运行。
/// </summary>
public sealed class LayerPipeline
{
    private readonly Dictionary<string, LayerCreatorBase> layers;

    /// <summary>
    /// 使用 layer 集合初始化 pipeline。
    /// </summary>
    public LayerPipeline(IEnumerable<ILayerCreator> layerCreators)
    {
        ArgumentNullException.ThrowIfNull(layerCreators);
        layers = layerCreators.Cast<LayerCreatorBase>()
            .ToDictionary(layer => layer.OverlayName, StringComparer.Ordinal);
    }

    /// <summary>
    /// 创建包含当前所有 x2cpg 核心 layer 的 pipeline。
    /// </summary>
    public static LayerPipeline CreateDefault()
    {
        return new LayerPipeline(new ILayerCreator[]
        {
            new BaseLayer(),
            new TypeRelationsLayer(),
            new CallGraphLayer(),
            new ControlFlowLayer(),
            new DumpAstLayer(),
            new DumpCfgLayer(),
            new DumpCdgLayer(),
        });
    }

    /// <summary>
    /// 应用指定 layer 及其依赖。
    /// </summary>
    public void Apply(CpgGraph graph, string overlayName)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayName);
        ApplyInternal(graph, overlayName, new HashSet<string>(StringComparer.Ordinal));
    }

    private void ApplyInternal(CpgGraph graph, string overlayName, HashSet<string> visiting)
    {
        if (!layers.TryGetValue(overlayName, out LayerCreatorBase? layer))
        {
            throw new InvalidOperationException($"未知 layer：{overlayName}。");
        }

        if (Domain.Analysis.Engine.Semantic.Overlays.AppliedOverlays(graph).Contains(overlayName, StringComparer.Ordinal))
        {
            return;
        }

        if (!visiting.Add(overlayName))
        {
            throw new InvalidOperationException($"layer 依赖存在环：{overlayName}。");
        }

        foreach (string dependency in layer.DependsOn)
        {
            ApplyInternal(graph, dependency, visiting);
        }

        visiting.Remove(overlayName);
        layer.Apply(graph);
    }
}
