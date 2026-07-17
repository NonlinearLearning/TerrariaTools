using System.Diagnostics;
using System.Text;
using RoslynPrototype.Application;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class TextLogSystemTests : IDisposable
{
    private readonly string _tempDirectory;

    public TextLogSystemTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"roslyn-prototype-text-log-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task AnalyzeFromArgs_WithBenchmarkProfile_WritesRunAndAnalysisTextLogs()
    {
        var sourcePath = WriteSourceFile("benchmark-profile.cs");
        var runtimeLogPath = Path.Combine(_tempDirectory, "runtime.log");
        var analysisLogPath = Path.Combine(_tempDirectory, "analysis.log");
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());

        await host.AnalyzeFromArgsAsync(new[]
        {
            sourcePath,
            "--target-name",
            "s",
            "--skip-rewrite",
            "--no-diff",
            "--runtime-log",
            runtimeLogPath,
            "--analysis-log",
            analysisLogPath,
            "--log-profile",
            "benchmark"
        });

        var runtimeLines = ReadNonEmptyLines(runtimeLogPath);
        var analysisLines = ReadNonEmptyLines(analysisLogPath);

        Assert.Contains(runtimeLines, line => line.Contains("cat=run evt=started", StringComparison.Ordinal));
        Assert.Contains(runtimeLines, line => line.Contains("cat=run evt=sampled", StringComparison.Ordinal));
        Assert.Contains(runtimeLines, line => line.Contains("cat=run evt=completed", StringComparison.Ordinal));
        Assert.Contains(runtimeLines, line => line.Contains("cat=cpg evt=summary", StringComparison.Ordinal));
        Assert.Contains(runtimeLines, line => line.Contains("cat=mark evt=summary", StringComparison.Ordinal));
        Assert.Contains(runtimeLines, line => line.Contains("cat=diag evt=summary", StringComparison.Ordinal));
        Assert.Contains(runtimeLines, line => line.Contains("cat=io evt=summary", StringComparison.Ordinal));
        Assert.Contains(analysisLines, line => line.Contains("cat=file evt=completed", StringComparison.Ordinal));
        Assert.Contains(analysisLines, line => line.Contains("cat=phase evt=completed", StringComparison.Ordinal));
        Assert.Contains(analysisLines, line => line.Contains("cat=memory evt=snapshot", StringComparison.Ordinal));
        Assert.Contains(analysisLines, line => line.Contains("cat=io evt=summary", StringComparison.Ordinal));
        AssertHeaderOrder(runtimeLines[0]);
        AssertHeaderOrder(analysisLines[0]);
    }

    [Fact]
    public async Task AnalyzeFromArgs_ForDirectoryBenchmarkProfile_UsesWallClockCompletionAndAggregatedSummaries()
    {
        var sourceDirectory = WriteDirectorySources();
        var runtimeLogPath = Path.Combine(_tempDirectory, "runtime-directory.log");
        var analysisLogPath = Path.Combine(_tempDirectory, "analysis-directory.log");
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());
        var stopwatch = Stopwatch.StartNew();

        await host.AnalyzeFromArgsAsync(new[]
        {
            sourceDirectory,
            "--target-name",
            "s",
            "--skip-rewrite",
            "--no-diff",
            "--max-degree-of-parallelism",
            "2",
            "--runtime-log",
            runtimeLogPath,
            "--analysis-log",
            analysisLogPath,
            "--log-profile",
            "benchmark"
        });

        stopwatch.Stop();
        var runtimeLines = ReadNonEmptyLines(runtimeLogPath);
        var completed = ParseLine(runtimeLines.Single(line =>
          line.Contains("cat=run evt=completed", StringComparison.Ordinal)));
        var finalSample = ParseLine(runtimeLines.Last(line =>
          line.Contains("cat=run evt=sampled", StringComparison.Ordinal)));
        var cpgSummary = ParseLine(runtimeLines.Single(line =>
          line.Contains("cat=cpg evt=summary", StringComparison.Ordinal)));
        var markSummary = ParseLine(runtimeLines.Single(line =>
          line.Contains("cat=mark evt=summary", StringComparison.Ordinal)));

        Assert.True(long.Parse(cpgSummary["nodes"]) > 0);
        Assert.True(long.Parse(cpgSummary["edges"]) > 0);
        Assert.True(long.Parse(markSummary["rules"]) > 0);
        Assert.True(long.Parse(completed["elapsedMs"]) > 0);
        Assert.InRange(
          long.Parse(completed["elapsedMs"]),
          0,
          long.Parse(finalSample["elapsedMs"]) + 1000);
        Assert.InRange(
          long.Parse(completed["elapsedMs"]),
          0,
          stopwatch.ElapsedMilliseconds + 2000);
    }

    [Fact]
    public async Task AnalyzeFromArgs_WithLegacyRuntimeAndAnalysisPaths_StillWritesTextLogs()
    {
        var sourcePath = WriteSourceFile("legacy-alias.cs");
        var runtimeLogPath = Path.Combine(_tempDirectory, "runtime-legacy.log");
        var analysisLogPath = Path.Combine(_tempDirectory, "analysis-legacy.log");
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());

        await host.AnalyzeFromArgsAsync(new[]
        {
            sourcePath,
            "--target-name",
            "s",
            "--skip-rewrite",
            "--no-diff",
            "--runtime-metrics-log",
            runtimeLogPath,
            "--per-file-memory-diagnostics-log",
            analysisLogPath,
            "--log-profile",
            "benchmark"
        });

        var runtimeLines = ReadNonEmptyLines(runtimeLogPath);
        var analysisLines = ReadNonEmptyLines(analysisLogPath);

        Assert.Contains(runtimeLines, line => line.Contains("cat=run evt=completed", StringComparison.Ordinal));
        Assert.Contains(analysisLines, line => line.Contains("cat=memory evt=snapshot", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeFromArgs_WithRunCategoryFilter_SuppressesCpgAndMarkRuntimeEvents()
    {
        var sourcePath = WriteSourceFile("run-category-only.cs");
        var runtimeLogPath = Path.Combine(_tempDirectory, "runtime-run-only.log");
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());

        await host.AnalyzeFromArgsAsync(new[]
        {
            sourcePath,
            "--target-name",
            "s",
            "--skip-rewrite",
            "--no-diff",
            "--runtime-log",
            runtimeLogPath,
            "--log-profile",
            "benchmark",
            "--log-categories",
            "run"
        });

        var runtimeLines = ReadNonEmptyLines(runtimeLogPath);

        Assert.Contains(runtimeLines, line => line.Contains("cat=run evt=started", StringComparison.Ordinal));
        Assert.Contains(runtimeLines, line => line.Contains("cat=run evt=sampled", StringComparison.Ordinal));
        Assert.Contains(runtimeLines, line => line.Contains("cat=run evt=completed", StringComparison.Ordinal));
        Assert.DoesNotContain(runtimeLines, line => line.Contains("cat=cpg", StringComparison.Ordinal));
        Assert.DoesNotContain(runtimeLines, line => line.Contains("cat=mark", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeFromArgs_WithCompletedEventFilter_WritesOnlyCompletedRuntimeEvents()
    {
        var sourcePath = WriteSourceFile("completed-events-only.cs");
        var runtimeLogPath = Path.Combine(_tempDirectory, "runtime-completed-only.log");
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());

        await host.AnalyzeFromArgsAsync(new[]
        {
            sourcePath,
            "--target-name",
            "s",
            "--skip-rewrite",
            "--no-diff",
            "--runtime-log",
            runtimeLogPath,
            "--log-profile",
            "benchmark",
            "--log-events",
            "completed"
        });

        var runtimeLines = ReadNonEmptyLines(runtimeLogPath);

        Assert.Single(runtimeLines);
        Assert.True(runtimeLines[0].Contains("cat=run evt=completed", StringComparison.Ordinal));
        Assert.False(runtimeLines[0].Contains("evt=started", StringComparison.Ordinal));
        Assert.False(runtimeLines[0].Contains("evt=sampled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeFromArgs_WithCompactView_ElidesCpgSummaryDetails()
    {
        var sourcePath = WriteSourceFile("compact-view.cs");
        var runtimeLogPath = Path.Combine(_tempDirectory, "runtime-compact.log");
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());

        await host.AnalyzeFromArgsAsync(new[]
        {
            sourcePath,
            "--target-name",
            "s",
            "--skip-rewrite",
            "--no-diff",
            "--runtime-log",
            runtimeLogPath,
            "--log-profile",
            "benchmark",
            "--log-view",
            "compact"
        });

        var runtimeLines = ReadNonEmptyLines(runtimeLogPath);

        Assert.Contains(runtimeLines, line => line.Contains("cat=cpg evt=summary", StringComparison.Ordinal));
        Assert.DoesNotContain(runtimeLines, line => line.Contains("nodes=", StringComparison.Ordinal));
        Assert.DoesNotContain(runtimeLines, line => line.Contains("edges=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeFromArgs_WithInvalidLogCategory_FailsFast()
    {
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
          await host.AnalyzeFromArgsAsync(new[]
          {
            "--runtime-log",
            Path.Combine(_tempDirectory, "invalid.log"),
            "--log-categories",
            "run,unknown"
          }));
    }

    [Fact]
    public async Task AnalyzeFromArgs_WithLegacyRuntimeLog_WritesDeprecationWarning()
    {
        var sourcePath = WriteSourceFile("legacy-warning.cs");
        var runtimeLogPath = Path.Combine(_tempDirectory, "runtime-warning.log");
        var host = new DeletionCommandHost(RuleRegistry.CreateDefaultRules());
        var originalError = Console.Error;
        var errorWriter = new StringWriter();

        try
        {
            Console.SetError(errorWriter);
            await host.AnalyzeFromArgsAsync(new[]
            {
                sourcePath,
                "--target-name",
                "s",
                "--skip-rewrite",
                "--no-diff",
                "--runtime-metrics-log",
                runtimeLogPath
            });
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains("--runtime-metrics-log is deprecated", errorWriter.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDirectory))
        {
            return;
        }

        Directory.Delete(_tempDirectory, recursive: true);
    }

    private string WriteSourceFile(string fileName)
    {
        var filePath = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(
          filePath,
          """
          namespace Demo;

          public sealed class Sample
          {
            public int Run(Box s)
            {
              return s.Seed + 1;
            }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """,
          Encoding.UTF8);
        return filePath;
    }

    private string WriteDirectorySources()
    {
        var directoryPath = Path.Combine(_tempDirectory, "directory-input");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(
          Path.Combine(directoryPath, "DirectorySample.cs"),
          """
          namespace Demo;

          public sealed class DirectorySample
          {
            public int Run(Box s)
            {
              return s.Seed + 1;
            }
          }

          public sealed class Box
          {
            public int Seed { get; set; }
          }
          """,
          Encoding.UTF8);
        return directoryPath;
    }

    private static IReadOnlyList<string> ReadNonEmptyLines(string filePath)
    {
        return File.ReadAllLines(filePath)
          .Where(line => !string.IsNullOrWhiteSpace(line))
          .ToList();
    }

    private static void AssertHeaderOrder(string line)
    {
        var fields = SplitFields(line);
        Assert.True(fields.Count >= 6);
        Assert.Equal("ts", fields[0].Key);
        Assert.Equal("lvl", fields[1].Key);
        Assert.Equal("cat", fields[2].Key);
        Assert.Equal("evt", fields[3].Key);
        Assert.Equal("msg", fields[4].Key);
        Assert.Equal("run", fields[5].Key);
    }

    private static IReadOnlyDictionary<string, string> ParseLine(string line)
    {
        return SplitFields(line)
          .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static List<KeyValuePair<string, string>> SplitFields(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var escape = false;

        foreach (var character in line)
        {
            if (escape)
            {
                current.Append(character);
                escape = false;
                continue;
            }

            if (character == '\\' && inQuotes)
            {
                escape = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (character == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens
          .Select(token =>
          {
              var separatorIndex = token.IndexOf('=');
              return new KeyValuePair<string, string>(
                token[..separatorIndex],
                token[(separatorIndex + 1)..]);
          })
          .ToList();
    }
}
