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
    });

    Assert.Equal(new[] { "large-single-file" }, configuration.Fixtures);
    Assert.Equal(new[] { CpgPersistenceDurabilityMode.Strict }, configuration.DurabilityModes);
    Assert.Equal(new[] { 12 }, configuration.DegreesOfParallelism);
    Assert.Equal(new[] { 4 }, configuration.ShardExportConcurrencyLimits);
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
}
