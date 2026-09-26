using NLISSN.Application;
using NLISSN.Artifacts;
using NLISSN.Composition;
using NLISSN.Infrastructure.Configuration;
using NLISSN.Core.Pipeline;
using NLISSN.Telemetry;
using System.Text;
using NLISSN.Core.Rewrite;
using NLISSN.Core.Performance;
using NLISSN.Performance;
using NLISSN.Application.Performance;

namespace NLISSN.Hosting;

/// 协调命令行解析、分析、可选的制品回放和输出发布。
public sealed class  CommandHost
{
    private readonly  RulePipeline _pipeline;
    // 持有一条默认规则管道，供单文件和目录入口按同一规则集运行。
    public CommandHost()
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
        return (await AnalyzeCoreAsync(configuration)).Result;
    }

    internal async Task<AnalysisRunOutcome> AnalyzeOutcomeAsync(AnalysisConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return await AnalyzeCoreAsync(configuration);
    }

    private async Task<AnalysisRunOutcome> AnalyzeCoreAsync(AnalysisConfiguration configuration)
    {
        var inputPath = configuration.InputPath;
        var settings = configuration.CreateAnalysisRequestSettings();
        var performanceMode = ParsePerformanceMode(configuration.Artifacts.PerformanceMode);
        var runtime = AnalysisRuntimeFactory.Create(new RoslynPrototypeExecutionOptions(
          DirectoryMaxDegreeOfParallelism: configuration.Execution.DirectoryMaxDegreeOfParallelism,
          CpgMaxDegreeOfParallelism: configuration.Execution.CpgMaxDegreeOfParallelism,
          GroupMaxDegreeOfParallelism: configuration.Execution.GroupMaxDegreeOfParallelism,
          HelperMaxDegreeOfParallelism: configuration.Execution.HelperMaxDegreeOfParallelism,
          ReplayMaxDegreeOfParallelism: configuration.Execution.ReplayMaxDegreeOfParallelism,
          MaxConcurrentOperations: configuration.Execution.MaxConcurrentOperations,
          EnableDirectoryParallelism: configuration.Execution.DirectoryParallelism,
          EnableGroupParallelism: configuration.Execution.GroupParallelism,
          EnableHelperParallelism: configuration.Execution.HelperParallelism));
        PerformanceDiagnosticsCollector? diagnosticsCollector = null;
        if (configuration.Artifacts.WritePerformanceSummary)
        {
            runtime.PerformanceStageCollector = new PerformanceStageCollector();
            if (performanceMode is PerformanceMode.Diagnostic or PerformanceMode.Profile)
            {
                diagnosticsCollector = new PerformanceDiagnosticsCollector();
                runtime.PerformanceEventSink = diagnosticsCollector;
                runtime.PartitionPerformanceEventSink = diagnosticsCollector;
            }
            runtime.PerformanceRunId = ResolveRunId(configuration.Artifacts.RunId);
        }
        await using var runtimeLog = RuntimeMeasurementLog.TryCreate(
          configuration.Artifacts.WriteRuntimeLog ? configuration.Artifacts.RuntimeLogPath : null,
          configuration.Logging,
          configuration.Artifacts.RunId,
          inputPath,
          runtime);
        var performanceMeasurement = new PerformanceRunMeasurement();
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
                    var replayOutcome = AnalysisRunOutcome.FromItem(
                      ResolveRunId(configuration.Artifacts.RunId),
                      "replay",
                      replayResult,
                      inputIdentity: inputPath);
                    replayOutcome = await CompleteRuntimeLogAsync(configuration, runtimeLog, replayOutcome, runtime, rules, performanceMeasurement);
                    return replayOutcome;
                }

                var directoryOutcome = await new  DirectoryAnalysisService(rules).AnalyzeDirectoryAsync(
                  inputPath,
                  settings,
                  runtime,
                  configuration.Execution,
                  configuration.Artifacts);
                directoryOutcome = directoryOutcome.WithMode(performanceMode);
                if (configuration.Artifacts.RewritePlanMode == RewritePlanMode.Capture)
                {
                    CaptureRewritePlan(inputPath, configuration.Artifacts.RewritePlanRoot, directoryOutcome.Result);
                }

                directoryOutcome = await CompleteRuntimeLogAsync(configuration, runtimeLog, directoryOutcome, runtime, rules, performanceMeasurement);
                return directoryOutcome;
            }

            if (configuration.Workspace is not null)
            {
                var workspaceOutcome = await new WorkspaceAnalysisService(rules).AnalyzeAsync(
                  configuration.Workspace,
                  settings,
                  runtime,
                  configuration.Execution,
                  configuration.Artifacts);
                workspaceOutcome = workspaceOutcome.WithMode(performanceMode);
                if (configuration.Artifacts.RewritePlanMode == RewritePlanMode.Capture)
                {
                    CaptureRewritePlan(
                      configuration.Workspace.SolutionRoot,
                      configuration.Artifacts.RewritePlanRoot,
                      workspaceOutcome.Result,
                      configuration.Workspace.TargetDocumentPath is null ? null : 1);
                }

                workspaceOutcome = await CompleteRuntimeLogAsync(configuration, runtimeLog, workspaceOutcome, runtime, rules, performanceMeasurement);
                return workspaceOutcome;
            }

            var source = inputPath is not null && File.Exists(inputPath)
              ? File.ReadAllText(inputPath)
              : DefaultSourceProvider.GetDefaultSource();
            var filePath = inputPath ?? "demo.cs";
            var application = new  ApplicationService(rules);
            var result = application.Analyze(source, filePath, settings, runtime);
            result =  PostRewriteDiagnostics.AddSingleFileDiagnostics(
              result,
              source,
              filePath,
              PostRewriteDiagnostics.ShouldSkipDeclarationDiagnostics(settings));

            if (inputPath is null || !File.Exists(inputPath) || result.Edits.Count == 0)
            {
                var outcome = AnalysisRunOutcome.FromItem(
                  ResolveRunId(configuration.Artifacts.RunId),
                  "file",
                  result,
                  mode: performanceMode,
                  inputIdentity: filePath);
                outcome = await CompleteRuntimeLogAsync(configuration, runtimeLog, outcome, runtime, rules, performanceMeasurement);
                return outcome;
            }

            if (configuration.Execution.WriteBack)
            {
                var writeBackScope = StartStage(
                  runtime,
                  PerformanceStageId.ArtifactWriteBack,
                  PerformanceStageId.Run,
                  filePath);
                try
                {
                    File.WriteAllText(inputPath, result.RewrittenSource ?? source, Encoding.UTF8);
                    writeBackScope?.Complete();
                }
                catch (Exception exception)
                {
                    writeBackScope?.Fail(exception);
                    throw;
                }
            }

            if (!configuration.Artifacts.WriteDiff)
            {
                var outcome = AnalysisRunOutcome.FromItem(
                  ResolveRunId(configuration.Artifacts.RunId),
                  "file",
                  result,
                  mode: performanceMode,
                  inputIdentity: filePath);
                outcome = await CompleteRuntimeLogAsync(configuration, runtimeLog, outcome, runtime, rules, performanceMeasurement);
                return outcome;
            }

            var inputRoot = Path.GetDirectoryName(Path.GetFullPath(inputPath))
              ?? throw new InvalidOperationException("The input file must have a parent directory.");
            var diffScope = StartStage(
              runtime,
              PerformanceStageId.ArtifactDiff,
              PerformanceStageId.Run,
              filePath);
            int writtenDiffCount;
            try
            {
                writtenDiffCount = new CategoryDiffArtifactService().Write(
                  inputRoot,
                  Path.GetFullPath(inputPath),
                  source,
                  result.Decisions,
                  configuration.Artifacts.DiffRoot,
                  configuration.Artifacts.DiffView);
                diffScope?.Complete();
            }
            catch (Exception exception)
            {
                diffScope?.Fail(exception);
                throw;
            }
            result = result with
            {
                DiffFilePath = writtenDiffCount > 0 ? configuration.Artifacts.DiffRoot : null,
            };
            var finalOutcome = AnalysisRunOutcome.FromItem(
              ResolveRunId(configuration.Artifacts.RunId),
              "file",
              result,
              mode: performanceMode,
              inputIdentity: filePath);
            finalOutcome = await CompleteRuntimeLogAsync(configuration, runtimeLog, finalOutcome, runtime, rules, performanceMeasurement);
            return finalOutcome;
        }
        catch (Exception exception)
        {
            if (runtimeLog is not null)
            {
                await runtimeLog.FailAsync(exception, runtime);
            }

            if (configuration.Artifacts.WritePerformanceSummary)
            {
                try
                {
                    PublishFailedPerformanceSummary(
                      configuration,
                      runtime,
                      performanceMeasurement,
                      performanceMode,
                      exception);
                }
                catch
                {
                    // Performance failure reporting is fail-open with respect to the business exception.
                }
            }

            throw;
        }
        finally
        {
            // G0-L：本 run 自建的内核必须在此收尾。成功、首次异常与外部取消都经过这里；
            // DisposeAsync 的语义是排空已接受的工作再结束长期 worker，不是取消，
            // 所以异常路径上调用它不会改变原有异常语义（原异常照常传播）。
            // 注入内核与派生 runtime 的 OwnsScheduler 为 false，本调用对其是空操作。
            await runtime.DisposeSchedulerAsync();
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

    private static string ResolveRunId(string runId)
    {
        return string.IsNullOrWhiteSpace(runId) ? "unassigned" : runId;
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

    private static PerformanceMode ParsePerformanceMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "normal" => PerformanceMode.Normal,
            "diagnostic" => PerformanceMode.Diagnostic,
            "profile" => PerformanceMode.Profile,
            "benchmark" => PerformanceMode.Benchmark,
            _ => PerformanceMode.Normal
        };
    }

    private static async Task<AnalysisRunOutcome> CompleteRuntimeLogAsync(
        AnalysisConfiguration configuration,
        RuntimeMeasurementLog? runtimeLog,
        AnalysisRunOutcome outcome,
        AnalysisRuntime runtime,
        RulePipeline rules,
        PerformanceRunMeasurement performanceMeasurement)
    {
        var result = outcome.Result;
        var diagnosticsCollector = runtime.PerformanceEventSink as PerformanceDiagnosticsCollector;
        if (configuration.Artifacts.WriteEvidence && result.Evidence is not null)
        {
            var evidenceScope = StartStage(
              runtime,
              PerformanceStageId.ArtifactEvidence,
              PerformanceStageId.Run,
              outcome.Performance.InputIdentity);
            try
            {
                AnalysisEvidenceArtifactService.Write(configuration.Artifacts.EvidencePath, result.Evidence, configuration);
                evidenceScope?.Complete();
            }
            catch (Exception exception)
            {
                evidenceScope?.Fail(exception);
                throw;
            }
        }
        else if (configuration.Artifacts.WriteEvidence)
        {
            var evidenceScope = StartStage(
              runtime,
              PerformanceStageId.ArtifactEvidence,
              PerformanceStageId.Run,
              outcome.Performance.InputIdentity);
            try
            {
                AnalysisEvidenceArtifactService.WriteReplayNotice(configuration.Artifacts.EvidencePath, configuration);
                evidenceScope?.Complete();
            }
            catch (Exception exception)
            {
                evidenceScope?.Fail(exception);
                throw;
            }
        }

        if (runtimeLog is not null)
        {
            await runtimeLog.CompleteAsync(result, runtime);
        }

        var rootStage = performanceMeasurement.CompleteRoot(
          PerformanceStageId.Run,
          outcome.Performance.InputIdentity,
          runtime);
        var stages = PerformanceStageId.Required.Select(stageId =>
            new PerformanceStageSample(
              stageId,
              PerformanceStageId.Run,
              outcome.Performance.InputIdentity,
              null,
              null,
              PerformanceStatus.Unavailable,
              "stage-not-instrumented"))
          .Concat(outcome.Performance.Stages)
          .Concat(outcome.Performance.RootStage is null
            ? Array.Empty<PerformanceStageSample>()
            : new[] { outcome.Performance.RootStage })
          .Concat(outcome.Performance.Directory?.Stage is { } directoryStage
            ? new[] { directoryStage }
            : Array.Empty<PerformanceStageSample>())
          .Concat(runtime.PerformanceStageCollector?.Snapshot()
            ?? Array.Empty<PerformanceStageSample>())
          .Append(rootStage)
          .GroupBy(stage => $"{stage.StageId}\u0000{stage.ItemId}", StringComparer.Ordinal)
          .Select(group => group.Last())
          .OrderBy(stage => stage.StageId, StringComparer.Ordinal)
          .ThenBy(stage => stage.ItemId, StringComparer.Ordinal)
          .ToArray();
        outcome = outcome.WithRuntimeFacts(
          rootStage,
          stages,
          performanceMeasurement.CompleteResources(runtime));
        outcome = outcome.WithIdentity(
          PerformanceRunIdentityFactory.Create(
            configuration,
            rules,
            outcome,
            runtime,
            outcome.Performance.Mode));

        if (diagnosticsCollector is not null && configuration.Artifacts.WritePerformanceSummary)
        {
            var runRoot = PerformanceDiagnosticAttachment.ResolveRunArtifactRoot(
              configuration.Artifacts.PerformanceSummaryPath);
            var diagnosticPath = Path.Combine(runRoot, "Performance", "diagnostic-events.json");
            var diagnosticStatus = diagnosticsCollector.TryWriteJson(diagnosticPath, out var diagnosticError);
            var attachment = diagnosticStatus
              ? PerformanceDiagnosticAttachment.Create(
                "diagnostic-events",
                outcome.Performance.RunId,
                PerformanceStageId.Run,
                outcome.Performance.Mode,
                runRoot,
                diagnosticPath)
              : PerformanceDiagnosticAttachment.CreateUnavailable(
                "diagnostic-events",
                outcome.Performance.RunId,
                PerformanceStageId.Run,
                outcome.Performance.Mode,
                "Performance/diagnostic-events.json",
                diagnosticError ?? "diagnostic-artifact-write-failed");
            outcome = outcome with
            {
                Performance = outcome.Performance.WithAttachments(
                  outcome.Performance.Attachments.Append(attachment).ToArray())
            };
        }

        if (configuration.Artifacts.WritePerformanceSummary)
        {
            var publication = new PerformanceSummaryPublisher().Publish(
              configuration.Artifacts.PerformanceSummaryPath,
              outcome.Performance);
            if (publication.Status == PerformancePublicationStatus.Failed && publication.ErrorKind is not null)
            {
                outcome = outcome with
                {
                    Performance = outcome.Performance.WithPublicationFailure(publication.ErrorKind)
                };
            }
        }

        return outcome;
    }

    private static PerformanceStageScope? StartStage(
        AnalysisRuntime runtime,
        string stageId,
        string parentStageId,
        string? itemId)
    {
        return runtime.PerformanceStageCollector is PerformanceStageCollector collector
          ? collector.Start(stageId, parentStageId, itemId)
          : null;
    }

    private static void PublishFailedPerformanceSummary(
        AnalysisConfiguration configuration,
        AnalysisRuntime runtime,
        PerformanceRunMeasurement performanceMeasurement,
        PerformanceMode mode,
        Exception exception)
    {
        var errorKind = exception.GetType().FullName ?? exception.GetType().Name;
        var rootStage = performanceMeasurement.CompleteRoot(
          PerformanceStageId.Run,
          configuration.InputPath,
          runtime,
          PerformanceStatus.Failed,
          errorKind);
        var stages = PerformanceStageId.Required
          .Select(stageId => new PerformanceStageSample(
            stageId,
            PerformanceStageId.Run,
            configuration.InputPath,
            null,
            null,
            PerformanceStatus.Unavailable,
            "stage-not-instrumented"))
          .Concat(runtime.PerformanceStageCollector?.Snapshot()
            ?? Array.Empty<PerformanceStageSample>())
          .Append(rootStage)
          .GroupBy(stage => $"{stage.StageId}\u0000{stage.ItemId}", StringComparer.Ordinal)
          .Select(group => group.Last())
          .OrderBy(stage => stage.StageId, StringComparer.Ordinal)
          .ThenBy(stage => stage.ItemId, StringComparer.Ordinal)
          .ToArray();
        var report = new RunPerformanceReport(
          ResolveRunId(configuration.Artifacts.RunId),
          configuration.InputPath is not null && Directory.Exists(configuration.InputPath)
            ? "directory"
            : "file",
          configuration.InputPath,
          Array.Empty<ApplicationPerformanceFacts>(),
          rootStage,
          new PerformanceTerminalSummary(
            rootStage.WallElapsedMs,
            rootStage.AccumulatedElapsedMs,
            PerformanceStatus.Failed,
            false,
            errorKind),
          PerformanceStatus.Failed,
          mode,
          stages: stages,
          resources: performanceMeasurement.CompleteResources(runtime));
        var diagnosticsCollector = runtime.PerformanceEventSink as PerformanceDiagnosticsCollector;
        if (diagnosticsCollector is not null)
        {
            var runRoot = PerformanceDiagnosticAttachment.ResolveRunArtifactRoot(
              configuration.Artifacts.PerformanceSummaryPath);
            var diagnosticPath = Path.Combine(runRoot, "Performance", "diagnostic-events.json");
            var diagnosticStatus = diagnosticsCollector.TryWriteJson(diagnosticPath, out var diagnosticError);
            var attachment = diagnosticStatus
              ? PerformanceDiagnosticAttachment.Create(
                "diagnostic-events",
                report.RunId,
                PerformanceStageId.Run,
                mode,
                runRoot,
                diagnosticPath)
              : PerformanceDiagnosticAttachment.CreateUnavailable(
                "diagnostic-events",
                report.RunId,
                PerformanceStageId.Run,
                mode,
                "Performance/diagnostic-events.json",
                diagnosticError ?? "diagnostic-artifact-write-failed");
            report = report.WithAttachments(new[] { attachment });
        }

        _ = new PerformanceSummaryPublisher().Publish(
          configuration.Artifacts.PerformanceSummaryPath,
          report);
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
