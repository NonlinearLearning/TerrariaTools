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
            lines.Add($"CandidateMethods: {result.Stats.CandidateMethodCount}");
            lines.Add($"DeletedMethods: {result.Stats.DeletedMethodCount}");
            lines.Add($"ElapsedMs: {result.Stats.ElapsedMilliseconds}");
        }

        var diagnostics = result.Diagnostics ?? Array.Empty<AnalysisDiagnostic>();
        lines.Add($"Diagnostics: {diagnostics.Count}");
        foreach (var diagnostic in diagnostics)
        {
            lines.Add(
              $"DIAGNOSTIC {diagnostic.Severity} {diagnostic.Id} {diagnostic.FilePath}:{diagnostic.Start}..{diagnostic.End}: {diagnostic.Message}");
        }

        lines.Add("--- Rewritten Source ---");
        lines.Add(result.RewrittenSource);
        return lines;
    }

    private static string GetNodeKindText(SyntaxNode node)
    {
        return node is CSharpSyntaxNode csharpNode
          ? csharpNode.Kind().ToString()
          : node.RawKind.ToString();
    }
}
