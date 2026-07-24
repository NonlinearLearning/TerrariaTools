using CpgPersistenceBenchmark;
using MinimalRoslynCpg.Builder;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class CpgPersistenceBenchmarkConfigurationTests
{
  [Fact]
  public void Parse_WhenFileWriteConcurrencyIsOmitted_UsesCurrentDefault()
  {
    var configuration = BenchmarkConfiguration.Parse(Array.Empty<string>());

    Assert.Equal(new[] { 2 }, configuration.FileWriteConcurrencyLimits);
  }

  [Fact]
  public void Parse_WhenFileWriteConcurrencyIsSpecified_UsesDistinctSortedLimits()
  {
    var configuration = BenchmarkConfiguration.Parse(new[]
    {
      "--file-write-concurrency", "4,1,2,4,6",
    });

    Assert.Equal(new[] { 1, 2, 4, 6 }, configuration.FileWriteConcurrencyLimits);
  }

  [Fact]
  public void Parse_WhenBenchmarkFiltersAreSpecified_UsesRequestedValues()
  {
    var configuration = BenchmarkConfiguration.Parse(new[]
    {
      "--fixture", "large-single-file",
      "--durability", "Strict",
      "--dop", "12",
      "--shard-export-concurrency", "4",
      "--warmup-count", "0",
      "--sample-count", "1",
    });

    Assert.Equal(new[] { "large-single-file" }, configuration.Fixtures);
    Assert.Equal(new[] { CpgPersistenceDurabilityMode.Strict }, configuration.DurabilityModes);
    Assert.Equal(new[] { 12 }, configuration.DegreesOfParallelism);
    Assert.Equal(new[] { 4 }, configuration.ShardExportConcurrencyLimits);
    Assert.Equal(0, configuration.WarmupCount);
    Assert.Equal(1, configuration.SampleCount);
  }

  [Fact]
  public void Parse_WhenSourceRootIsSpecified_UsesRequestedDirectory()
  {
    var configuration = BenchmarkConfiguration.Parse(new[]
    {
      "--source-root", @"D:\\sources\\terraria",
    });

    Assert.Equal(@"D:\\sources\\terraria", configuration.SourceRoot);
  }

  [Fact]
  public void Parse_WhenSourceRootLimitsAreSpecified_UsesRequestedDeterministicCaps()
  {
    var configuration = BenchmarkConfiguration.Parse(new[]
    {
      "--source-root", @"D:\\sources\\terraria",
      "--source-root-max-files", "12",
      "--source-root-max-bytes", "1048576",
    });

    Assert.Equal(12, configuration.SourceRootMaxFiles);
    Assert.Equal(1048576, configuration.SourceRootMaxBytes);
  }

  [Fact]
  public void Parse_WhenTemporaryStoreRootIsSpecified_UsesRequestedDirectory()
  {
    var configuration = BenchmarkConfiguration.Parse(new[]
    {
      "--temporary-store-root", @"D:\\cpg-benchmark-store",
    });

    Assert.Equal(@"D:\\cpg-benchmark-store", configuration.TemporaryStoreRoot);
  }

  [Fact]
  public void Parse_WhenCatalogBatchRowsAreSpecified_UsesDistinctSortedLimits()
  {
    var configuration = BenchmarkConfiguration.Parse(new[]
    {
      "--catalog-batch-rows", "8192,1024,8192,4096",
    });

    Assert.Equal(new[] { 1024, 4096, 8192 }, configuration.CatalogBatchRowLimits);
  }

  [Fact]
  public void InputManifest_WhenFilesAreReordered_IsStableAndDetectsContentChanges()
  {
    var first = BenchmarkInputManifest.Create(new[]
    {
      ("class Second { }", "Second.cs"),
      ("class First { }", "First.cs"),
    });
    var reordered = BenchmarkInputManifest.Create(new[]
    {
      ("class First { }", "First.cs"),
      ("class Second { }", "Second.cs"),
    });
    var changed = BenchmarkInputManifest.Create(new[]
    {
      ("class First { }", "First.cs"),
      ("class Second { int Value => 1; }", "Second.cs"),
    });

    Assert.Equal(first.ContentHash, reordered.ContentHash);
    Assert.Equal(first.Files, reordered.Files);
    Assert.Equal(new[] { "First.cs", "Second.cs" }, first.Files.Select(file => file.RelativePath));
    Assert.NotEqual(first.ContentHash, changed.ContentHash);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-1")]
  [InlineData("1,0")]
  [InlineData("invalid")]
  public void Parse_WhenFileWriteConcurrencyIsInvalid_ThrowsArgumentException(string value)
  {
    Assert.Throws<ArgumentException>(() => BenchmarkConfiguration.Parse(new[]
    {
      "--file-write-concurrency", value,
    }));
  }

  [Theory]
  [InlineData("--warmup-count", "-1")]
  [InlineData("--warmup-count", "invalid")]
  [InlineData("--sample-count", "0")]
  [InlineData("--sample-count", "-1")]
  [InlineData("--sample-count", "invalid")]
  public void Parse_WhenLoopCountsAreInvalid_ThrowsArgumentException(string optionName, string value)
  {
    Assert.Throws<ArgumentException>(() => BenchmarkConfiguration.Parse(new[]
    {
      optionName, value,
    }));
  }
}
