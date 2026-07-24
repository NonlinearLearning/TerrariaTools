using MinimalRoslynCpg.Builder;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgCatalogBatchWriterTests
{
  [Fact]
  public void BuildFromSource_LargeShardPayloadWithSmallCatalogMetadata_BatchesPublicationsTogether()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-catalog-batch-metadata-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var options = RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(
          root,
          "catalog-metadata-batch-profile",
          StreamingMode: true,
          MaxCatalogBatchRows: 4096,
          MaxCatalogBatchBytes: 8192),
      };
      var builder = new RoslynCpgBuilder(options);

      _ = builder.BuildFromSource(
        "class Example { int First() => 111111111 + 222222222; int Second() => 333333333 + 444444444; }",
        "input.cs");

      var persistence = Assert.IsType<CpgPersistenceTelemetry>(builder.LastBuildTelemetry.Persistence);
      Assert.True(
        persistence.CatalogBatchCount < 7,
        $"Expected metadata batching; batches={persistence.CatalogBatchCount}, metadataBytes={string.Join(',', persistence.CatalogBatchEstimatedMetadataBytes!)}.");
      Assert.True(persistence.CatalogBatchRows!.Count < 7);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void BuildFromSource_SmallShardPayloadsWithManyCatalogRows_SplitsByCatalogRows()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-catalog-batch-row-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var options = RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(
          root,
          "catalog-row-batch-profile",
          StreamingMode: true,
          MaxCatalogBatchRows: 20,
          MaxCatalogBatchBytes: 1024 * 1024),
      };
      var builder = new RoslynCpgBuilder(options);

      _ = builder.BuildFromSource(
        "class Example { int First() => 1; int Second() => 2; int Third() => 3; }",
        "input.cs");

      var persistence = Assert.IsType<CpgPersistenceTelemetry>(builder.LastBuildTelemetry.Persistence);
      Assert.True(persistence.CatalogBatchCount > 1);
      Assert.Equal(persistence.CatalogBatchCount, persistence.CatalogBatchPublicationCounts!.Count);
      Assert.Equal(persistence.CatalogBatchCount, persistence.CatalogBatchEstimatedMetadataBytes!.Count);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }

  [Fact]
  public void BuildFromSource_StreamingPersistence_ReportsCatalogTransactionBreakdown()
  {
    var root = Path.Combine(Path.GetTempPath(), "cpg-catalog-breakdown-tests", Guid.NewGuid().ToString("N"));
    try
    {
      var options = RoslynCpgBuilderOptions.CreateDefault() with
      {
        Persistence = new CpgPersistenceOptions(
          root,
          "catalog-breakdown-profile",
          StreamingMode: true,
          MaxCatalogBatchRows: 20),
      };
      var builder = new RoslynCpgBuilder(options);

      _ = builder.BuildFromSource(
        "class Example { int First(int value) => value + 1; int Second(int value) => First(value) + 2; }",
        "input.cs");

      var persistence = Assert.IsType<CpgPersistenceTelemetry>(builder.LastBuildTelemetry.Persistence);
      Assert.True(persistence.CatalogActualRowCount > 0);
      Assert.True(persistence.CatalogRowCount > persistence.CatalogActualRowCount);
      Assert.InRange(persistence.CatalogAffectedRowCount, 0, persistence.CatalogActualRowCount);
      Assert.True(persistence.CatalogStatementCount >= persistence.CatalogBatchCount);
      Assert.True(persistence.CatalogTransactionBeginMilliseconds >= 0);
      Assert.True(persistence.CatalogEnsureBuildingMilliseconds >= 0);
      Assert.True(persistence.CatalogFixedMetadataMilliseconds >= 0);
      Assert.Equal(0, persistence.CatalogNodeWriteMilliseconds);
      Assert.Equal(0, persistence.CatalogSpanWriteMilliseconds);
      Assert.Equal(0, persistence.CatalogSymbolWriteMilliseconds);
      Assert.Equal(0, persistence.CatalogBoundaryWriteMilliseconds);
      Assert.True(persistence.CatalogCommitTransactionMilliseconds >= 0);
      Assert.True(persistence.CatalogQueueSaturationMilliseconds >= 0);
      Assert.True(persistence.CatalogUnclassifiedMilliseconds >= 0);
    }
    finally
    {
      if (Directory.Exists(root))
      {
        Directory.Delete(root, recursive: true);
      }
    }
  }
}
