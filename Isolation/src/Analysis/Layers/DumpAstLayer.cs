using Analysis.Core;
using Analysis.Passes;
using Analysis.Semantic;

namespace Analysis.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/DumpAst.scala`。
/// </summary>
public sealed class DumpAstLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "dumpAst";

    /// <inheritdoc />
    public override string OverlayName => OverlayNameValue;

    /// <inheritdoc />
    public override string Description => "Dump AST layer";

    /// <inheritdoc />
    public override IReadOnlyList<string> DependsOn => new[] { BaseLayer.OverlayNameValue };

    /// <inheritdoc />
    public override IReadOnlyList<string> PassNames() => new[] { "DumpAst" };

    /// <inheritdoc />
    public override IReadOnlyList<CpgPass> CreatePasses(CpgGraph graph) => Array.Empty<CpgPass>();

    /// <summary>
    /// 生成简单 AST 边列表，供调试使用。
    /// </summary>
    public string Dump(CpgGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return string.Join(Environment.NewLine, graph.Edges.Where(edge => edge.Kind == CpgEdgeKind.Ast)
            .Select(edge => $"{edge.SourceId} AST {edge.TargetId}"));
    }
}
