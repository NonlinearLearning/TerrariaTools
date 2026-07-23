using System.Text;
using Microsoft.CodeAnalysis.Text;
using RoslynPrototype.Rewrite;
using Rules;

namespace RoslynPrototype.Application;

internal sealed class RewritePlanReplayService
{
  private readonly RewritePlanArtifactService _artifactService = new();
  private readonly DiffBuilder _diffBuilder = new();
  private readonly TextDiffRenderer _diffRenderer = new();

  internal async Task<PrototypeAnalysisResult> ReplayAsync(
    string inputRoot,
    string artifactRoot,
    IReadOnlyDictionary<string, string> options,
    DeletionAnalysisRuntime runtime)
  {
    var (_, plans) = _artifactService.ReadAndValidate(artifactRoot, inputRoot);
    var results = await runtime.Scheduler.RunOrderedAsync(
      plans.Count,
      runtime.ExecutionOptions.EffectiveMaxDegreeOfParallelism,
      (index, cancellationToken) => Task.FromResult(Execute(inputRoot, plans[index], cancellationToken)),
      runtime.ExecutionOptions.CancellationToken);
    var edits = new List<RewriteEdit>();
    var documents = new List<DiffDocument>();
    var rewrittenSources = new List<(string Path, string Source)>();
    var diffRoot = DeletionDiffPathResolver.ResolveDirectoryDiffRoot(inputRoot, options);
    foreach (var result in results)
    {
      edits.AddRange(result.Edits);
      documents.Add(result.Diff);
      rewrittenSources.Add((result.FilePath, result.RewrittenSource));
    }

    if (DeletionApplicationOptions.ShouldWriteDiff(options))
    {
      foreach (var result in results)
      {
        var diffPath = DeletionDiffPathResolver.ResolveFileDiffPath(inputRoot, result.FilePath, diffRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
        File.WriteAllText(diffPath, _diffRenderer.Render(result.Diff.Files.Single(), DeletionApplicationOptions.ResolveDiffView(options)), new UTF8Encoding(false));
      }
    }

    if (DeletionApplicationOptions.ShouldWriteBack(options))
    {
      foreach (var (path, source) in rewrittenSources)
      {
        File.WriteAllText(path, source, new UTF8Encoding(false));
      }
    }

    var diff = _diffBuilder.Combine(documents);
    return new PrototypeAnalysisResult(
      Array.Empty<RoslynPrototype.Marking.MarkRecord>(),
      Array.Empty<RoslynPrototype.Propagation.PropagatedMarkRecord>(),
      Array.Empty<RoslynPrototype.Lifting.LiftedMarkRecord>(),
      Array.Empty<RoslynPrototype.Decision.RuleDecision>(),
      edits,
      $"<replay:{plans.Count}>",
      diff,
      DeletionApplicationOptions.ShouldWriteDiff(options) && plans.Count > 0 ? diffRoot : null,
      new AnalysisStats(plans.Count, plans.Count, 0, 0, 0));
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
