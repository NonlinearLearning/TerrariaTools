using Analysis.Core;
using Analysis.Passes;
using Analysis.Semantic;

namespace Analysis.Layers;

/// <summary>
/// 提供 layer 的公共模板逻辑。
/// </summary>
public abstract class LayerCreatorBase : ILayerCreator
{
    /// <inheritdoc />
    public abstract string OverlayName { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public virtual IReadOnlyList<string> DependsOn => Array.Empty<string>();

    /// <inheritdoc />
    public abstract IReadOnlyList<string> PassNames();

    /// <inheritdoc />
    public abstract IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph);

    /// <summary>
    /// 执行当前 layer 并记录 overlay 名称。
    /// </summary>
    public void Apply(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EnsureMetaDataNode(graph);

        foreach (CpgPass pass in CreatePasses(graph))
        {
            pass.Run(graph);
        }

        if (!Overlays.AppliedOverlays(graph).Contains(OverlayName, StringComparer.Ordinal))
        {
            Overlays.AppendOverlayName(graph, OverlayName);
        }
    }

    private static void EnsureMetaDataNode(CpgGraph graph)
    {
        if (!graph.GetNodes(CpgNodeKind.MetaData).Any())
        {
            graph.CreateNode(CpgNodeKind.MetaData);
        }
    }
}
