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
                result = await new DeletionDirectoryAnalysisService(rules).AnalyzeDirectoryAsync(
                  inputPath,
                  options,
                  runtime,
                  analysisWriter);
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
            runtimeWriter?.Complete(result, "completed", inputPath is not null && Directory.Exists(inputPath) ? "directory" : inputPath is not null ? "single-file" : "demo");
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
          Array.Empty<MarkRuleTelemetry>());
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
