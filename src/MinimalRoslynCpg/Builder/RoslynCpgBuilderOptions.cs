using MinimalRoslynCpg.Contracts;

namespace MinimalRoslynCpg.Builder;

public enum RoslynCpgBuilderMode
{
  Partitioned
}

public enum RoslynCpgSyntaxPassMode
{
  Partitioned
}

public sealed record RoslynCpgBuilderOptions(
  RoslynCpgBuilderMode BuildMode,
  int MaxDegreeOfParallelism,
  int LargeFileLineThreshold,
  int LargeFileMethodThreshold,
  int LargeMethodLineSpanThreshold,
  bool EnableReferencedSymbolTypeReuse = true,
  bool EnableOperationBackedSyntaxTypes = true,
  RoslynCpgSyntaxPassMode SyntaxPassMode = RoslynCpgSyntaxPassMode.Partitioned,
  int SyntaxLargeFileLineThreshold = 800,
  IReadOnlyCollection<RoslynCpgCapability>? RequestedCapabilities = null)
{
  public int EffectiveMaxDegreeOfParallelism => Math.Max(1, MaxDegreeOfParallelism);

  public static RoslynCpgBuilderOptions CreateDefault()
  {
    return new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      Math.Max(1, Environment.ProcessorCount),
      LargeFileLineThreshold: 800,
      LargeFileMethodThreshold: 8,
      LargeMethodLineSpanThreshold: 80,
      EnableReferencedSymbolTypeReuse: true,
      EnableOperationBackedSyntaxTypes: true,
      SyntaxPassMode: RoslynCpgSyntaxPassMode.Partitioned,
      SyntaxLargeFileLineThreshold: 800,
      RequestedCapabilities: null);
  }
}

public sealed record RoslynCpgBuildTelemetry(
  RoslynCpgBuilderMode RequestedMode,
  RoslynCpgBuilderMode ExecutedMode,
  bool UsedPartitionedOperationBuild,
  bool UsedPartitionedSyntaxPass,
  int SourceLineCount,
  int PartitionCount,
  int MaxDegreeOfParallelism,
  RoslynCpgSyntaxPassTelemetry SyntaxPassTelemetry,
  RoslynCpgMethodDecorationTelemetry MethodDecorationTelemetry,
  RoslynCpgDataFlowPassTelemetry DataFlowPassTelemetry,
  int OperationChildBufferRentCount = 0,
  IReadOnlyCollection<RoslynCpgCapability>? ResolvedCapabilities = null,
  IReadOnlyList<string>? ExecutedPassNames = null,
  IReadOnlyList<string>? SkippedPassNames = null,
  string GraphSnapshotVersion = "legacy-v1")
{
  public static RoslynCpgBuildTelemetry CreateDefault()
  {
    return new RoslynCpgBuildTelemetry(
      RoslynCpgBuilderMode.Partitioned,
      RoslynCpgBuilderMode.Partitioned,
      UsedPartitionedOperationBuild: true,
      UsedPartitionedSyntaxPass: true,
      SourceLineCount: 0,
      PartitionCount: 0,
      MaxDegreeOfParallelism: 1,
      SyntaxPassTelemetry: RoslynCpgSyntaxPassTelemetry.CreateDefault(),
      MethodDecorationTelemetry: RoslynCpgMethodDecorationTelemetry.CreateDefault(),
      DataFlowPassTelemetry: RoslynCpgDataFlowPassTelemetry.CreateDefault(),
      ResolvedCapabilities: Array.Empty<RoslynCpgCapability>(),
      ExecutedPassNames: Array.Empty<string>(),
      SkippedPassNames: Array.Empty<string>(),
      GraphSnapshotVersion: "legacy-v1");
  }
}

public sealed record RoslynCpgMethodDecorationTelemetry(
  int SyntaxNodeCount,
  int DeclaredSymbolQueryCount)
{
  public static RoslynCpgMethodDecorationTelemetry CreateDefault()
  {
    return new RoslynCpgMethodDecorationTelemetry(
      SyntaxNodeCount: 0,
      DeclaredSymbolQueryCount: 0);
  }
}

public sealed record RoslynCpgSyntaxPassTelemetry(
  long TotalElapsedMilliseconds,
  long TraversalElapsedMilliseconds,
  long CreateSyntaxNodeElapsedMilliseconds,
  long EmitChildTokensElapsedMilliseconds,
  long AddDeclaredSymbolEdgesElapsedMilliseconds,
  long AddReferencedSymbolEdgesElapsedMilliseconds,
  long AddTypeInfoElapsedMilliseconds,
  long ResolveTypeInfoElapsedMilliseconds,
  long AddSyntaxTypeEdgesElapsedMilliseconds,
  long AddTypeReferenceEdgesElapsedMilliseconds,
  int TypeInfoQueryCount,
  int TypeInfoResolvedCount,
  int TypeInfoSymbolReuseCount,
  int DeclaredSymbolQueryCount,
  int DeclaredSymbolResolvedCount,
  int SyntaxNodeCount,
  int SyntaxTokenCount,
  int SyntaxPartitionCount,
  int SyntaxPartitionMaxDegreeOfParallelism,
  int OperationBackedTypeInfoDeferredCount,
  int OperationBackedTypeInfoResolvedCount,
  int OperationBackedTypeInfoFallbackCount,
  long OperationBackedTypeInfoFallbackElapsedMilliseconds,
  long OperationBackedTypeInfoFallbackElapsedTicks,
  int OperationBackedTypeInfoMissingOperationCount,
  int OperationBackedTypeInfoNullOperationTypeCount,
  IReadOnlyDictionary<string, int> OperationBackedTypeInfoFallbackCountBySyntaxKind)
{
  public static RoslynCpgSyntaxPassTelemetry CreateDefault()
  {
    return new RoslynCpgSyntaxPassTelemetry(
      TotalElapsedMilliseconds: 0,
      TraversalElapsedMilliseconds: 0,
      CreateSyntaxNodeElapsedMilliseconds: 0,
      EmitChildTokensElapsedMilliseconds: 0,
      AddDeclaredSymbolEdgesElapsedMilliseconds: 0,
      AddReferencedSymbolEdgesElapsedMilliseconds: 0,
      AddTypeInfoElapsedMilliseconds: 0,
      ResolveTypeInfoElapsedMilliseconds: 0,
      AddSyntaxTypeEdgesElapsedMilliseconds: 0,
      AddTypeReferenceEdgesElapsedMilliseconds: 0,
      TypeInfoQueryCount: 0,
      TypeInfoResolvedCount: 0,
      TypeInfoSymbolReuseCount: 0,
      DeclaredSymbolQueryCount: 0,
      DeclaredSymbolResolvedCount: 0,
      SyntaxNodeCount: 0,
      SyntaxTokenCount: 0,
      SyntaxPartitionCount: 0,
      SyntaxPartitionMaxDegreeOfParallelism: 1,
      OperationBackedTypeInfoDeferredCount: 0,
      OperationBackedTypeInfoResolvedCount: 0,
      OperationBackedTypeInfoFallbackCount: 0,
      OperationBackedTypeInfoFallbackElapsedMilliseconds: 0,
      OperationBackedTypeInfoFallbackElapsedTicks: 0,
      OperationBackedTypeInfoMissingOperationCount: 0,
      OperationBackedTypeInfoNullOperationTypeCount: 0,
      OperationBackedTypeInfoFallbackCountBySyntaxKind: new Dictionary<string, int>(StringComparer.Ordinal));
  }
}

public sealed record RoslynCpgDataFlowPassTelemetry(
  long TotalElapsedMilliseconds,
  long EnumerateMethodBlocksElapsedMilliseconds,
  long EnumerateOrderedOperationsElapsedMilliseconds,
  long CfgSensitiveElapsedMilliseconds,
  long ValueSourceEdgeElapsedMilliseconds,
  long ReturnFlowEdgeElapsedMilliseconds,
  long TerminalFlowEdgeElapsedMilliseconds,
  long CallArgumentAndReturnElapsedMilliseconds,
  long BuildFlowNeighborsElapsedMilliseconds,
  long FixpointElapsedMilliseconds,
  long ReachingDefinitionEdgeElapsedMilliseconds,
  int MethodBlockCount,
  int OrderedOperationCount,
  long PrepareFlowNodesElapsedMilliseconds,
  long CollectUsedFactsElapsedMilliseconds,
  long CreateDefinitionFactsElapsedMilliseconds,
  long InitializeCfgSensitiveStateElapsedMilliseconds,
  int FlowNodeCount,
  int UsedFactCount,
  int DefinitionFactCount,
  int UsedFactPartitionCount,
  int UsedFactPartitionMaxDegreeOfParallelism,
  int CfgSensitivePartitionCount,
  int CfgSensitivePartitionMaxDegreeOfParallelism,
  long CfgSensitiveCandidateGenerationElapsedMilliseconds = 0,
  long CfgSensitiveCandidateCommitElapsedMilliseconds = 0,
  int PeakBufferedCandidateBatchCount = 0,
  int CandidateEdgeCount = 0,
  int FrozenOperationNodeCount = 0,
  int MethodOperationNodeProjectionCount = 0,
  int UsedFactRecordCount = 0,
  long PrepareFlowNodesElapsedTicks = 0,
  long CollectUsedFactsElapsedTicks = 0)
{
  public static RoslynCpgDataFlowPassTelemetry CreateDefault()
  {
    return new RoslynCpgDataFlowPassTelemetry(
      TotalElapsedMilliseconds: 0,
      EnumerateMethodBlocksElapsedMilliseconds: 0,
      EnumerateOrderedOperationsElapsedMilliseconds: 0,
      CfgSensitiveElapsedMilliseconds: 0,
      ValueSourceEdgeElapsedMilliseconds: 0,
      ReturnFlowEdgeElapsedMilliseconds: 0,
      TerminalFlowEdgeElapsedMilliseconds: 0,
      CallArgumentAndReturnElapsedMilliseconds: 0,
      BuildFlowNeighborsElapsedMilliseconds: 0,
      FixpointElapsedMilliseconds: 0,
      ReachingDefinitionEdgeElapsedMilliseconds: 0,
      MethodBlockCount: 0,
      OrderedOperationCount: 0,
      PrepareFlowNodesElapsedMilliseconds: 0,
      CollectUsedFactsElapsedMilliseconds: 0,
      CreateDefinitionFactsElapsedMilliseconds: 0,
      InitializeCfgSensitiveStateElapsedMilliseconds: 0,
      FlowNodeCount: 0,
      UsedFactCount: 0,
      DefinitionFactCount: 0,
      UsedFactPartitionCount: 0,
      UsedFactPartitionMaxDegreeOfParallelism: 1,
      CfgSensitivePartitionCount: 0,
      CfgSensitivePartitionMaxDegreeOfParallelism: 1);
  }
}
