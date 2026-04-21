using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;
using Domain.Analysis.Engine.Semantic;

namespace Logic.Analysis.Engine.Layers;

/// <summary>
/// 对应 Joern `x2cpg/layers/DumpAst.scala`。
/// </summary>
public sealed class DumpAstLayer : LayerCreatorBase
{
    /// <summary>
    /// Joern overlay 名称。
    /// </summary>
    public const string OverlayNameValue = "dumpAst";


    public override string OverlayName => OverlayNameValue;


    public override string Description => "Dump AST layer";


    public override IReadOnlyList<string> DependsOn => new[] { BaseLayer.OverlayNameValue };


    public override IReadOnlyList<string> PassNames() => new[] { "DumpAst" };


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
