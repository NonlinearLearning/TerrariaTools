using MinimalRoslynCpg.Builder;
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
