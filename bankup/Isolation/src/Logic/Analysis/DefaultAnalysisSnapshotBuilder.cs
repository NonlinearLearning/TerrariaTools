using Domain.Analysis;
using Domain.Workspaces;

namespace Logic.Analysis;

/// <summary>
/// 默认分析快照构建器。
/// </summary>
public sealed class DefaultAnalysisSnapshotBuilder : IAnalysisSnapshotFactory
{

    public AnalysisCpgSnapshot BuildCpgSnapshot(
        WorkspaceContext workspaceContext,
        MinimumAnalysisTarget minimumTarget,
        string entrySymbol,
        int depth)
    {
        ArgumentNullException.ThrowIfNull(workspaceContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(entrySymbol);

        AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(
            workspaceContext.Id,
            minimumTarget,
            entrySymbol,
            depth);

        string[] documents = workspaceContext.ResolveAnalysisDocuments(depth).ToArray();

        MinimumNode entryNode = CreateNode("entry", entrySymbol, CpgType.Method, documents[0], 1);
        snapshot.AddNode(entryNode);

        for (int index = 0; index < documents.Length; index++)
        {
            MinimumNode currentNode = CreateNode(
                $"file-{index + 1}",
                Path.GetFileNameWithoutExtension(documents[index]),
                index == 0 ? CpgType.TypeDecl : CpgType.File,
                documents[index],
                index + 2);

            snapshot.AddNode(currentNode);
            snapshot.AddFlow(new CpgFlow(entryNode.NodeId, currentNode.NodeId, CpgFlowKind.Sequential));
            snapshot.AddCall(new CpgCall(entryNode.NodeId, currentNode.NodeId, CpgCallKind.Static, currentNode.DisplayName));
        }

        return snapshot;
    }


    public AnalysisCompositeLayerSnapshot BuildCompositeSnapshot(
        WorkspaceContext workspaceContext,
        string compositionName,
        int depth,
        IReadOnlyCollection<string> layerNames)
    {
        ArgumentNullException.ThrowIfNull(workspaceContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(compositionName);

        AnalysisCompositeLayerSnapshot snapshot = AnalysisCompositeLayerSnapshot.Create(
            workspaceContext.Id,
            compositionName,
            depth);

        string[] layers = layerNames.Any()
            ? layerNames.ToArray()
            : new[] { "Domain", "Logic", "Application", "Interface" };

        for (int index = 0; index < layers.Length; index++)
        {
            string document = workspaceContext.Documents
                .Skip(index)
                .Select(item => workspaceContext.SolutionPathValue.ResolveWorkspacePath(item.Value))
                .FirstOrDefault()
                ?? workspaceContext.SolutionPath;

            snapshot.AddLayer(layers[index]);
            snapshot.AddNode(CreateNode(
                $"layer-{index + 1}",
                layers[index],
                CpgType.TypeDecl,
                document,
                index + 1));
        }

        return snapshot;
    }

    private static MinimumNode CreateNode(
        string nodeId,
        string displayName,
        CpgType nodeType,
        string documentPath,
        int line)
    {
        return new MinimumNode(
            nodeId,
            displayName,
            nodeType,
            new LocationRange(documentPath, line, 1, line, Math.Max(displayName.Length, 1)));
    }
}
