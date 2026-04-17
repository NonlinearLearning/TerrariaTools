using Analysis.Core;

namespace Analysis.Frontend.AstModel;

/// <summary>
/// 表示尚未写入图中的 AST 边。
///
/// 对应 Joern `AstEdge`。它让前端可以先组装子树，再一次性写入 CPG。
/// </summary>
public sealed record AstEdge(CpgNode Source, CpgNode Target, CpgEdgeKind Kind, string Label = "");
