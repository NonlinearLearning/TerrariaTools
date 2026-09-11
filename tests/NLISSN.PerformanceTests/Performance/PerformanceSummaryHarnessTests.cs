using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NLISSN.Core.Performance;
using NLISSN.Hosting;
using NLISSN.Performance;
using Xunit;
using Xunit.Abstractions;

namespace RoslynPrototype.Tests.Performance;

public sealed class PerformanceSummaryHarnessTests : IDisposable
{
  private static readonly DopCase[] DopMatrix =
  [
    new("dop-1-1", 1, 1),
    new("dop-1-12", 1, 12),
    new("dop-12-1", 12, 1),
    new("dop-12-12", 12, 12)
  ];

  private readonly string _fixtureRoot = Path.Combine(
    Path.GetTempPath(),
    $"nlissn-performance-harness-{Guid.NewGuid():N}");
  private readonly string _artifactRoot = Path.Combine(
    Path.GetTempPath(),
    $"nlissn-performance-harness-artifacts-{Guid.NewGuid():N}");
  private readonly ITestOutputHelper _output;
  private readonly IReadOnlyDictionary<string, string> _fixtureSources;

  public PerformanceSummaryHarnessTests(ITestOutputHelper output)
  {
    _output = output;
    Directory.CreateDirectory(_fixtureRoot);
    Directory.CreateDirectory(_artifactRoot);
    _fixtureSources = CreateFixture();
  }

  [Fact]
  public async Task FixedFixture_CollectsWarmupMeasurementsAndEquivalentDopSnapshots()
  {
    var allSamples = new List<HarnessSample>();
    foreach (var dop in DopMatrix)
    {
      for (var sampleNumber = 1; sampleNumber <= 4; sampleNumber++)
      {
        var isWarmup = sampleNumber == 1;
        allSamples.Add(await RunSampleAsync(dop, sampleNumber, isWarmup));
      }
    }

    var runnableSamples = allSamples.Where(sample => !sample.IsSkipped).ToArray();
    Assert.Equal(DopMatrix.Length * 4, runnableSamples.Length);
    Assert.All(DopMatrix, dop =>
    {
      var samples = runnableSamples
        .Where(sample => sample.Dop == dop)
        .OrderBy(sample => sample.Report!.SampleNumber)
        .ToArray();
      Assert.Equal(4, samples.Length);
      Assert.Single(samples, sample => sample.IsWarmup);
      Assert.Equal(3, samples.Count(sample => !sample.IsWarmup));
    });

    var baselineSnapshot = runnableSamples[0].BusinessSnapshot;
    Assert.All(runnableSamples, sample =>
      Assert.Equal(baselineSnapshot, sample.BusinessSnapshot));

    foreach (var dop in DopMatrix)
    {
      var reports = runnableSamples
        .Where(sample => sample.Dop == dop)
        .Select(sample => sample.Report!)
        .OrderBy(report => report.SampleNumber)
        .ToArray();
      var baseline = reports.Single(report => report.IsWarmup == false && report.SampleNumber == 2);
      var comparedReports = reports
        .Select(report =>
        {
          var comparison = PerformanceEquivalenceChecker.Compare(baseline, report);
          return report.WithComparison(comparison.IsEligible, comparison.ReasonCodes);
        })
        .ToArray();
      var measurements = comparedReports.Where(report => !report.IsWarmup).ToArray();

      Assert.All(measurements, report => Assert.True(report.ComparisonEligible));
      Assert.All(measurements.Skip(1), report =>
        Assert.True(PerformanceEquivalenceChecker.Compare(measurements[0], report).IsEligible));

      var aggregate = PerformanceSampleAggregator.Aggregate(
        comparedReports,
        PerformanceMode.Benchmark);
      Assert.Equal(4, aggregate.RawReports.Count);
      Assert.Single(aggregate.WarmupRunIds);
      Assert.Equal(3, aggregate.MeasurementCount);
      Assert.False(aggregate.IsLowConfidence);
      Assert.NotNull(aggregate.Wall);
      Assert.Equal(3, aggregate.Wall!.Count);
      Assert.Equal(3, aggregate.Wall.RawValues.Count);

      var summaryPath = Path.Combine(
        _artifactRoot,
        dop.Name,
        "Performance",
        "summary.json");
      new PerformanceSummaryWriter().WriteAtomic(summaryPath, measurements[0], aggregate);
      Assert.True(File.Exists(summaryPath));
      using var json = JsonDocument.Parse(File.ReadAllText(summaryPath));
      var root = json.RootElement;
      Assert.Equal("benchmark", root.GetProperty("mode").GetString());
      Assert.Equal(3, root.GetProperty("sampleAggregate").GetProperty("measurementCount").GetInt32());
      Assert.True(root.GetProperty("sampleAggregate").GetProperty("isFormalStatisticsEligible").GetBoolean());
      Assert.Equal(
        new[] { "dop-1-1-measurement-1", "dop-1-1-measurement-2", "dop-1-1-measurement-3" }
          .Select(runId => runId.Replace("dop-1-1", dop.Name, StringComparison.Ordinal)),
        root.GetProperty("sampleAggregate")
          .GetProperty("rawMeasurements")
          .EnumerateArray()
          .Select(sample => sample.GetProperty("runId").GetString()));

      _output.WriteLine(
        $"dop={dop.Name}; runIds={string.Join(",", measurements.Select(report => report.RunId))}; " +
        $"mode=benchmark; warmup={reports.Single(report => report.IsWarmup).RunId}; " +
        $"eligible={string.Join(",", measurements.Select(report => report.ComparisonEligible))}; " +
        $"summaryPath={summaryPath}");
    }

    AssertFixtureWasNotModified();
  }

  [Fact]
  public void UnsupportedDopIsSkippedWithoutCreatingAZeroMeasurement()
  {
    var unsupported = new DopCase("unsupported", 0, 12);
    var sample = CreateSkippedSample(unsupported, "DOP must be positive");

    Assert.True(sample.IsSkipped);
    Assert.Null(sample.Report);
    Assert.Null(sample.BusinessSnapshot);
    Assert.Equal("unsupported", sample.Dop.Name);
    Assert.Equal("DOP must be positive", sample.SkipReason);
  }

  public void Dispose()
  {
    if (Directory.Exists(_fixtureRoot))
    {
      Directory.Delete(_fixtureRoot, recursive: true);
    }

    if (Directory.Exists(_artifactRoot))
    {
      Directory.Delete(_artifactRoot, recursive: true);
    }
  }

  private async Task<HarnessSample> RunSampleAsync(
    DopCase dop,
    int sampleNumber,
    bool isWarmup)
  {
    if (dop.DirectoryDop <= 0 || dop.CpgDop <= 0)
    {
      return CreateSkippedSample(dop, "DOP must be positive");
    }

    var runId = $"{dop.Name}-{(isWarmup ? "warmup" : $"measurement-{sampleNumber - 1}")}";
    var outcome = await new CommandHost(RulePipelineTestFactory.Create()).AnalyzeOutcomeFromArgsAsync(
      new[]
      {
        _fixtureRoot,
        "--max-degree-of-parallelism",
        dop.DirectoryDop.ToString(),
        "--cpg-max-degree-of-parallelism",
        dop.CpgDop.ToString(),
        "--performance-mode",
        "benchmark",
        "--target-name",
        "s, other, s",
        "--run-id",
        runId,
        "--no-diff"
      });
    var businessSnapshot = PerformanceOptimizationRegressionTests.CreateResultSnapshot(outcome.Result);
    var report = CreateComparableReport(
      outcome.Performance,
      dop,
      runId,
      sampleNumber,
      isWarmup,
      businessSnapshot);
    return new HarnessSample(dop, report, businessSnapshot, null);
  }

  private static HarnessSample CreateSkippedSample(DopCase dop, string reason)
  {
    return new HarnessSample(dop, null, null, reason);
  }

  private IReadOnlyDictionary<string, string> CreateFixture()
  {
    var sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["Box.cs"] = """
        public sealed class Box
        {
          public int Left { get; }
          public int Right { get; }
          public bool Ready { get; }
          public Box Next { get; }
        }
        """,
      ["Sample.cs"] = """
        public sealed class Sample
        {
          public int Run(Box s, Box other, bool fallback)
          {
            var value = s.Next.Left + s.Right + other.Left;
            return s.Ready && other.Ready && fallback ? value : s.Next.Right;
          }
        }
        """
    };
    foreach (var (fileName, source) in sources)
    {
      File.WriteAllText(Path.Combine(_fixtureRoot, fileName), source);
    }

    return sources.ToDictionary(
      entry => Path.Combine(_fixtureRoot, entry.Key),
      entry => entry.Value,
      StringComparer.Ordinal);
  }

  private static RunPerformanceReport CreateComparableReport(
    RunPerformanceReport report,
    DopCase dop,
    string runId,
    int sampleNumber,
    bool isWarmup,
    string businessSnapshot)
  {
    var snapshotHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(businessSnapshot)));
    var identity = new PerformanceRunIdentity(
      snapshotHash,
      "performance-harness-rules",
      "performance-harness-capabilities",
      "disabled",
      Environment.Version.ToString(),
      Environment.Version.ToString(),
      Environment.OSVersion.VersionString,
      Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? Environment.MachineName,
      "performance-harness-environment",
      dop.DirectoryDop,
      dop.CpgDop,
      1,
      PerformanceMode.Benchmark,
      false,
      snapshotHash,
      snapshotHash,
      snapshotHash);
    return new RunPerformanceReport(
      runId,
      report.InputKind,
      report.InputIdentity,
      report.Items,
      report.RootStage,
      report.TerminalSummary,
      report.TerminalStatus,
      PerformanceMode.Benchmark,
      sampleNumber,
      isWarmup,
      comparisonEligible: false,
      comparisonReasons: Array.Empty<string>(),
      report.Directory,
      report.Stages,
      report.Resources,
      report.Attachments,
      identity);
  }

  private void AssertFixtureWasNotModified()
  {
    var actualPaths = Directory.EnumerateFiles(_fixtureRoot, "*.cs", SearchOption.TopDirectoryOnly)
      .OrderBy(path => path, StringComparer.Ordinal)
      .ToArray();
    Assert.Equal(
      _fixtureSources.Keys.OrderBy(path => path, StringComparer.Ordinal),
      actualPaths);
    foreach (var (path, expectedSource) in _fixtureSources)
    {
      Assert.Equal(expectedSource, File.ReadAllText(path));
    }

    Assert.DoesNotContain(
      Directory.EnumerateFiles(_fixtureRoot, "*", SearchOption.AllDirectories),
      path => !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
  }

  private sealed record DopCase(string Name, int DirectoryDop, int CpgDop);

  private sealed record HarnessSample(
    DopCase Dop,
    RunPerformanceReport? Report,
    string? BusinessSnapshot,
    string? SkipReason)
  {
    public bool IsSkipped => Report is null;

    public bool IsWarmup => Report?.IsWarmup == true;
  }
}
