using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLCPG.Builder;
using NLCPG.Contracts;
using RoslynPrototype.Tests.TestCodeSet.Cpg;

namespace DataFlowTailMeasurement;

internal static class Program {
  private static readonly JsonSerializerOptions JsonOptions = new() {
    Converters = { new JsonStringEnumConverter() },
  };

  private static int Main(string[] args) {
    string output = Path.GetFullPath(args.Length == 1 ? args[0] :
        "Build/DataFlowTailMeasurement/run-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));
    Directory.CreateDirectory(output);
    var batch = Stopwatch.StartNew();
    using var batchTimeout = new Timer(_ => Timeout(output, "Batch", batch.Elapsed),
        null, TimeSpan.FromSeconds(45), System.Threading.Timeout.InfiniteTimeSpan);
    var rows = new List<Sample>();
    var snapshots = new Dictionary<string, (string Graph, string Publication)>();
    try {
      WriteJson(Path.Combine(output, "environment.json"), new {
        StartedUtc = DateTime.UtcNow, Environment.MachineName, Environment.ProcessorCount,
        Runtime = RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture, Stopwatch.Frequency,
        Configuration = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
        Binaries = Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
            .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetFileName(path)!,
                path => Hash(File.ReadAllBytes(path))),
        TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
        ReadyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),
        DataFlowBudget = NLCPGDataFlowOptions.Unbounded,
        SampleLimitSeconds = 5, BatchLimitSeconds = 45,
      });
      string sparse = DataFlowMeasurementSources.Sparse();
      string collision = DataFlowMeasurementSources.Collision();
      string joinLoop = DataFlowMeasurementSources.JoinLoop();
      foreach (var fixture in new[] { ("Sparse", sparse), ("Collision", collision),
          ("JoinLoop", joinLoop) }) {
        File.WriteAllText(Path.Combine(output, fixture.Item1 + ".cs"), fixture.Item2);
      }

      Run("Sparse", sparse, DataFlowDiagnosticMode.Detailed, 0, 1);
      for (int round = 1; round <= 3; round++) {
        Run("Sparse", sparse, DataFlowDiagnosticMode.Detailed, round, 1);
      }
      var modes = new[] { DataFlowDiagnosticMode.Off, DataFlowDiagnosticMode.Coarse,
          DataFlowDiagnosticMode.Detailed };
      foreach (var mode in modes) {
        Run("Collision", collision, mode, 0, 1);
      }
      // Latin rotation: O/C/D, C/D/O, D/O/C. Never infer phase time from mode differences.
      for (int round = 1; round <= 3; round++) {
        for (int position = 0; position < 3; position++) {
          Run("Collision", collision, modes[(position + round - 1) % 3], round, 1);
        }
      }
      Run("JoinLoop", joinLoop, DataFlowDiagnosticMode.Detailed, 0, 1);
      for (int round = 1; round <= 3; round++) {
        Run("JoinLoop", joinLoop, DataFlowDiagnosticMode.Detailed, round, 1);
      }
      Run("Sparse", sparse, DataFlowDiagnosticMode.Detailed, 4, 2);
      batch.Stop();
      WriteJson(Path.Combine(output, "summary.json"), new {
        Status = "Complete", Samples = rows.Count, BatchElapsedMs = batch.Elapsed.TotalMilliseconds,
        MaxSampleElapsedMs = rows.Max(row => row.SampleElapsedMs),
        Equivalence = rows.All(row => row.Equivalent),
        Coverage = rows.All(row => row.CoveragePassed),
        TimingInvariants = rows.All(row => row.TimingValid),
        Statistics = rows.Where(row => row.Round > 0 && row.Dop == 1)
            .GroupBy(row => (row.Fixture, row.Mode)).Select(group => new {
              group.Key.Fixture, group.Key.Mode,
              RawMs = group.Select(row => Milliseconds(row.Diagnostic.MethodTotalTicks)).ToArray(),
              MinMs = group.Min(row => Milliseconds(row.Diagnostic.MethodTotalTicks)),
              MedianMs = Median(group.Select(row => Milliseconds(row.Diagnostic.MethodTotalTicks))),
              MaxMs = group.Max(row => Milliseconds(row.Diagnostic.MethodTotalTicks)),
            }).ToArray(),
        Calibration = Enumerable.Range(1, 3).Select(round => {
          double coarse = MethodMilliseconds("Collision", DataFlowDiagnosticMode.Coarse, round);
          double detailed = MethodMilliseconds("Collision", DataFlowDiagnosticMode.Detailed, round);
          return new { Round = round, CoarseMs = coarse, DetailedMs = detailed,
            IncreasePercent = (detailed / coarse - 1) * 100 };
        }).ToArray(),
        FallbackCoverage = "Synthetic correctness contract only; no source fallback in these fixtures",
        RealNpcCoverage = "Not run; small-fixture evidence cannot establish NPC bottlenecks",
      });
      Console.WriteLine($"COMPLETE samples={rows.Count} elapsedMs={batch.Elapsed.TotalMilliseconds:F1}");
      return 0;
    } catch (Exception exception) {
      WriteJson(Path.Combine(output, "failure.json"), new {
        Status = "Incomplete", CompletedRecords = rows.Count, Exception = exception.ToString(),
        BatchElapsedMs = batch.Elapsed.TotalMilliseconds,
      });
      Console.Error.WriteLine(exception);
      return 1;
    }

    void Run(string fixture, string source, DataFlowDiagnosticMode mode, int round, int dop) {
      string runId = $"{rows.Count:D2}-{fixture}-{mode}-r{round}-dop{dop}";
      Console.WriteLine("BEGIN " + runId);
      var sampleClock = Stopwatch.StartNew();
      using var sampleTimeout = new Timer(_ => Timeout(output, runId, sampleClock.Elapsed),
          null, TimeSpan.FromSeconds(5), System.Threading.Timeout.InfiniteTimeSpan);
      var options = NLCPGBuilderOptions.CreateDefault() with {
        MaxDegreeOfParallelism = dop, LargeFileLineThreshold = 1, LargeFileMethodThreshold = 1,
        LargeMethodLineSpanThreshold = 1, SyntaxLargeFileLineThreshold = 1,
        DataFlowOptions = NLCPGDataFlowOptions.Unbounded,
      };
      var builder = new NLCPGBuilder(options) {
        DataFlowDiagnostics = new(mode, runId, Hash(Encoding.UTF8.GetBytes(source))),
      };
      var buildClock = Stopwatch.StartNew();
      var graph = builder.BuildFromSource(source, "measurement/" + fixture + ".cs");
      buildClock.Stop();
      var diagnostic = builder.LastDataFlowDiagnostics.Single();
      string snapshot = DataFlowGraphSnapshot.Capture(graph);
      string publication = JsonSerializer.Serialize(diagnostic.PublicationOrder, JsonOptions);
      bool equivalent = !snapshots.TryGetValue(fixture, out var baseline) ||
          (snapshot == baseline.Graph && publication == baseline.Publication);
      snapshots.TryAdd(fixture, (snapshot, publication));
      File.WriteAllText(Path.Combine(output, runId + ".graph.json"), snapshot);
      File.WriteAllText(Path.Combine(output, runId + ".publication.json"), publication);
      bool timingValid = diagnostic.PhaseTicks.Values.Sum() <= diagnostic.MethodTotalTicks &&
          diagnostic.DetailTicks.Values.Sum() <= diagnostic.PhaseTicks["CandidateLoop"];
      bool coveragePassed = mode != DataFlowDiagnosticMode.Detailed || Coverage(fixture, diagnostic);
      sampleClock.Stop();
      var row = new Sample(runId, fixture, mode, round, dop, round == 0,
          buildClock.Elapsed.TotalMilliseconds, sampleClock.Elapsed.TotalMilliseconds,
          equivalent, coveragePassed, timingValid, graph.Nodes.Count(), graph.Edges.Count(),
          graph.Edges.Count(edge => edge.Kind == NLCPGEdgeKind.DataFlow),
          graph.GraphSnapshotVersion, Hash(Encoding.UTF8.GetBytes(snapshot)),
          Hash(Encoding.UTF8.GetBytes(publication)), diagnostic);
      // Persist every completed record, including rejected records, before the next sample.
      File.AppendAllText(Path.Combine(output, "raw.jsonl"),
          JsonSerializer.Serialize(row, JsonOptions) + Environment.NewLine);
      rows.Add(row);
      Console.WriteLine($"END {runId} buildMs={row.BuildElapsedMs:F2} " +
          $"methodMs={Milliseconds(diagnostic.MethodTotalTicks):F3} equivalent={equivalent}");
      if (!equivalent || !timingValid || !coveragePassed || diagnostic.ExitReason != "Complete") {
        throw new InvalidOperationException($"Rejected sample {runId}: " +
            $"equivalent={equivalent}, timing={timingValid}, coverage={coveragePassed}, " +
            $"exit={diagnostic.ExitReason}");
      }
    }

    double MethodMilliseconds(string fixture, DataFlowDiagnosticMode mode, int round) {
      return Milliseconds(rows.Single(row => row.Fixture == fixture && row.Mode == mode &&
          row.Round == round && row.Dop == 1).Diagnostic.MethodTotalTicks);
    }
  }

  private static bool Coverage(string fixture, DataFlowDiagnosticResult result) {
    var counts = result.Counters;
    bool common = counts["IndexedReturns"] > 0 && counts["FactsMatchTrue"] > 0 &&
        result.Metrics.UniqueCandidateCount > 0 &&
        counts["RawCandidates"] == result.Metrics.RawCandidateCount &&
        counts["TouchedMarks"] == counts["TouchedClears"];
    return common && fixture switch {
      "Collision" => counts["LocationHits"] > 0 && counts["RootHits"] > 0 &&
          counts["SeenRejects"] > 0,
      "JoinLoop" => counts["Dequeues"] > result.Metrics.FlowNodeCount &&
          counts["CandidatePredecessorVisits"] > 0,
      _ => true,
    };
  }

  private static double Median(IEnumerable<double> values) {
    double[] sorted = values.Order().ToArray();
    return sorted[sorted.Length / 2];
  }

  private static double Milliseconds(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }
  private static string Hash(byte[] bytes) { return Convert.ToHexString(SHA256.HashData(bytes)); }

  private static void WriteJson(string path, object value) {
    File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
  }

  private static void Timeout(string output, string scope, TimeSpan elapsed) {
    WriteJson(Path.Combine(output, "timeout.json"), new {
      Status = "Incomplete", Scope = scope, ElapsedMs = elapsed.TotalMilliseconds,
    });
    Environment.Exit(124);
  }

  private sealed record Sample(
      string RunId, string Fixture, DataFlowDiagnosticMode Mode, int Round, int Dop, bool Warmup,
      double BuildElapsedMs, double SampleElapsedMs, bool Equivalent, bool CoveragePassed,
      bool TimingValid, int Nodes, int Edges, int DataFlowEdges, string GraphSnapshotVersion,
      string NormalizedGraphHash, string PublicationHash, DataFlowDiagnosticResult Diagnostic);
}
