using NLISSN.Application;
using System.Text;
using Microsoft.CodeAnalysis.Text;
using NLISSN.Core.Rewrite;
using NLISSN.Rules;

namespace NLISSN;

/// 回放已验证的重写计划制品，不重新构建 CPG 或评估规则。
internal sealed class RewritePlanReplayService
{
    private readonly RewritePlanArtifactService _artifactService = new();
    private readonly DiffBuilder _diffBuilder = new();
    private readonly TextDiffRenderer _diffRenderer = new();

    internal async Task<PrototypeAnalysisResult> ReplayAsync(string inputRoot, string artifactRoot, IReadOnlyDictionary<string, string> options, AnalysisRuntime runtime)
    {
        var (_, plans) = _artifactService.ReadAndValidate(artifactRoot, inputRoot);
        var results = await runtime.ConcurrencyPool.SelectOrderedAsync(
          plans.Count,
          runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
          (index, cancellationToken) => Task.FromResult(Execute(inputRoot, plans[index], cancellationToken)),
          runtime.ExecutionOptions.CancellationToken);
        var edits = new List<RewriteEdit>();
        var documents = new List<DiffDocument>();
        var rewrittenSources = new List<(string Path, string Source)>();
        var diffRoot = DiffPathResolver.ResolveDirectoryDiffRoot(inputRoot, options);
        foreach (var result in results)
        {
            edits.AddRange(result.Edits);
            documents.Add(result.Diff);
            rewrittenSources.Add((result.FilePath, result.RewrittenSource));
        }

        if (ApplicationOptions.ShouldWriteDiff(options))
        {
            foreach (var result in results)
            {
                var diffPath = DiffPathResolver.ResolveFileDiffPath(inputRoot, result.FilePath, diffRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
                File.WriteAllText(diffPath, _diffRenderer.Render(result.Diff.Files.Single(), ApplicationOptions.ResolveDiffView(options)), new UTF8Encoding(false));
            }
        }

        if (ApplicationOptions.ShouldWriteBack(options))
        {
            foreach (var (path, source) in rewrittenSources)
            {
                File.WriteAllText(path, source, new UTF8Encoding(false));
            }
        }

        var diff = _diffBuilder.Combine(documents);
        return new PrototypeAnalysisResult(
          Array.Empty<NLISSN.Core.Marking.MarkRecord>(),
          Array.Empty<NLISSN.Core.Propagation.PropagatedMarkRecord>(),
          Array.Empty<NLISSN.Core.Lifting.LiftedMarkRecord>(),
          Array.Empty<NLISSN.Core.Decision.RuleDecision>(),
          edits,
          $"<replay:{plans.Count}>",
          diff,
           ApplicationOptions.ShouldWriteDiff(options) && plans.Count > 0 ? diffRoot : null,
          new AnalysisStats(plans.Count, plans.Count, 0, 0));
    }

    private static ReplayFileResult Execute(string inputRoot, RewritePlanFile plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(inputRoot, plan.RelativePath);
        var source = File.ReadAllText(path);
        var rewriteResult = new PrototypeRewriter().ExecutePlan(source, path, plan);
        return new ReplayFileResult(
          path,
          rewriteResult.RewrittenSource ?? source,
          rewriteResult.Edits,
          rewriteResult.Diff);
    }

    private sealed record ReplayFileResult(
      string FilePath,
      string RewrittenSource,
      IReadOnlyList<RewriteEdit> Edits,
      DiffDocument Diff);
}
