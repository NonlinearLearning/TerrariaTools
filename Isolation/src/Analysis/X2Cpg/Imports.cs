using Analysis.Core;

namespace Analysis.X2Cpg;

/// <summary>
/// 创建并连接 import 节点。
///
/// 对应 Joern `Imports.scala`。
/// </summary>
public static class Imports
{
    /// <summary>
    /// 创建 import 节点，并在存在导入调用时补 `IsCallForImport` 边。
    /// </summary>
    public static CpgNode CreateImportNodeAndLink(
        CpgGraph graph,
        string importedEntity,
        string importedAs,
        CpgNode? callNode = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(importedEntity);

        CpgNode importNode = graph.CreateNode(CpgNodeKind.Import);
        importNode.SetProperty("ImportedEntity", importedEntity);
        importNode.SetProperty("ImportedAs", importedAs ?? string.Empty);

        if (callNode is not null)
        {
            graph.AddEdge(callNode.Id, importNode.Id, CpgEdgeKind.IsCallForImport);
        }

        return importNode;
    }
}
