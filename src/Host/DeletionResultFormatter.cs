using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Rewrite;

namespace RoslynPrototype.Application;

public sealed class DeletionResultFormatter
{
    public IReadOnlyList<string> FormatResult(PrototypeAnalysisResult result)
    {
        var effectiveMarks = result.SeedMarks.Select(mark => mark.SyntaxNode)
          .Concat(result.PropagatedMarks.Select(mark => mark.Mark.SyntaxNode))
          .Concat(result.LiftedMarks.Select(mark => mark.Mark.SyntaxNode))
          .ToList();
        var lines = new List<string>
    {
      $"SeedMarks: {result.SeedMarks.Count}",
      $"PropagatedMarks: {result.PropagatedMarks.Count}",
      $"LiftedMarks: {result.LiftedMarks.Count}",
      $"EffectiveMarks: {effectiveMarks.Count}"
    };

        foreach (var mark in result.SeedMarks)
        {
            lines.Add($"SEED [{GetNodeKindText(mark.SyntaxNode)}] {mark.SyntaxNode.Span}: {mark.Reason}");
        }

        foreach (var mark in result.PropagatedMarks)
        {
            lines.Add(
              $"PROPAGATED [{GetNodeKindText(mark.Mark.SyntaxNode)}] {mark.Mark.SyntaxNode.Span} from {mark.SourceMark.SyntaxNode.Span} depth={mark.Depth}");
        }

        foreach (var mark in result.LiftedMarks)
        {
            lines.Add(
              $"LIFTED [{GetNodeKindText(mark.Mark.SyntaxNode)}] {mark.Mark.SyntaxNode.Span} from {mark.SourceMark.SyntaxNode.Span} depth={mark.Depth}");
        }

        lines.Add($"Decisions: {result.Decisions.Count}");
        foreach (var decision in result.Decisions)
        {
            lines.Add(
              $"DECISION {decision.Action} [{GetNodeKindText(decision.FinalNode)}] {decision.FinalNode.Span}: {decision.Reason}");
        }

        lines.Add($"Edits: {result.Edits.Count}");
        if (!string.IsNullOrEmpty(result.DiffFilePath))
        {
            lines.Add($"DiffFile: {result.DiffFilePath}");
        }

        if (result.Stats is not null)
        {
            lines.Add($"ScannedFiles: {result.Stats.ScannedFileCount}");
            if (result.Stats.AnalyzedFileCount is not null)
            {
                lines.Add($"AnalyzedFiles: {result.Stats.AnalyzedFileCount}");
            }

            lines.Add($"CandidateMethods: {result.Stats.CandidateMethodCount}");
            lines.Add($"DeletedMethods: {result.Stats.DeletedMethodCount}");
            lines.Add($"ElapsedMs: {result.Stats.ElapsedMilliseconds}");
        }

        if (result.Timings is not null)
        {
            lines.Add($"PreparationMs: {result.Timings.PreparationMilliseconds}");
            lines.Add($"CpgBuildMs: {result.Timings.CpgBuildMilliseconds}");
            lines.Add($"MarkMs: {result.Timings.MarkMilliseconds}");
            lines.Add($"PropagateMs: {result.Timings.PropagateMilliseconds}");
            lines.Add($"LiftMs: {result.Timings.LiftMilliseconds}");
            lines.Add($"DecideMs: {result.Timings.DecideMilliseconds}");
            lines.Add($"RewriteMs: {result.Timings.RewriteMilliseconds}");
            lines.Add($"AnalysisTotalMs: {result.Timings.TotalMilliseconds}");
        }

        if (result.CpgBuildTelemetry is not null)
        {
            var cpg = result.CpgBuildTelemetry;
            var freeze = cpg.FreezeTelemetry;
            var syntax = cpg.SyntaxPassTelemetry;
            var dataFlow = cpg.DataFlowPassTelemetry;
            lines.Add($"GraphNodes: {cpg.GraphNodeCount}");
            lines.Add($"GraphEdges: {cpg.GraphEdgeCount}");
            lines.Add($"CpgPartitions: {cpg.PartitionCount}");
            lines.Add($"OperationChildBufferRents: {cpg.OperationChildBufferRentCount}");
            lines.Add($"CpgMaxDop: {cpg.MaxDegreeOfParallelism}");
            lines.Add($"OperationMs: {cpg.OperationBuildElapsedMilliseconds}");
            lines.Add($"SyntaxNodes: {syntax.SyntaxNodeCount}");
            lines.Add($"SyntaxTokens: {syntax.SyntaxTokenCount}");
            lines.Add($"SyntaxMs: {cpg.SyntaxBuildElapsedMilliseconds}");
            lines.Add($"DataFlowMs: {cpg.DataFlowBuildElapsedMilliseconds}");
            lines.Add($"FreezeMs: {cpg.FreezeQueryIndexElapsedMilliseconds}");
            lines.Add($"FreezeAssignNodeIdsMs: {freeze.AssignDeterministicNodeIdsElapsedMilliseconds}");
            lines.Add($"FreezeCreateAnchorsMs: {freeze.CreateAnchorsElapsedMilliseconds}");
            lines.Add($"FreezeCreateNodeIdTableMs: {freeze.CreateNodeIdTableElapsedMilliseconds}");
            lines.Add($"FreezeRemapNodesMs: {freeze.RemapNodesElapsedMilliseconds}");
            lines.Add($"FreezeRemapEdgesMs: {freeze.RemapEdgesElapsedMilliseconds}");
            lines.Add($"FreezeBuildQueryIndexMs: {freeze.BuildQueryIndexElapsedMilliseconds}");
            lines.Add($"FreezePopulateEdgeBucketsMs: {freeze.PopulateEdgeIndexBucketsElapsedMilliseconds}");
            lines.Add($"FreezeOrderEdgesMs: {freeze.OrderEdgesElapsedMilliseconds}");
            lines.Add($"FreezeOrderNodesMs: {freeze.OrderNodesElapsedMilliseconds}");
            lines.Add($"FreezeSnapshotHashMs: {freeze.SnapshotHashElapsedMilliseconds}");
            lines.Add($"FreezeBuildAdjacencyMs: {freeze.BuildAdjacencyElapsedMilliseconds}");
            lines.Add($"FreezeBuildKindAdjacencyMs: {freeze.BuildKindAdjacencyElapsedMilliseconds}");
            lines.Add($"FreezeBuildEdgeKindIndexMs: {freeze.BuildEdgeKindIndexElapsedMilliseconds}");
            lines.Add($"FreezeBuildNodeKindIndexMs: {freeze.BuildNodeKindIndexElapsedMilliseconds}");
            lines.Add($"FreezeBuildFilePathIndexMs: {freeze.BuildFilePathIndexElapsedMilliseconds}");
            lines.Add($"FreezeDistinctAnchors: {freeze.DistinctAnchorCount}");
            lines.Add($"DataFlowFlowNodes: {dataFlow.FlowNodeCount}");
            lines.Add($"DataFlowDefinitionFacts: {dataFlow.DefinitionFactCount}");
            lines.Add($"DataFlowUsedFacts: {dataFlow.UsedFactCount}");
            lines.Add($"DataFlowCandidateEdges: {dataFlow.CandidateEdgeCount}");
            lines.Add($"DataFlowPeakBufferedCandidateBatches: {dataFlow.PeakBufferedCandidateBatchCount}");
            lines.Add($"DataFlowSkippedMethods: {dataFlow.SkippedMethodCount}");
        }

        if (result.MarkAnalysisTelemetry is not null)
        {
            var mark = result.MarkAnalysisTelemetry;
            lines.Add($"MarkRuleCount: {mark.RuleTelemetry.Count}");
            lines.Add($"MarkAtomicCandidateIndexHits: {mark.AtomicCandidateIndexHitCount}");
            lines.Add($"MarkAtomicCandidateIndexMisses: {mark.AtomicCandidateIndexMissCount}");
            lines.Add($"MarkOperationLookupHits: {mark.OperationLookupCacheHitCount}");
            lines.Add($"MarkOperationLookupMisses: {mark.OperationLookupCacheMissCount}");
            lines.Add($"MarkGraphBindingHits: {mark.GraphBindingIndexHitCount}");
            lines.Add($"MarkGraphBindingMisses: {mark.GraphBindingIndexMissCount}");
            lines.Add($"MarkRegionCacheHits: {mark.RegionCacheHitCount}");
            lines.Add($"MarkRegionCacheMisses: {mark.RegionCacheMissCount}");
            lines.Add($"MarkTargetMatchHits: {mark.TargetMatchCacheHitCount}");
            lines.Add($"MarkTargetMatchMisses: {mark.TargetMatchCacheMissCount}");
            lines.Add($"SliceQueryCacheHits: {mark.SliceQueryCacheHitCount}");
            lines.Add($"SliceQueryCacheMisses: {mark.SliceQueryCacheMissCount}");
            var slowestRule = mark.RuleTelemetry
              .OrderByDescending(rule => rule.ElapsedMilliseconds)
              .FirstOrDefault();
            if (slowestRule is not null)
            {
                lines.Add(
                  $"SlowestMarkRule: {slowestRule.RuleId} elapsedMs={slowestRule.ElapsedMilliseconds} candidates={slowestRule.CandidateMarkCount} accepted={slowestRule.AcceptedMarkCount} graphBindingFallbacks={slowestRule.GraphBindingFallbackCount}");
            }
        }

        if (result.StructureViewCacheTelemetry is not null)
        {
            var structureView = result.StructureViewCacheTelemetry;
            lines.Add($"StructureViewRequests: {structureView.RequestCount}");
            lines.Add($"StructureViewCacheHits: {structureView.CacheHitCount}");
            lines.Add($"StructureViewCacheMisses: {structureView.CacheMissCount}");
            lines.Add($"StructureViewUniqueFragmentSets: {structureView.UniqueFragmentSetCount}");
            lines.Add($"StructureViewMaxCachedViews: {structureView.MaxCachedViewCount}");
            lines.Add($"StructureViewHitRate: {structureView.CacheHitRate:0.###}");
        }

        var diagnostics = result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>();
        lines.Add($"Diagnostics: {diagnostics.Count}");
        foreach (var diagnostic in diagnostics)
        {
            lines.Add(
              $"DIAGNOSTIC {diagnostic.Severity} {diagnostic.Id} {diagnostic.FilePath}:{diagnostic.Start}..{diagnostic.End}: {diagnostic.Message}");
        }

        lines.Add("--- Rewritten Source ---");
        lines.Add(result.RewrittenSource ?? string.Empty);
        return lines;
    }

    private static string GetNodeKindText(SyntaxNode node)
    {
        return node is CSharpSyntaxNode csharpNode
          ? csharpNode.Kind().ToString()
          : node.RawKind.ToString();
    }
}
