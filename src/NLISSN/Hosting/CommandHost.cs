using NLISSN.Application;
using NLISSN.Artifacts;
using NLISSN.Composition;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Core.Pipeline;
using NLISSN.Telemetry;
using System.Text;
using NLISSN.Core.Rewrite;

namespace NLISSN.Hosting;

/// 协调命令行解析、分析、可选的制品回放和输出发布。
public sealed class  CommandHost
{
    private readonly  RulePipeline _pipeline;
    // 持有一条默认规则管道，供单文件和目录入口按同一规则集运行。
    public  CommandHost()
      : this(RulePipelineComposer.Compose(new RuleSelection()).Pipeline)
    {
    }

    public  CommandHost( RulePipeline pipeline)
    {
        _pipeline = pipeline;
    }

    internal PrototypeAnalysisResult Analyze(AnalysisConfiguration configuration)
    {
        return AnalyzeAsync(configuration).GetAwaiter().GetResult();
    }

    internal async Task<PrototypeAnalysisResult> AnalyzeAsync(AnalysisConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return await AnalyzeCoreAsync(configuration);
    }

    private async Task<PrototypeAnalysisResult> AnalyzeCoreAsync(AnalysisConfiguration configuration)
    {
        var inputPath = configuration.InputPath;
        var settings = configuration.CreateAnalysisRequestSettings();
        var runtime = AnalysisRuntimeFactory.Create(new RoslynPrototypeExecutionOptions(
          configuration.Execution.MaxDegreeOfParallelism,
          configuration.Execution.DirectoryParallelism,
          configuration.Execution.GroupParallelism,
          configuration.Execution.HelperParallelism,
          CpgMaxDegreeOfParallelism: configuration.Execution.CpgMaxDegreeOfParallelism));
        await using var runtimeLog = RuntimeMeasurementLog.TryCreate(
          configuration.Artifacts.WriteRuntimeLog ? configuration.Artifacts.RuntimeLogPath : null,
          configuration.Logging,
          configuration.Artifacts.RunId,
          inputPath,
          runtime);
        var policy = configuration.RulePolicy;
        var rules = HasRuleSelection(policy)
          ? RulePipelineComposer.Compose(CreateRuleSelection(policy)).Pipeline
          : _pipeline;

        try
        {
            if (inputPath is not null && Directory.Exists(inputPath))
            {
                var replayPlanPath = configuration.Artifacts.ReplayPlanPath;
                if (replayPlanPath is not null)
                {
                    var replayResult = await new RewritePlanReplayService().ReplayAsync(
                      inputPath,
                      replayPlanPath,
                      runtime,
                      configuration.Execution,
                      configuration.Artifacts);
                    await CompleteRuntimeLogAsync(configuration, runtimeLog, replayResult, runtime);
                    return replayResult;
                }

                var directoryResult = await new  DirectoryAnalysisService(rules).AnalyzeDirectoryAsync(
                  inputPath,
                  settings,
                  runtime,
                  configuration.Execution,
                  configuration.Artifacts);
                if (configuration.Artifacts.RewritePlanMode == RewritePlanMode.Capture)
                {
                    CaptureRewritePlan(inputPath, configuration.Artifacts.RewritePlanRoot, directoryResult);
                }

                await CompleteRuntimeLogAsync(configuration, runtimeLog, directoryResult, runtime);
                return directoryResult;
            }

            if (configuration.Workspace is not null)
            {
                var workspaceResult = await new WorkspaceAnalysisService(rules).AnalyzeAsync(
                  configuration.Workspace,
                  settings,
                  runtime,
                  configuration.Execution,
                  configuration.Artifacts);
                if (configuration.Artifacts.RewritePlanMode == RewritePlanMode.Capture)
                {
                    CaptureRewritePlan(
                      configuration.Workspace.SolutionRoot,
                      configuration.Artifacts.RewritePlanRoot,
                      workspaceResult,
                      configuration.Workspace.TargetDocumentPath is null ? null : 1);
                }

                await CompleteRuntimeLogAsync(configuration, runtimeLog, workspaceResult, runtime);
                return workspaceResult;
            }

            var source = inputPath is not null && File.Exists(inputPath)
              ? File.ReadAllText(inputPath)
              : DefaultSourceProvider.GetDefaultSource();
            var filePath = inputPath ?? "demo.cs";
            var application = new  ApplicationService(rules);
            var result = application.Analyze(source, filePath, settings, runtime);
            result =  PostRewriteDiagnostics.AddSingleFileDiagnostics(
              result,
              filePath,
                PostRewriteDiagnostics.ShouldSkipDeclarationDiagnostics(settings));

            if (inputPath is null || !File.Exists(inputPath) || result.Edits.Count == 0)
            {
                await CompleteRuntimeLogAsync(configuration, runtimeLog, result, runtime);
                return result;
            }

            if (configuration.Execution.WriteBack)
            {
                File.WriteAllText(inputPath, result.RewrittenSource ?? source, Encoding.UTF8);
            }

            if (!configuration.Artifacts.WriteDiff)
            {
                await CompleteRuntimeLogAsync(configuration, runtimeLog, result, runtime);
                return result;
            }

            var inputRoot = Path.GetDirectoryName(Path.GetFullPath(inputPath))
              ?? throw new InvalidOperationException("The input file must have a parent directory.");
            var writtenDiffCount = new CategoryDiffArtifactService().Write(
              inputRoot,
              Path.GetFullPath(inputPath),
              source,
              result.Decisions,
              configuration.Artifacts.DiffRoot,
              configuration.Artifacts.DiffView);
            result = result with
            {
                DiffFilePath = writtenDiffCount > 0 ? configuration.Artifacts.DiffRoot : null,
            };
            await CompleteRuntimeLogAsync(configuration, runtimeLog, result, runtime);
            return result;
        }
        catch (Exception exception)
        {
            if (runtimeLog is not null)
            {
                await runtimeLog.FailAsync(exception, runtime);
            }

            throw;
        }
    }

    private static bool HasRuleSelection(RulePolicySettings policy)
    {
        return policy.DisabledRuleTypes.Count > 0 ||
          policy.DeleteUnreachableMethods ||
          policy.DeleteUnreferencedMethods ||
          policy.ClearUnusedInterfaceImplementations ||
          policy.PrivatizeInternalOnlyPublicMethods;
    }

    private static RuleSelection CreateRuleSelection(RulePolicySettings policy)
    {
        return RuleSelectionAdapter.FromLegacySettings(
          policy.DisabledRuleTypes,
          policy.DeleteUnreachableMethods,
          policy.DeleteUnreferencedMethods,
          policy.ClearUnusedInterfaceImplementations,
          policy.PrivatizeInternalOnlyPublicMethods);
    }

    private static async Task CompleteRuntimeLogAsync(
        AnalysisConfiguration configuration,
        RuntimeMeasurementLog? runtimeLog,
        PrototypeAnalysisResult result,
        AnalysisRuntime runtime)
    {
        if (configuration.Artifacts.WriteEvidence && result.Evidence is not null)
        {
            AnalysisEvidenceArtifactService.Write(configuration.Artifacts.EvidencePath, result.Evidence, configuration);
        }
        else if (configuration.Artifacts.WriteEvidence)
        {
            AnalysisEvidenceArtifactService.WriteReplayNotice(configuration.Artifacts.EvidencePath, configuration);
        }

        if (runtimeLog is not null)
        {
            await runtimeLog.CompleteAsync(result, runtime);
        }
    }

    private static void CaptureRewritePlan(
      string inputRoot,
      string artifactRoot,
      PrototypeAnalysisResult result,
      int? sourceFileCount = null)
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
                  .Select(operation => operation with
                  {
                      RuleId = ResolveOperationRuleId(fullPath, operation, result.Decisions),
                  })
                  .OrderByDescending(operation => operation.Start)
                  .ThenByDescending(operation => operation.Length)
                  .ToArray());
          })
          .ToArray();
        new RewritePlanArtifactService().Write(
          artifactRoot,
          inputRoot,
          sourceFileCount ??
            Directory.EnumerateFiles(inputRoot, "*.cs", SearchOption.AllDirectories).Count(),
          plans);
    }

    private static string ResolveOperationRuleId(
      string filePath,
      RewritePlanEdit operation,
      IReadOnlyList<NLISSN.Core.Decision.RuleDecision> decisions)
    {
        var candidates = decisions
          .Where(decision => !string.IsNullOrWhiteSpace(decision.RuleId))
          .Where(decision => string.Equals(
            Path.GetFullPath(decision.FinalNode.SyntaxTree.FilePath),
            filePath,
            StringComparison.OrdinalIgnoreCase))
          .Where(decision =>
            decision.FinalNode.Span.Start <= operation.Start &&
            operation.Start + operation.Length <= decision.FinalNode.Span.End ||
            operation.Start <= decision.FinalNode.Span.Start &&
            decision.FinalNode.Span.End <= operation.Start + operation.Length)
          .OrderBy(decision => decision.FinalNode.Span.Length)
          .ToArray();
        return candidates.FirstOrDefault()?.RuleId
          ?? throw new InvalidOperationException(
            $"Cannot determine proposal rule provenance for rewrite operation {operation.Start}..{operation.Start + operation.Length} in '{filePath}'.");
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
