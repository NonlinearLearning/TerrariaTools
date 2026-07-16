using System.Text;
using RoslynPrototype.Rewrite;
using Rules;

namespace RoslynPrototype.Application;

public sealed class DeletionCommandHost
{
    private readonly DeletionRulePipeline _pipeline;

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
        var rules = DeletionApplicationOptions.TryParseDisabledRuleTypes(
          options,
          out var disabledRuleTypes)
            ? RuleRegistry.CreateDefaultRules(disabledRuleTypes)
            : _pipeline;
        using var runtimeMetricsLog = RuntimeMetricsLog.Create(options, runtime);

        try
        {
            if (inputPath is not null && Directory.Exists(inputPath))
            {
                var directoryResult = await new DeletionDirectoryAnalysisService(rules).AnalyzeDirectoryAsync(
                  inputPath,
                  options,
                  runtime);
                runtimeMetricsLog?.Complete(directoryResult);
                return directoryResult;
            }

            var source = inputPath is not null && File.Exists(inputPath)
              ? File.ReadAllText(inputPath)
              : DefaultSourceProvider.GetDefaultSource();
            var filePath = inputPath ?? "demo.cs";
            var application = new DeletionApplicationService(rules);
            var result = application.Analyze(source, filePath, options, runtime);
            result = DeletionPostRewriteDiagnostics.AddSingleFileDiagnostics(result, filePath, options);

            if (inputPath is null || !File.Exists(inputPath) || result.Edits.Count == 0)
            {
                runtimeMetricsLog?.Complete(result);
                return result;
            }

            if (DeletionApplicationOptions.ShouldWriteBack(options))
            {
                File.WriteAllText(inputPath, result.RewrittenSource ?? source, Encoding.UTF8);
            }

            if (!DeletionApplicationOptions.ShouldWriteDiff(options))
            {
                runtimeMetricsLog?.Complete(result);
                return result;
            }

            var diffPath = DeletionDiffPathResolver.ResolveDiffPath(inputPath, options);
            File.WriteAllText(diffPath, result.DiffText, Encoding.UTF8);
            var finalizedResult = result with { DiffFilePath = diffPath };
            runtimeMetricsLog?.Complete(finalizedResult);
            return finalizedResult;
        }
        catch
        {
            runtimeMetricsLog?.Fail();
            throw;
        }
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
