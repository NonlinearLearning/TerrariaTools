namespace TerrariaTools.Dome.Adapters.Analysis.Roslyn.CpgProjection;

using CoreAnalysis = TerrariaTools.Dome.Core.Analysis;
using CoreCommon = TerrariaTools.Dome.Core.Common;
using CoreCpg = TerrariaTools.Dome.Core.Cpg;

public sealed class RoslynCpgProjector
{
    public CpgAnalysisProjection Project(
        CoreAnalysis.SourceDocumentSet sourceSet,
        CoreAnalysis.TypeDependencyGraph typeGraph,
        CoreAnalysis.FunctionIndex functionIndex,
        CoreAnalysis.FunctionFactsIndex functionFacts)
    {
        var codePropertyGraph = BuildCodePropertyGraph(sourceSet, typeGraph, functionIndex, functionFacts);
        var functionGraph = BuildFunctionGraph(codePropertyGraph, functionIndex);
        return new CpgAnalysisProjection(codePropertyGraph, functionGraph);
    }

    private static CoreCpg.DomeCpg BuildCodePropertyGraph(
        CoreAnalysis.SourceDocumentSet sourceSet,
        CoreAnalysis.TypeDependencyGraph typeGraph,
        CoreAnalysis.FunctionIndex functionIndex,
        CoreAnalysis.FunctionFactsIndex functionFacts)
    {
        var graph = new CoreCpg.DomeCpg();
        var diff = new CoreCpg.DiffGraph();
        var addedNodeIds = new HashSet<string>(StringComparer.Ordinal);

        diff.AddNode(new CoreCpg.MetaDataNode("meta-data", "CSHARP", sourceSet.EntryPath, "0.1"));
        addedNodeIds.Add("meta-data");

        foreach (var document in sourceSet.Documents)
        {
            AddNode(
                diff,
                addedNodeIds,
                new CoreCpg.FileNode($"file:{document.RelativePath}", document.RelativePath));
        }

        foreach (var type in typeGraph.Nodes)
        {
            AddNode(
                diff,
                addedNodeIds,
                new CoreCpg.TypeDeclNode(
                    CoreCpg.NodeIdFactory.TypeDecl(type.TypeId),
                    GetSimpleName(type.TypeId),
                    fullName: type.TypeId));
        }

        foreach (var edge in typeGraph.Edges.Where(static edge => edge.Kind == CoreAnalysis.TypeDependencyKind.Inherits))
        {
            diff.AddEdge(new CoreCpg.CpgEdge(
                CoreCpg.EdgeKinds.InheritsFrom,
                CoreCpg.NodeIdFactory.TypeDecl(edge.SourceTypeId),
                CoreCpg.NodeIdFactory.TypeDecl(edge.TargetTypeId)));
        }

        foreach (var functionNode in functionIndex.NodesByMemberId.Values)
        {
            AddNode(diff, addedNodeIds, CreateMethodNode(functionNode));
        }

        foreach (var functionFact in functionFacts.FactsByMemberId.Values)
        {
            var sourceMethodNodeId = BuildMethodNodeId(functionFact.Node.MemberId.Value);
            foreach (var calledMemberId in functionFact.CalledMemberIds)
            {
                var targetMethodNodeId = BuildMethodNodeId(calledMemberId.Value);
                AddNode(diff, addedNodeIds, CreateMethodStubNode(calledMemberId.Value));
                diff.AddEdge(new CoreCpg.CpgEdge(CoreCpg.EdgeKinds.Call, sourceMethodNodeId, targetMethodNodeId));
            }
        }

        CoreCpg.DiffGraphApplier.Apply(graph, diff);
        return graph;
    }

    private static CoreAnalysis.FunctionDependencyGraph BuildFunctionGraph(
        CoreCpg.DomeCpg codePropertyGraph,
        CoreAnalysis.FunctionIndex functionIndex)
    {
        var nodes = functionIndex.NodesByMemberId.Values
            .OrderBy(node => node.MemberId.Value, StringComparer.Ordinal)
            .ToArray();
        var nodesByMethodNodeId = functionIndex.NodesByMemberId.Values
            .GroupBy(node => BuildMethodNodeId(node.MemberId.Value), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<CoreAnalysis.FunctionNodeRef>)group.ToArray(),
                StringComparer.Ordinal);
        var edges = codePropertyGraph.GetEdgesByLabel(CoreCpg.EdgeKinds.Call)
            .SelectMany(edge => BuildFunctionEdges(edge, nodesByMethodNodeId))
            .Distinct()
            .OrderBy(edge => edge.SourceMemberId.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetMemberId.Value, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind)
            .ToArray();

        return new CoreAnalysis.FunctionDependencyGraph(nodes, edges);
    }

    private static IEnumerable<CoreAnalysis.FunctionDependencyEdge> BuildFunctionEdges(
        CoreCpg.CpgEdge edge,
        IReadOnlyDictionary<string, IReadOnlyList<CoreAnalysis.FunctionNodeRef>> nodesByMethodNodeId)
    {
        if (!nodesByMethodNodeId.TryGetValue(edge.SourceId, out var sourceNodes) ||
            !nodesByMethodNodeId.TryGetValue(edge.TargetId, out var targetNodes))
        {
            yield break;
        }

        foreach (var sourceNode in sourceNodes)
        {
            foreach (var targetNode in targetNodes)
            {
                yield return new CoreAnalysis.FunctionDependencyEdge(
                    sourceNode.MemberId,
                    targetNode.MemberId,
                    CoreCommon.FunctionDependencyKind.Calls);
            }
        }
    }

    private static CoreCpg.MethodNode CreateMethodNode(CoreAnalysis.FunctionNodeRef functionNode)
    {
        return new CoreCpg.MethodNode(
            BuildMethodNodeId(functionNode.MemberId.Value),
            GetSimpleName(functionNode.MemberId.Value),
            functionNode.DeclaringTypeId,
            functionNode.ReturnTypeDisplay,
            GetQualifiedMemberName(functionNode.MemberId.Value),
            functionNode.ReturnTypeDisplay,
            functionNode.ReturnTypeDisplay);
    }

    private static CoreCpg.MethodNode CreateMethodStubNode(string memberId)
    {
        return new CoreCpg.MethodNode(
            BuildMethodNodeId(memberId),
            GetSimpleName(memberId),
            GetContainingTypeName(memberId),
            fullName: GetQualifiedMemberName(memberId));
    }

    private static void AddNode(
        CoreCpg.DiffGraph diff,
        ISet<string> addedNodeIds,
        CoreCpg.StoredNode node)
    {
        if (addedNodeIds.Add(node.Id))
        {
            diff.AddNode(node);
        }
    }

    private static string BuildMethodNodeId(string memberId)
    {
        return CoreCpg.NodeIdFactory.Method(GetContainingTypeName(memberId), GetSimpleName(memberId));
    }

    private static string GetQualifiedMemberName(string memberId)
    {
        var parameterListIndex = memberId.IndexOf('(');
        return parameterListIndex >= 0 ? memberId[..parameterListIndex] : memberId;
    }

    private static string GetContainingTypeName(string memberId)
    {
        var qualifiedMemberName = GetQualifiedMemberName(memberId);
        var lastSeparatorIndex = qualifiedMemberName.LastIndexOf('.');
        return lastSeparatorIndex > 0 ? qualifiedMemberName[..lastSeparatorIndex] : string.Empty;
    }

    private static string GetSimpleName(string memberId)
    {
        var qualifiedMemberName = GetQualifiedMemberName(memberId);
        var lastSeparatorIndex = qualifiedMemberName.LastIndexOf('.');
        return lastSeparatorIndex >= 0 ? qualifiedMemberName[(lastSeparatorIndex + 1)..] : qualifiedMemberName;
    }
}
