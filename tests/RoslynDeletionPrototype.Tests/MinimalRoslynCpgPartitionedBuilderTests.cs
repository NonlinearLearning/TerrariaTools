using MinimalRoslynCpg.Builder;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class MinimalRoslynCpgPartitionedBuilderTests
{
  [Fact]
  public void BuildFromSource_PartitionedMode_ProducesSameGraphAsLegacy()
  {
    const string filePath = "partitioned-ab.cs";
    var source = CreateLargeSource(methodCount: 12, statementsPerMethod: 10);
    var legacyBuilder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Legacy,
      MaxDegreeOfParallelism: 1,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6));
    var partitionedBuilder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6));

    var legacyGraph = legacyBuilder.BuildFromSource(source, filePath);
    var partitionedGraph = partitionedBuilder.BuildFromSource(source, filePath);

    AssertGraphsEqual(legacyGraph, partitionedGraph);
    Assert.False(legacyBuilder.LastBuildTelemetry.UsedPartitionedOperationBuild);
    Assert.True(partitionedBuilder.LastBuildTelemetry.UsedPartitionedOperationBuild);
    Assert.Equal(12, partitionedBuilder.LastBuildTelemetry.PartitionCount);
  }

  [Fact]
  public void BuildFromSource_AutoMode_UsesPartitionedBuildForLargeFile()
  {
    const string filePath = "partitioned-auto-large.cs";
    var source = CreateLargeSource(methodCount: 10, statementsPerMethod: 12);
    var builder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Auto,
      MaxDegreeOfParallelism: 3,
      LargeFileLineThreshold: 80,
      LargeFileMethodThreshold: 6,
      LargeMethodLineSpanThreshold: 8));

    _ = builder.BuildFromSource(source, filePath);

    Assert.True(builder.LastBuildTelemetry.UsedPartitionedOperationBuild);
    Assert.Equal(RoslynCpgBuilderMode.Partitioned, builder.LastBuildTelemetry.ExecutedMode);
    Assert.Equal(10, builder.LastBuildTelemetry.PartitionCount);
    Assert.Equal(3, builder.LastBuildTelemetry.MaxDegreeOfParallelism);
  }

  [Fact]
  public void BuildFromSource_AutoMode_KeepsLegacyBuildForSmallFile()
  {
    const string filePath = "partitioned-auto-small.cs";
    const string source =
      """
      namespace Demo;

      public sealed class SmallSample
      {
        public int Run(int value)
        {
          return value + 1;
        }
      }
      """;
    var builder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Auto,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 80,
      LargeFileMethodThreshold: 6,
      LargeMethodLineSpanThreshold: 8));

    _ = builder.BuildFromSource(source, filePath);

    Assert.False(builder.LastBuildTelemetry.UsedPartitionedOperationBuild);
    Assert.Equal(RoslynCpgBuilderMode.Legacy, builder.LastBuildTelemetry.ExecutedMode);
    Assert.Equal(0, builder.LastBuildTelemetry.PartitionCount);
  }

  [Fact]
  public void BuildFromSource_RecordsFineGrainedSyntaxAndDataFlowTelemetry()
  {
    const string filePath = "fine-grained-telemetry.cs";
    var source = CreateLargeSource(methodCount: 8, statementsPerMethod: 9);
    var builder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 40,
      LargeFileMethodThreshold: 4,
      LargeMethodLineSpanThreshold: 6));

    _ = builder.BuildFromSource(source, filePath);

    Assert.True(builder.LastBuildTelemetry.SyntaxPassTelemetry.TotalElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.SyntaxPassTelemetry.SyntaxNodeCount > 0);
    Assert.True(builder.LastBuildTelemetry.SyntaxPassTelemetry.SyntaxTokenCount > 0);
    Assert.True(builder.LastBuildTelemetry.DataFlowPassTelemetry.TotalElapsedMilliseconds >= 0);
    Assert.True(builder.LastBuildTelemetry.DataFlowPassTelemetry.MethodBlockCount > 0);
    Assert.True(builder.LastBuildTelemetry.DataFlowPassTelemetry.OrderedOperationCount > 0);
  }

  [Fact]
  public void BuildFromSource_RecordsSeparatedTypeInfoTelemetry()
  {
    const string source =
      """
      namespace Demo;

      public sealed class TypeInfoSample
      {
        private int _field;
        public int Property { get; set; }

        public int Run(int parameter)
        {
          var local = parameter + _field + Property;
          return new System.Collections.Generic.List<int> { local }.Count;
        }
      }
      """;
    var builder = new RoslynCpgBuilder();

    _ = builder.BuildFromSource(source, "separated-type-info-telemetry.cs");

    var telemetry = builder.LastBuildTelemetry.SyntaxPassTelemetry;
    Assert.True(telemetry.ResolveTypeInfoElapsedMilliseconds >= 0);
    Assert.True(telemetry.AddSyntaxTypeEdgesElapsedMilliseconds >= 0);
    Assert.True(telemetry.TypeInfoQueryCount > 0);
    Assert.True(telemetry.TypeInfoResolvedCount > 0);
  }

  [Fact]
  public void BuildFromSource_SymbolTypeReuse_PreservesCompleteGraph()
  {
    const string filePath = "symbol-type-reuse.cs";
    const string source =
      """
      namespace Demo;

      public sealed class SymbolTypeSample
      {
        private int _field;
        public int Property { get; set; }
        public event System.Action? Changed;

        public int Run(int parameter)
        {
          var local = parameter + _field + Property;
          Changed?.Invoke();
          return local;
        }
      }
      """;
    var withoutReuseBuilder = new RoslynCpgBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: false));
    var withReuseBuilder = new RoslynCpgBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: true));

    var withoutReuseGraph = withoutReuseBuilder.BuildFromSource(source, filePath);
    var withReuseGraph = withReuseBuilder.BuildFromSource(source, filePath);

    AssertGraphsEqual(withoutReuseGraph, withReuseGraph);
    AssertTypeGraphEqual(withoutReuseGraph, withReuseGraph);
    Assert.True(withReuseBuilder.LastBuildTelemetry.SyntaxPassTelemetry.TypeInfoSymbolReuseCount > 0);
  }

  [Fact]
  public void BuildFromSource_SymbolTypeReuse_FallsBackForMethodGroupsAndDynamicSyntax()
  {
    const string source =
      """
      namespace Demo;

      public sealed class FallbackSample
      {
        private static int Transform(int value) => value + 1;

        public int Run(dynamic dynamicValue)
        {
          System.Func<int, int> methodGroup = Transform;
          var conditional = dynamicValue?.ToString();
          return methodGroup(conditional is null ? 0 : 1);
        }
      }
      """;
    var withoutReuseBuilder = new RoslynCpgBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: false));
    var withReuseBuilder = new RoslynCpgBuilder(CreateBuilderOptions(enableReferencedSymbolTypeReuse: true));

    var withoutReuseGraph = withoutReuseBuilder.BuildFromSource(source, "symbol-type-fallback.cs");
    var withReuseGraph = withReuseBuilder.BuildFromSource(source, "symbol-type-fallback.cs");

    AssertGraphsEqual(withoutReuseGraph, withReuseGraph);
    AssertTypeGraphEqual(withoutReuseGraph, withReuseGraph);
    Assert.True(withReuseBuilder.LastBuildTelemetry.SyntaxPassTelemetry.TypeInfoQueryCount > 0);
  }

  [Fact]
  public void BuildFromSource_OperationBackedSyntaxTypes_PreservesTypeEdges()
  {
    const string source =
      """
      namespace Demo;

      public sealed class OperationTypeSample
      {
        private int _value;

        public int Run(int parameter)
        {
          var local = parameter + _value;
          return local > 0 ? local : local + 1;
        }
      }
      """;
    var syntaxOnlyBuilder = new RoslynCpgBuilder(CreateBuilderOptions(
      enableReferencedSymbolTypeReuse: true,
      enableOperationBackedSyntaxTypes: false));
    var operationBackedBuilder = new RoslynCpgBuilder(CreateBuilderOptions(
      enableReferencedSymbolTypeReuse: true,
      enableOperationBackedSyntaxTypes: true));

    var syntaxOnlyGraph = syntaxOnlyBuilder.BuildFromSource(source, "operation-backed-types.cs");
    var operationBackedGraph = operationBackedBuilder.BuildFromSource(source, "operation-backed-types.cs");

    AssertGraphsEqual(syntaxOnlyGraph, operationBackedGraph);
    AssertTypeGraphEqual(syntaxOnlyGraph, operationBackedGraph);
  }

  [Fact]
  public void BuildFromSource_PartitionedMode_PreservesControlFlowAndDataFlowHeavyGraph()
  {
    const string filePath = "partitioned-controlflow-dataflow.cs";
    const string source =
      """
      namespace Demo;

      public sealed class ComplexSample
      {
        public int Run(int input)
        {
          var total = input;
          while (total < 5)
          {
            total += 1;
            if (total == 3)
            {
              continue;
            }
          }

          for (var index = 0; index < 2; index += 1)
          {
            total += index;
          }

          switch (total)
          {
            case 0:
              total += 10;
              break;
            case 1:
            case 2:
              total += 20;
              break;
            default:
              total += 30;
              break;
          }

          try
          {
            total += Helper(total);
          }
          catch (System.InvalidOperationException)
          {
            total -= 1;
          }
          finally
          {
            total += 100;
          }

          return total;
        }

        private static int Helper(int value)
        {
          return value > 10 ? value : value + 1;
        }
      }
      """;
    var legacyBuilder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Legacy,
      MaxDegreeOfParallelism: 1,
      LargeFileLineThreshold: 20,
      LargeFileMethodThreshold: 2,
      LargeMethodLineSpanThreshold: 5));
    var partitionedBuilder = new RoslynCpgBuilder(new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Partitioned,
      MaxDegreeOfParallelism: 4,
      LargeFileLineThreshold: 20,
      LargeFileMethodThreshold: 2,
      LargeMethodLineSpanThreshold: 5));

    var legacyGraph = legacyBuilder.BuildFromSource(source, filePath);
    var partitionedGraph = partitionedBuilder.BuildFromSource(source, filePath);

    AssertGraphsEqual(legacyGraph, partitionedGraph);
    Assert.True(partitionedBuilder.LastBuildTelemetry.DataFlowPassTelemetry.BuildFlowNeighborsElapsedMilliseconds >= 0);
  }

  private static void AssertGraphsEqual(RoslynCpgGraph expected, RoslynCpgGraph actual)
  {
    Assert.Equal(
      expected.Nodes
        .OrderBy(node => node.Id, StringComparer.Ordinal)
        .Select(FormatNode)
        .ToArray(),
      actual.Nodes
        .OrderBy(node => node.Id, StringComparer.Ordinal)
        .Select(FormatNode)
        .ToArray());
    Assert.Equal(
      expected.Edges
        .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
        .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
        .ThenBy(edge => edge.Label, StringComparer.Ordinal)
        .Select(edge => $"{edge.SourceId}|{edge.Kind}|{edge.TargetId}|{edge.Label}")
        .ToArray(),
      actual.Edges
        .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
        .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
        .ThenBy(edge => edge.Label, StringComparer.Ordinal)
        .Select(edge => $"{edge.SourceId}|{edge.Kind}|{edge.TargetId}|{edge.Label}")
        .ToArray());
  }

  private static void AssertTypeGraphEqual(RoslynCpgGraph expected, RoslynCpgGraph actual)
  {
    Assert.Equal(
      expected.Nodes
        .Where(node => node.Kind == RoslynCpgNodeKind.TypeRef)
        .OrderBy(node => node.Id, StringComparer.Ordinal)
        .Select(FormatNode)
        .ToArray(),
      actual.Nodes
        .Where(node => node.Kind == RoslynCpgNodeKind.TypeRef)
        .OrderBy(node => node.Id, StringComparer.Ordinal)
        .Select(FormatNode)
        .ToArray());
    Assert.Equal(
      expected.Edges
        .Where(edge => edge.Kind is RoslynCpgEdgeKind.HasType
          or RoslynCpgEdgeKind.RefersToType
          or RoslynCpgEdgeKind.SyntaxChild)
        .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
        .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
        .ThenBy(edge => edge.Label, StringComparer.Ordinal)
        .Select(edge => $"{edge.SourceId}|{edge.Kind}|{edge.TargetId}|{edge.Label}")
        .ToArray(),
      actual.Edges
        .Where(edge => edge.Kind is RoslynCpgEdgeKind.HasType
          or RoslynCpgEdgeKind.RefersToType
          or RoslynCpgEdgeKind.SyntaxChild)
        .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
        .ThenBy(edge => edge.Kind.ToString(), StringComparer.Ordinal)
        .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
        .ThenBy(edge => edge.Label, StringComparer.Ordinal)
        .Select(edge => $"{edge.SourceId}|{edge.Kind}|{edge.TargetId}|{edge.Label}")
        .ToArray());
  }

  private static RoslynCpgBuilderOptions CreateBuilderOptions(
    bool enableReferencedSymbolTypeReuse,
    bool enableOperationBackedSyntaxTypes = true)
  {
    return new RoslynCpgBuilderOptions(
      RoslynCpgBuilderMode.Legacy,
      MaxDegreeOfParallelism: 1,
      LargeFileLineThreshold: 800,
      LargeFileMethodThreshold: 8,
      LargeMethodLineSpanThreshold: 80,
      EnableReferencedSymbolTypeReuse: enableReferencedSymbolTypeReuse,
      EnableOperationBackedSyntaxTypes: enableOperationBackedSyntaxTypes);
  }

  private static string FormatNode(RoslynCpgNode node)
  {
    return string.Join(
      "|",
      node.Id,
      node.Kind,
      node.DisplayKind,
      node.Name,
      node.FullName,
      node.Signature,
      node.DispatchKind,
      node.TypeFullName,
      node.FilePath,
      node.SpanStart,
      node.SpanEnd,
      node.IsImplicit,
      node.Text);
  }

  private static string CreateLargeSource(int methodCount, int statementsPerMethod)
  {
    var builder = new System.Text.StringBuilder();
    builder.AppendLine("namespace Demo;");
    builder.AppendLine();
    builder.AppendLine("public sealed class LargeSample");
    builder.AppendLine("{");
    for (var methodIndex = 0; methodIndex < methodCount; methodIndex += 1)
    {
      builder.AppendLine($"  public int Run{methodIndex}(int seed)");
      builder.AppendLine("  {");
      builder.AppendLine("    var total = seed;");
      for (var statementIndex = 0; statementIndex < statementsPerMethod; statementIndex += 1)
      {
        builder.AppendLine($"    total += {statementIndex + 1};");
      }

      builder.AppendLine($"    return total > {methodIndex + statementsPerMethod} ? total : total + 1;");
      builder.AppendLine("  }");
      builder.AppendLine();
    }

    builder.AppendLine("}");
    return builder.ToString();
  }

}
