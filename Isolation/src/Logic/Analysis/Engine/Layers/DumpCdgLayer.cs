using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;

namespace Logic.Analysis.Engine.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/DumpCdg.scala`。
/// </summary>
public sealed class DumpCdgLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "dumpCdg";


    public override string OverlayName => OverlayNameValue;


    public override string Description => "Dump CDG layer";


    public override IReadOnlyList<string> DependsOn => new[] { ControlFlowLayer.OverlayNameValue };


    public override IReadOnlyList<string> PassNames() => new[] { "DumpCdg" };


    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph) => Array.Empty<CpgPass>();

    /// <summary>
    /// 生成简单 CDG 边列表。
    /// </summary>
    public string Dump(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return string.Join(Environment.NewLine, graph.Edges.Where(edge => edge.Kind == CpgEdgeKind.Cdg)
            .Select(edge => $"{edge.SourceId} CDG {edge.TargetId}"));
    }
}
