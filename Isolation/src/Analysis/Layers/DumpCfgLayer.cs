using Analysis.Core;
using Analysis.Passes;

namespace Analysis.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/DumpCfg.scala`。
/// </summary>
public sealed class DumpCfgLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "dumpCfg";

    /// <inheritdoc />
    public override string OverlayName => OverlayNameValue;

    /// <inheritdoc />
    public override string Description => "Dump CFG layer";

    /// <inheritdoc />
    public override IReadOnlyList<string> DependsOn => new[] { ControlFlowLayer.OverlayNameValue };

    /// <inheritdoc />
    public override IReadOnlyList<string> PassNames() => new[] { "DumpCfg" };

    /// <inheritdoc />
    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph) => Array.Empty<CpgPass>();

    /// <summary>
    /// 生成简单 CFG 边列表。
    /// </summary>
    public string Dump(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return string.Join(Environment.NewLine, graph.Edges.Where(edge => edge.Kind == CpgEdgeKind.Cfg)
            .Select(edge => $"{edge.SourceId} CFG {edge.TargetId}"));
    }
}
