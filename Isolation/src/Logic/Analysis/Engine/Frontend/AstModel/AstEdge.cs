using Domain.Analysis.Engine.Core;

namespace Logic.Analysis.Engine.Frontend.AstModel;

/// <summary>
/// 表示尚未写入图中的 AST 边。
/// </summary>
public sealed record AstEdge(CpgNode Source, CpgNode Target, CpgEdgeKind Kind, string Label = "");
