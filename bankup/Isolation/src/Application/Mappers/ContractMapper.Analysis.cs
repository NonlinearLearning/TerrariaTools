using Application.Contracts;
using Application.Contracts.Analysis;
using Domain.Analysis;

namespace Application.Mappers;

public static partial class ContractMapper
{
    public static AnalysisCpgSnapshotDto Map(AnalysisCpgSnapshot snapshot)
    {
        return new AnalysisCpgSnapshotDto
        {
            Id = snapshot.Id,
            WorkspaceContextId = snapshot.WorkspaceContextId,
            MinimumTarget = Map(snapshot.MinimumTarget),
            EntrySymbol = snapshot.EntrySymbol,
            Depth = snapshot.Depth,
            Nodes = snapshot.Nodes.Select(Map).ToArray(),
        };
    }

    public static AnalysisCompositeLayerSnapshotDto Map(AnalysisCompositeLayerSnapshot snapshot)
    {
        return new AnalysisCompositeLayerSnapshotDto
        {
            Id = snapshot.Id,
            CompositionName = snapshot.CompositionName,
            Depth = snapshot.Depth,
            LayerNames = snapshot.LayerNames.ToArray(),
            Nodes = snapshot.Nodes.Select(Map).ToArray(),
        };
    }

    public static MinimumNodeDto Map(MinimumNode node)
    {
        return new MinimumNodeDto
        {
            NodeId = node.NodeId,
            DisplayName = node.DisplayName,
            NodeType = Map(node.NodeType),
            DocumentPath = node.LocationRange.DocumentPath,
            StartLine = node.LocationRange.StartLine,
            StartColumn = node.LocationRange.StartColumn,
            EndLine = node.LocationRange.EndLine,
            EndColumn = node.LocationRange.EndColumn,
        };
    }

    public static MinimumNode Map(MinimumNodeDto node)
    {
        return new MinimumNode(
            node.NodeId,
            node.DisplayName,
            Map(node.NodeType),
            new LocationRange(
                node.DocumentPath,
                node.StartLine,
                node.StartColumn,
                node.EndLine,
                node.EndColumn));
    }

    public static ContractAnalysisSourceKind Map(AnalysisSourceKind sourceKind)
    {
        return sourceKind switch
        {
            AnalysisSourceKind.Solution => ContractAnalysisSourceKind.Solution,
            AnalysisSourceKind.Project => ContractAnalysisSourceKind.Project,
            AnalysisSourceKind.Directory => ContractAnalysisSourceKind.Directory,
            AnalysisSourceKind.SourceFile => ContractAnalysisSourceKind.SourceFile,
            _ => ContractAnalysisSourceKind.Unknown,
        };
    }

    public static AnalysisSourceKind Map(ContractAnalysisSourceKind sourceKind)
    {
        return sourceKind switch
        {
            ContractAnalysisSourceKind.Solution => AnalysisSourceKind.Solution,
            ContractAnalysisSourceKind.Project => AnalysisSourceKind.Project,
            ContractAnalysisSourceKind.Directory => AnalysisSourceKind.Directory,
            ContractAnalysisSourceKind.SourceFile => AnalysisSourceKind.SourceFile,
            _ => AnalysisSourceKind.Unknown,
        };
    }

    public static ContractMinimumAnalysisTarget Map(MinimumAnalysisTarget minimumTarget)
    {
        return minimumTarget switch
        {
            MinimumAnalysisTarget.File => ContractMinimumAnalysisTarget.File,
            MinimumAnalysisTarget.Type => ContractMinimumAnalysisTarget.Type,
            MinimumAnalysisTarget.Method => ContractMinimumAnalysisTarget.Method,
            MinimumAnalysisTarget.Statement => ContractMinimumAnalysisTarget.Statement,
            _ => ContractMinimumAnalysisTarget.Unknown,
        };
    }

    public static MinimumAnalysisTarget Map(ContractMinimumAnalysisTarget minimumTarget)
    {
        return minimumTarget switch
        {
            ContractMinimumAnalysisTarget.File => MinimumAnalysisTarget.File,
            ContractMinimumAnalysisTarget.Type => MinimumAnalysisTarget.Type,
            ContractMinimumAnalysisTarget.Method => MinimumAnalysisTarget.Method,
            ContractMinimumAnalysisTarget.Statement => MinimumAnalysisTarget.Statement,
            _ => MinimumAnalysisTarget.Method,
        };
    }

    public static ContractCpgNodeType Map(CpgType cpgType)
    {
        return cpgType switch
        {
            CpgType.File => ContractCpgNodeType.File,
            CpgType.TypeDecl => ContractCpgNodeType.TypeDecl,
            CpgType.Method => ContractCpgNodeType.Method,
            CpgType.Call => ContractCpgNodeType.Call,
            _ => ContractCpgNodeType.Unknown,
        };
    }

    public static CpgType Map(ContractCpgNodeType cpgType)
    {
        return cpgType switch
        {
            ContractCpgNodeType.File => CpgType.File,
            ContractCpgNodeType.TypeDecl => CpgType.TypeDecl,
            ContractCpgNodeType.Method => CpgType.Method,
            ContractCpgNodeType.Call => CpgType.Call,
            _ => CpgType.Unknown,
        };
    }
}
