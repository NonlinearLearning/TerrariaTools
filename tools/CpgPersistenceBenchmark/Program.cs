using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CpgPersistenceBenchmark;
using MinimalRoslynCpg.Builder;

var configuration = BenchmarkConfiguration.Parse(args);
var results = new List<BenchmarkCaseResult>();
var fixtures = BenchmarkFixture.CreateAll(
  configuration.SourceRoot,
  configuration.SourceRootMaxFiles,
  configuration.SourceRootMaxBytes)
  .Where(fixture => configuration.Fixtures.Count == 0 ||
    configuration.Fixtures.Contains(fixture.Name, StringComparer.OrdinalIgnoreCase))
  .ToArray();
if (fixtures.Length == 0)
{
  throw new ArgumentException("--fixture did not match a benchmark fixture.", "--fixture");
}

foreach (var fixture in fixtures)
{
  foreach (var durabilityMode in configuration.DurabilityModes)
  {
    foreach (var degreeOfParallelism in configuration.DegreesOfParallelism)
    {
      foreach (var shardExportConcurrency in configuration.ShardExportConcurrencyLimits)
      {
      foreach (var fileWriteConcurrency in configuration.FileWriteConcurrencyLimits)
      {
        foreach (var catalogBatchRows in configuration.CatalogBatchRowLimits)
        {
          for (var warmup = 0; warmup < configuration.WarmupCount; warmup += 1)
          {
            _ = BenchmarkCase.Run(
              fixture,
              durabilityMode,
              degreeOfParallelism,
              shardExportConcurrency,
              fileWriteConcurrency,
              catalogBatchRows,
              configuration.TemporaryStoreRoot);
          }

          var samples = Enumerable.Range(0, configuration.SampleCount)
            .Select(_ => BenchmarkCase.Run(
              fixture,
              durabilityMode,
              degreeOfParallelism,
              shardExportConcurrency,
              fileWriteConcurrency,
              catalogBatchRows,
              configuration.TemporaryStoreRoot))
            .OrderBy(sample => sample.ElapsedMilliseconds)
            .ToArray();
          results.Add(BenchmarkCaseResult.FromSamples(
            fixture.Name,
            durabilityMode,
            degreeOfParallelism,
            shardExportConcurrency,
            fileWriteConcurrency,
            catalogBatchRows,
            BenchmarkInputManifest.Create(fixture.Files, fixture.SourceRoot),
            samples));
        }
      }
      }
    }
  }
}

var report = new BenchmarkReport(
  DateTimeOffset.UtcNow,
  Environment.Version.ToString(),
  Environment.OSVersion.ToString(),
  configuration,
  results,
  BenchmarkComparison.Create(results));
var outputPath = configuration.OutputPath ?? Path.Combine(
  "Build",
  $"cpg-persistence-benchmark-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
{
  WriteIndented = true,
  Converters = { new JsonStringEnumConverter() },
}));
Console.WriteLine(outputPath);

internal sealed record BenchmarkFixture(
  string Name,
  IReadOnlyList<(string Source, string FilePath)> Files,
  IReadOnlyList<(string Source, string FilePath)>? ChangedFiles = null,
  bool StreamingMode = false,
  string? SourceRoot = null)
{
  internal static IReadOnlyList<BenchmarkFixture> CreateAll(
    string? sourceRoot,
    int? sourceRootMaxFiles,
    long? sourceRootMaxBytes)
  {
    var fixtures = new List<BenchmarkFixture>
    {
      new BenchmarkFixture("large-single-file", new[] { (CreateSource("Large", 96), "Large.cs") }),
      new BenchmarkFixture("multi-file", Enumerable.Range(0, 6)
        .Select(index => (CreateSource($"Part{index}", 16), $"Part{index}.cs"))
        .ToArray()),
      new BenchmarkFixture(
        "changed-method-reuse",
        new[] { (CreateSource("Reuse", 96), "Reuse.cs") },
        new[] { (CreateChangedMethodSource(), "Reuse.cs") },
        StreamingMode: true),
    };
    var repositorySourcePath = Path.Combine(
      "src", "MinimalRoslynCpg", "Builder", "Passes", "DataFlowPass.cs");
    if (File.Exists(repositorySourcePath))
    {
      var source = File.ReadAllText(repositorySourcePath);
      fixtures.Add(new BenchmarkFixture(
        "repository-dataflow-pass-reuse",
        new[] { (source, repositorySourcePath) },
        new[] { (ReplaceLastPropertyLiteral(source), repositorySourcePath) }));
    }

    if (sourceRoot is not null)
    {
      fixtures.Add(CreateSourceRootFixture(sourceRoot, sourceRootMaxFiles, sourceRootMaxBytes));
    }

    return fixtures;
  }

  private static BenchmarkFixture CreateSourceRootFixture(
    string sourceRoot,
    int? maxFiles,
    long? maxBytes)
  {
    var fullSourceRoot = Path.GetFullPath(sourceRoot);
    if (!Directory.Exists(fullSourceRoot))
    {
      throw new DirectoryNotFoundException($"Source root does not exist: {fullSourceRoot}");
    }

    var candidates = Directory.EnumerateFiles(fullSourceRoot, "*.cs", SearchOption.AllDirectories)
      .Where(path => !IsIgnoredSourcePath(path, fullSourceRoot))
      .OrderBy(path => path, StringComparer.Ordinal)
      .ToArray();
    var files = new List<(string Source, string FilePath)>();
    var selectedBytes = 0L;
    foreach (var path in candidates)
    {
      if (maxFiles.HasValue && files.Count >= maxFiles.Value)
      {
        break;
      }

      var bytes = new FileInfo(path).Length;
      if (maxBytes.HasValue && selectedBytes + bytes > maxBytes.Value && files.Count > 0)
      {
        break;
      }

      files.Add((File.ReadAllText(path), Path.GetRelativePath(fullSourceRoot, path)));
      selectedBytes += bytes;
    }
    if (files.Count == 0)
    {
      throw new ArgumentException($"Source root contains no C# files: {fullSourceRoot}", nameof(sourceRoot));
    }

    return new BenchmarkFixture("source-root", files, StreamingMode: true, SourceRoot: fullSourceRoot);
  }

  private static bool IsIgnoredSourcePath(string path, string sourceRoot)
  {
    var relativePath = Path.GetRelativePath(sourceRoot, path);
    return relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
      .Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase));
  }

  private static string CreateSource(string className, int methodCount)
  {
    var methods = string.Join(Environment.NewLine, Enumerable.Range(0, methodCount).Select(index =>
      $"  int Method{index}(int value) {{ var current = value + {index}; if (current > {index / 2}) current += Method{Math.Max(0, index - 1)}(current - 1); return current; }}"));
    return $"class {className} {{ int Method0(int value) => value;{Environment.NewLine}{methods}{Environment.NewLine}}}";
  }

  private static string CreateChangedMethodSource()
  {
    return CreateSource("Reuse", 96).Replace("value + 95", "value + 94", StringComparison.Ordinal);
  }

  private static string ReplaceLastPropertyLiteral(string source)
  {
    var index = source.LastIndexOf("\"property\"", StringComparison.Ordinal);
    if (index < 0)
    {
      throw new InvalidOperationException("The repository reuse fixture could not find its late-method edit.");
    }

    return string.Concat(source.AsSpan(0, index), "\"propertz\"", source.AsSpan(index + "\"property\"".Length));
  }
}

internal static class BenchmarkCase
{
  internal static BenchmarkSample Run(
    BenchmarkFixture fixture,
    CpgPersistenceDurabilityMode durabilityMode,
    int degreeOfParallelism,
    int shardExportConcurrency,
    int fileWriteConcurrency,
    int catalogBatchRows,
    string? temporaryStoreRoot)
  {
    var temporaryRoot = temporaryStoreRoot is null
      ? Path.GetTempPath()
      : Path.GetFullPath(temporaryStoreRoot);
    Directory.CreateDirectory(temporaryRoot);
    var storeRoot = Path.Combine(temporaryRoot, "cpg-persistence-benchmark", Guid.NewGuid().ToString("N"));
    try
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
      GC.Collect();
      var stopwatch = Stopwatch.StartNew();
      var nodeCount = 0;
      var edgeCount = 0;
      var persistence = new List<CpgPersistenceTelemetry>();
      var coldBuildMilliseconds = 0L;
      var incrementalBuildMilliseconds = 0L;
      if (fixture.ChangedFiles is null)
      {
        foreach (var (source, filePath) in fixture.Files)
        {
          var builder = CreateBuilder();
          var graph = builder.BuildFromSource(source, filePath);
          nodeCount += graph.Nodes.Count;
          edgeCount += graph.Edges.Count;
          persistence.Add(builder.LastBuildTelemetry.Persistence!);
        }
      }
      else
      {
        var coldStopwatch = Stopwatch.StartNew();
        foreach (var (source, filePath) in fixture.Files)
        {
          _ = CreateBuilder().BuildFromSource(source, filePath);
        }

        coldStopwatch.Stop();
        coldBuildMilliseconds = coldStopwatch.ElapsedMilliseconds;
        var incrementalStopwatch = Stopwatch.StartNew();
        foreach (var (source, filePath) in fixture.ChangedFiles)
        {
          var builder = CreateBuilder();
          var graph = builder.BuildFromSource(source, filePath);
          nodeCount += graph.Nodes.Count;
          edgeCount += graph.Edges.Count;
          persistence.Add(builder.LastBuildTelemetry.Persistence!);
        }

        incrementalStopwatch.Stop();
        incrementalBuildMilliseconds = incrementalStopwatch.ElapsedMilliseconds;
      }

      stopwatch.Stop();
      var process = Process.GetCurrentProcess();
      return new BenchmarkSample(
        stopwatch.ElapsedMilliseconds,
        GC.GetTotalMemory(forceFullCollection: false),
        process.WorkingSet64,
        nodeCount,
        edgeCount,
        Directory.EnumerateFiles(storeRoot, "*.cpgbin", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length),
        File.Exists(Path.Combine(storeRoot, "catalog.db"))
          ? new FileInfo(Path.Combine(storeRoot, "catalog.db")).Length
          : 0,
        Directory.EnumerateFiles(storeRoot, "routing.cpgidx", SearchOption.AllDirectories)
          .Sum(path => new FileInfo(path).Length),
        persistence.Sum(item => item.FileWriteMilliseconds),
        persistence.Sum(item => item.SerializationMilliseconds),
        persistence.Sum(item => item.ValidationMilliseconds),
        persistence.Sum(item => item.ReadBackMilliseconds),
        persistence.Sum(item => item.HashMilliseconds),
        persistence.Sum(item => item.StructuralValidationMilliseconds),
        persistence.Sum(item => item.FlushMilliseconds),
        persistence.Sum(item => item.CatalogCommitMilliseconds),
        persistence.Sum(item => item.StoreLockWaitMilliseconds),
        persistence.Max(item => item.PeakQueueDepth),
        persistence.Max(item => item.PeakConcurrentFileWrites),
        persistence.Max(item => item.PeakConcurrentShardExports),
        persistence.Sum(item => item.ReusedShardCount),
        persistence.Sum(item => item.ReuseMissCount),
        persistence.Sum(item => item.ReuseRejectedCount),
        persistence.Sum(item => item.ReusedShardBytes),
        coldBuildMilliseconds,
        incrementalBuildMilliseconds,
        persistence.Sum(item => item.PrimaryShardCount + item.BoundaryAdjacencyShardCount),
        persistence.Sum(item => item.CatalogBatchCount),
        persistence.Sum(item => item.CatalogActualRowCount),
        persistence.Sum(item => item.CatalogStatementCount),
        persistence.Sum(item => item.CatalogTransactionBeginMilliseconds),
        persistence.Sum(item => item.CatalogEnsureBuildingMilliseconds),
        persistence.Sum(item => item.CatalogFixedMetadataMilliseconds),
        persistence.Sum(item => item.CatalogNodeWriteMilliseconds),
        persistence.Sum(item => item.CatalogSpanWriteMilliseconds),
        persistence.Sum(item => item.CatalogSymbolWriteMilliseconds),
        persistence.Sum(item => item.CatalogBoundaryWriteMilliseconds),
        persistence.Sum(item => item.CatalogCommitTransactionMilliseconds),
        persistence.Sum(item => item.CatalogQueueSaturationMilliseconds),
        persistence.Sum(item => item.CatalogAffectedRowCount),
        persistence.Sum(item => item.CatalogUnclassifiedMilliseconds),
        persistence.Sum(item => item.CatalogRowMaterializationMilliseconds),
        persistence.Sum(item => item.CatalogRowMaterializationAllocatedBytes),
        persistence.Sum(item => item.CatalogSqlTextBuildMilliseconds),
        persistence.Sum(item => item.CatalogSqlTextBuildAllocatedBytes),
        persistence.Sum(item => item.CatalogCommandPrepareMilliseconds),
        persistence.Sum(item => item.CatalogCommandPrepareAllocatedBytes),
        persistence.Sum(item => item.CatalogExecuteNonQueryMilliseconds),
        persistence.Sum(item => item.CatalogExecuteNonQueryAllocatedBytes));
    }
    finally
    {
      if (Directory.Exists(storeRoot))
      {
        Directory.Delete(storeRoot, recursive: true);
      }
    }

    RoslynCpgBuilder CreateBuilder()
    {
      return new RoslynCpgBuilder(RoslynCpgBuilderOptions.CreateDefault() with
      {
        MaxDegreeOfParallelism = degreeOfParallelism,
        Persistence = new CpgPersistenceOptions(
          storeRoot,
          $"benchmark-{fixture.Name}-{durabilityMode}",
          StreamingMode: fixture.StreamingMode,
          MaxConcurrentShardExports: shardExportConcurrency,
          MaxConcurrentShardFileWrites: fileWriteConcurrency,
          MaxCatalogBatchRows: catalogBatchRows,
          DurabilityMode: durabilityMode),
      });
    }
  }
}

internal sealed record BenchmarkSample(
  long ElapsedMilliseconds,
  long ManagedHeapBytes,
  long WorkingSetBytes,
  int NodeCount,
  int EdgeCount,
  long ShardBytes,
  long CatalogBytes,
  long RoutingIndexBytes,
  long FileWriteMilliseconds,
  long SerializationMilliseconds,
  long ValidationMilliseconds,
  long ReadBackMilliseconds,
  long HashMilliseconds,
  long StructuralValidationMilliseconds,
  long FlushMilliseconds,
  long CatalogCommitMilliseconds,
  long StoreLockWaitMilliseconds,
  int PeakQueueDepth,
  int PeakConcurrentFileWrites,
  int PeakConcurrentShardExports,
  int ReusedShardCount,
  int ReuseMissCount,
  int ReuseRejectedCount,
  long ReusedShardBytes,
  long ColdBuildMilliseconds,
  long IncrementalBuildMilliseconds,
  int ShardCount,
  int CatalogBatchCount,
  long CatalogActualRowCount,
  long CatalogStatementCount,
  long CatalogTransactionBeginMilliseconds,
  long CatalogEnsureBuildingMilliseconds,
  long CatalogFixedMetadataMilliseconds,
  long CatalogNodeWriteMilliseconds,
  long CatalogSpanWriteMilliseconds,
  long CatalogSymbolWriteMilliseconds,
  long CatalogBoundaryWriteMilliseconds,
  long CatalogCommitTransactionMilliseconds,
  long CatalogQueueSaturationMilliseconds,
  long CatalogAffectedRowCount,
  long CatalogUnclassifiedMilliseconds,
  long CatalogRowMaterializationMilliseconds,
  long CatalogRowMaterializationAllocatedBytes,
  long CatalogSqlTextBuildMilliseconds,
  long CatalogSqlTextBuildAllocatedBytes,
  long CatalogCommandPrepareMilliseconds,
  long CatalogCommandPrepareAllocatedBytes,
  long CatalogExecuteNonQueryMilliseconds,
  long CatalogExecuteNonQueryAllocatedBytes);

internal sealed record BenchmarkCaseResult(
  string Fixture,
  CpgPersistenceDurabilityMode DurabilityMode,
  int DegreeOfParallelism,
  int ShardExportConcurrency,
  int FileWriteConcurrency,
  int CatalogBatchRows,
  BenchmarkInputManifest InputManifest,
  BenchmarkSample Median,
  IReadOnlyList<BenchmarkSample> Samples)
{
  internal static BenchmarkCaseResult FromSamples(
    string fixture,
    CpgPersistenceDurabilityMode durabilityMode,
    int degreeOfParallelism,
    int shardExportConcurrency,
    int fileWriteConcurrency,
    int catalogBatchRows,
    BenchmarkInputManifest inputManifest,
    IReadOnlyList<BenchmarkSample> samples)
  {
    return new BenchmarkCaseResult(
      fixture,
      durabilityMode,
      degreeOfParallelism,
      shardExportConcurrency,
      fileWriteConcurrency,
      catalogBatchRows,
      inputManifest,
      samples[samples.Count / 2],
      samples);
  }
}

internal sealed record BenchmarkReport(
  DateTimeOffset GeneratedAtUtc,
  string RuntimeVersion,
  string OperatingSystem,
  BenchmarkConfiguration Configuration,
  IReadOnlyList<BenchmarkCaseResult> Cases,
  IReadOnlyList<BenchmarkComparison> Comparisons);

internal sealed record BenchmarkComparison(
  string ComparisonKind,
  string Fixture,
  CpgPersistenceDurabilityMode DurabilityMode,
  int? DegreeOfParallelism,
  int ShardExportConcurrency,
  int FileWriteConcurrency,
  int? CatalogBatchRows,
  bool IsComparable,
  string? Blocker)
{
  internal static IReadOnlyList<BenchmarkComparison> Create(
    IReadOnlyList<BenchmarkCaseResult> cases)
  {
    var dopComparisons = CreateComparisons(
      cases,
      "dop",
      item => new
      {
        item.Fixture,
        item.DurabilityMode,
        item.ShardExportConcurrency,
        item.FileWriteConcurrency,
        item.CatalogBatchRows,
      },
      group => group.Select(item => item.DegreeOfParallelism).Distinct().Count() > 1);
    var batchComparisons = CreateComparisons(
      cases,
      "catalog-batch-rows",
      item => new
      {
        item.Fixture,
        item.DurabilityMode,
        item.DegreeOfParallelism,
        item.ShardExportConcurrency,
        item.FileWriteConcurrency,
      },
      group => group.Select(item => item.CatalogBatchRows).Distinct().Count() > 1);
    return dopComparisons.Concat(batchComparisons)
      .OrderBy(item => item.ComparisonKind, StringComparer.Ordinal)
      .ThenBy(item => item.Fixture, StringComparer.Ordinal)
      .ThenBy(item => item.DurabilityMode)
      .ThenBy(item => item.ShardExportConcurrency)
      .ThenBy(item => item.FileWriteConcurrency)
      .ThenBy(item => item.CatalogBatchRows)
      .ToArray();
  }

  private static IReadOnlyList<BenchmarkComparison> CreateComparisons<TKey>(
    IReadOnlyList<BenchmarkCaseResult> cases,
    string comparisonKind,
    Func<BenchmarkCaseResult, TKey> groupKey,
    Func<IGrouping<TKey, BenchmarkCaseResult>, bool> include)
    where TKey : notnull
  {
    return cases
      .GroupBy(groupKey)
      .Where(include)
      .Select(group =>
      {
        var first = group.First();
        var inputs = group.Select(item => item.InputManifest.ContentHash).Distinct(StringComparer.Ordinal).Count();
        var roots = group.Select(item => item.InputManifest.SourceRoot).Distinct(StringComparer.Ordinal).Count();
        var graphScales = group.Select(item => new
        {
          item.Median.NodeCount,
          item.Median.EdgeCount,
          item.Median.ShardCount,
        }).Distinct().Count();
        var blocker = inputs != 1
          ? "input-manifest-mismatch"
          : roots != 1
            ? "source-root-mismatch"
            : graphScales != 1
              ? "graph-scale-mismatch"
              : null;
        return new BenchmarkComparison(
          comparisonKind,
          first.Fixture,
          first.DurabilityMode,
          comparisonKind == "catalog-batch-rows" ? first.DegreeOfParallelism : null,
          first.ShardExportConcurrency,
          first.FileWriteConcurrency,
          comparisonKind == "dop" ? first.CatalogBatchRows : null,
          blocker is null,
          blocker);
      })
      .ToArray();
  }
}
