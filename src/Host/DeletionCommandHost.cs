using System.Text;
using MinimalRoslynCpg.Builder;
using RoslynPrototype.Application.Logging;
using RoslynPrototype.Analysis;
using RoslynPrototype.Rewrite;
using Rules;

namespace RoslynPrototype.Application;

public sealed class DeletionCommandHost
{
    private readonly DeletionRulePipeline _pipeline;
    private readonly TextDiffRenderer _textDiffRenderer = new();

    public DeletionCommandHost(DeletionRulePipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public PrototypeAnalysisResult AnalyzeFromArgs(string[] args)
    {
        return AnalyzeFromArgsAsync(args).GetAwaiter().GetResult();
    }

    public async Task<PrototypeAnalysisResult> AnalyzeFromArgsAsync(string[] args)
    {
        var inputPath = args.FirstOrDefault(path => !path.StartsWith("--", StringComparison.Ordinal));
        var options = DeletionApplicationOptions.Parse(args);
        DeletionApplicationOptions.ValidateRewritePlanOptions(options);
        var runtime = DeletionApplicationOptions.CreateRuntime(options);
        var diffView = DeletionApplicationOptions.ResolveDiffView(options);
        EmitLegacyTextLogWarnings(options);
        var rules = DeletionApplicationOptions.TryParseDisabledRuleTypes(
          options,
          out var disabledRuleTypes)
          ? RuleRegistry.CreateDefaultRules(disabledRuleTypes)
          : _pipeline;
        var runtimeLogPath = DeletionApplicationOptions.ResolveRuntimeLogPath(options);
        var analysisLogPath = DeletionApplicationOptions.ResolveAnalysisLogPath(options);
        using var runtimeSink = runtimeLogPath is null
          ? null
          : TextLogFileSink.Create(runtimeLogPath, new TextLogFormatter(), TextLogFilter.CreateRuntimeFilter(options));
        using var analysisSink = analysisLogPath is null
          ? null
          : TextLogFileSink.Create(analysisLogPath, new TextLogFormatter(), TextLogFilter.CreateAnalysisFilter(options));
        var runContext = CreateRunLogContext(inputPath, runtime);
        var runtimeWriter = runtimeSink is null
          ? null
          : new RunTextLogWriter(runtimeSink, TextLogFilter.CreateRuntimeFilter(options), runContext, "host.runtime");
        var analysisWriter = analysisSink is null
          ? null
          : new AnalysisTextLogWriter(analysisSink, TextLogFilter.CreateAnalysisFilter(options), runContext, "host.analysis");

        runtimeWriter?.Start();
        runtimeWriter?.Sample();

        try
        {
            PrototypeAnalysisResult result;
            if (inputPath is not null && Directory.Exists(inputPath))
            {
                var replayPlanPath = DeletionApplicationOptions.ResolveRewritePlanInPath(options);
                if (replayPlanPath is not null)
                {
                    result = await new RewritePlanReplayService().ReplayAsync(
                      inputPath,
                      replayPlanPath,
                      options,
                      runtime);
                }
                else
                {
                    result = await new DeletionDirectoryAnalysisService(rules).AnalyzeDirectoryAsync(
                      inputPath,
                      options,
                      runtime,
                      analysisWriter);
                    var capturePlanPath = DeletionApplicationOptions.ResolveRewritePlanOutPath(options);
                    if (capturePlanPath is not null)
                    {
                        CaptureRewritePlan(inputPath, capturePlanPath, result);
                    }
                }
            }
            else
            {
                var source = inputPath is not null && File.Exists(inputPath)
                  ? File.ReadAllText(inputPath)
                  : DefaultSourceProvider.GetDefaultSource();
                var filePath = inputPath ?? "demo.cs";
                var application = new DeletionApplicationService(rules);
                result = application.Analyze(source, filePath, options, runtime);
                result = DeletionPostRewriteDiagnostics.AddSingleFileDiagnostics(result, filePath, options);
                analysisWriter?.WriteResult(filePath, result);

                if (inputPath is not null && File.Exists(inputPath) && result.Edits.Count > 0)
                {
                    if (DeletionApplicationOptions.ShouldWriteBack(options))
                    {
                        File.WriteAllText(inputPath, result.RewrittenSource ?? source, Encoding.UTF8);
                    }

                    if (DeletionApplicationOptions.ShouldWriteDiff(options))
                    {
                        var diffPath = DeletionDiffPathResolver.ResolveDiffPath(inputPath, options);
                        var renderedDiff = _textDiffRenderer.Render(result.Diff, diffView);
                        File.WriteAllText(diffPath, renderedDiff, Encoding.UTF8);
                        result = result with { DiffFilePath = diffPath };
                    }
                }
            }

            runtimeWriter?.WriteDiagnostics(result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>());
            runtimeWriter?.WriteCpgSummary(result.CpgBuildTelemetry ?? RoslynCpgBuildTelemetry.CreateDefault());
            runtimeWriter?.WriteMarkSummary(result.MarkAnalysisTelemetry ?? CreateEmptyMarkTelemetry());
            if (runtimeSink is not null)
            {
                runtimeWriter?.WriteIoSummary(runtimeSink);
            }
            if (analysisSink is not null)
            {
                analysisWriter?.WriteIoSummary(analysisSink);
            }
            var inputKind = inputPath is not null && Directory.Exists(inputPath) ? "directory" : inputPath is not null ? "single-file" : "demo";
            runtimeWriter?.Complete(result, "completed", inputKind);
            if (inputKind == "directory" && result.Stats is not null && runtimeSink is not null)
            {
                runtimeWriter?.WriteDirectoryPerformanceSummary(result.Stats, runtimeSink);
            }
            runtimeWriter?.Sample();
            return result;
        }
        catch (Exception exception)
        {
            if (runtimeSink is not null)
            {
                try
                {
                    runtimeWriter?.WriteIoFailure(exception, runtimeSink);
                }
                catch
                {
                }
            }

            if (analysisSink is not null)
            {
                try
                {
                    analysisWriter?.WriteIoFailure(exception, analysisSink);
                }
                catch
                {
                }
            }

            runtimeWriter?.Fail(exception, inputPath is not null && Directory.Exists(inputPath) ? "directory" : inputPath is not null ? "single-file" : "demo");
            throw;
        }
        finally
        {
            runtimeWriter?.Flush();
            analysisSink?.Flush();
            runtimeSink?.Flush();
        }
    }

    private static void CaptureRewritePlan(
      string inputRoot,
      string artifactRoot,
      PrototypeAnalysisResult result)
    {
        var plans = (result.RewritePlans ?? Array.Empty<PrototypeFileRewritePlan>())
          .Where(plan => plan.Operations.Count > 0)
          .Select(plan =>
          {
              var fullPath = Path.GetFullPath(plan.FilePath);
              var relativePath = Path.GetRelativePath(inputRoot, fullPath);
              var sourceBytes = File.ReadAllBytes(fullPath);
              return new RewritePlanFile(
                relativePath,
                RewritePlanArtifactService.ComputeSha256(sourceBytes),
                plan.Operations
                  .OrderByDescending(operation => operation.Start)
                  .ThenByDescending(operation => operation.Length)
                  .ToArray());
          })
          .ToArray();
        new RewritePlanArtifactService().Write(
          artifactRoot,
          inputRoot,
          Directory.EnumerateFiles(inputRoot, "*.cs", SearchOption.AllDirectories).Count(),
          plans);
    }

    private static void EmitLegacyTextLogWarnings(IReadOnlyDictionary<string, string> options)
    {
        EmitLegacyWarning(options, "runtime-metrics-log", "--runtime-metrics-log is deprecated and use --runtime-log instead.");
        EmitLegacyWarning(options, "per-file-timing-log", "--per-file-timing-log is deprecated and use --analysis-log instead.");
        EmitLegacyWarning(options, "per-file-phase-timing-log-directory", "--per-file-phase-timing-log-directory is deprecated and use --analysis-log instead.");
        EmitLegacyWarning(options, "per-file-memory-diagnostics-log", "--per-file-memory-diagnostics-log is deprecated and use --analysis-log instead.");
    }

    private static void EmitLegacyWarning(
      IReadOnlyDictionary<string, string> options,
      string key,
      string message)
    {
        if (options.ContainsKey(key))
        {
            Console.Error.WriteLine(message);
        }
    }

    private static RunLogContext CreateRunLogContext(
      string? inputPath,
      DeletionAnalysisRuntime runtime)
    {
        var inputKind = inputPath is null
          ? "demo"
          : Directory.Exists(inputPath)
            ? "directory"
            : "single-file";
        return new RunLogContext(
          Guid.NewGuid().ToString("N")[..12],
          "delete-class",
          inputKind,
          inputPath,
          runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism);
    }

    private static MarkAnalysisTelemetry CreateEmptyMarkTelemetry()
    {
        return new MarkAnalysisTelemetry(
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          0,
          Array.Empty<MarkRuleTelemetry>(),
          new Dictionary<string, long>(StringComparer.Ordinal));
    }

}

internal static class DefaultSourceProvider
{
    internal static string GetDefaultSource()
    {
        return """
      namespace Demo;

      public sealed class Sample
      {
        public int Compute(Box s, int offset)
        {
          var value = s.Seed + offset;
          if (s.IsReady)
          {
            return value;
          }

          return offset;
        }
      }

      public sealed class Box
      {
        public int Seed { get; set; }

        public bool IsReady { get; set; }
      }
      """;
    }
}
