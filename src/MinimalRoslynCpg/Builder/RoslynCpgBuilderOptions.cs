namespace MinimalRoslynCpg.Builder;

public enum RoslynCpgBuilderMode
{
  Legacy,
  Auto,
  Partitioned
}

public sealed record RoslynCpgBuilderOptions(
  RoslynCpgBuilderMode BuildMode,
  int MaxDegreeOfParallelism,
  int LargeFileLineThreshold,
  int LargeFileMethodThreshold,
  int LargeMethodLineSpanThreshold,
  bool EnableReferencedSymbolTypeReuse = true,
  bool EnableOperationBackedSyntaxTypes = true)
{
  public int EffectiveMaxDegreeOfParallelism => Math.Max(1, MaxDegreeOfParallelism);

  public static RoslynCpgBuilderOptions CreateDefault()
  {
    return new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Legacy,
      Math.Max(1, Environment.ProcessorCount),
      LargeFileLineThreshold: 800,
      LargeFileMethodThreshold: 8,
      LargeMethodLineSpanThreshold: 80,
      EnableReferencedSymbolTypeReuse: true,
      EnableOperationBackedSyntaxTypes: true);
  }
}

public sealed record RoslynCpgBuildTelemetry(
  RoslynCpgBuilderMode RequestedMode,
  RoslynCpgBuilderMode ExecutedMode,
  bool UsedPartitionedOperationBuild,
  int SourceLineCount,
  int PartitionCount,
  int MaxDegreeOfParallelism,
  RoslynCpgSyntaxPassTelemetry SyntaxPassTelemetry,
  RoslynCpgDataFlowPassTelemetry DataFlowPassTelemetry)
{
  public static RoslynCpgBuildTelemetry CreateDefault()
  {
    return new RoslynCpgBuildTelemetry(
      RoslynCpgBuilderMode.Legacy,
      RoslynCpgBuilderMode.Legacy,
      UsedPartitionedOperationBuild: false,
      SourceLineCount: 0,
      PartitionCount: 0,
      MaxDegreeOfParallelism: 1,
      SyntaxPassTelemetry: RoslynCpgSyntaxPassTelemetry.CreateDefault(),
      DataFlowPassTelemetry: RoslynCpgDataFlowPassTelemetry.CreateDefault());
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
  int SyntaxNodeCount,
  int SyntaxTokenCount)
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
      SyntaxNodeCount: 0,
      SyntaxTokenCount: 0);
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
  int OrderedOperationCount)
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
      OrderedOperationCount: 0);
  }
}
