using NLISSN.Application;
using System.Text;
using NLISSN.Core.Rewrite;
using NLISSN.Rules;

namespace NLISSN;

/// 协调命令行解析、分析、可选的制品回放和输出发布。
public sealed class  CommandHost
{
    private readonly  RulePipeline _pipeline;
    private readonly TextDiffRenderer _textDiffRenderer = new();

    // 持有一条默认规则管道，供单文件和目录入口按同一规则集运行。
    public  CommandHost( RulePipeline pipeline)
    {
        _pipeline = pipeline;
    }

    // 提供同步 CLI 入口，内部复用异步实现并阻塞等待结果。
    public PrototypeAnalysisResult AnalyzeFromArgs(string[] args)
    {
        return AnalyzeFromArgsAsync(args).GetAwaiter().GetResult();
    }

    // 解析命令行参数后选择目录分析、计划回放或单文件分析，并处理写回与 diff 输出。
    public async Task<PrototypeAnalysisResult> AnalyzeFromArgsAsync(string[] args)
    {
        var inputPath = args.FirstOrDefault(path => !path.StartsWith("--", StringComparison.Ordinal));
        var options =  ApplicationOptions.Parse(args);
         ApplicationOptions.ValidateRewritePlanOptions(options);
        var runtime =  ApplicationOptions.CreateRuntime(options);
        var diffView =  ApplicationOptions.ResolveDiffView(options);
        var rules =  ApplicationOptions.TryParseDisabledRuleTypes(
          options,
          out var disabledRuleTypes)
          ? RuleRegistry.CreateDefaultRules(disabledRuleTypes)
          : _pipeline;

        if (inputPath is not null && Directory.Exists(inputPath))
        {
            var replayPlanPath =  ApplicationOptions.ResolveRewritePlanInPath(options);
            if (replayPlanPath is not null)
            {
                return await new RewritePlanReplayService().ReplayAsync(
                  inputPath,
                  replayPlanPath,
                  options,
                  runtime);
            }

            var directoryResult = await new  DirectoryAnalysisService(rules).AnalyzeDirectoryAsync(
              inputPath,
              options,
              runtime);
            var capturePlanPath =  ApplicationOptions.ResolveRewritePlanOutPath(options);
            if (capturePlanPath is not null)
            {
                CaptureRewritePlan(inputPath, capturePlanPath, directoryResult);
            }

            return directoryResult;
        }

        var source = inputPath is not null && File.Exists(inputPath)
          ? File.ReadAllText(inputPath)
          : DefaultSourceProvider.GetDefaultSource();
        var filePath = inputPath ?? "demo.cs";
        var application = new  ApplicationService(rules);
        var result = application.Analyze(source, filePath, options, runtime);
        result =  PostRewriteDiagnostics.AddSingleFileDiagnostics(
          result,
          filePath,
           ApplicationOptions.ShouldSkipDeleteClassDirectoryPostRewriteDiagnostics(options));

        if (inputPath is null || !File.Exists(inputPath) || result.Edits.Count == 0)
        {
            return result;
        }

        if ( ApplicationOptions.ShouldWriteBack(options))
        {
            File.WriteAllText(inputPath, result.RewrittenSource ?? source, Encoding.UTF8);
        }

        if (! ApplicationOptions.ShouldWriteDiff(options))
        {
            return result;
        }

        var diffPath =  DiffPathResolver.ResolveDiffPath(inputPath, options);
        var renderedDiff = _textDiffRenderer.Render(result.Diff, diffView);
        File.WriteAllText(diffPath, renderedDiff, Encoding.UTF8);
        return result with { DiffFilePath = diffPath };
    }

    private static void CaptureRewritePlan(string inputRoot, string artifactRoot, PrototypeAnalysisResult result)
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
