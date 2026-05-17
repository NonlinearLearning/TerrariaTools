using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;
using RoslynPrototype.Decision;

namespace RoslynPrototype.Rewrite;

public sealed record PrototypeAnalysisResult(
  IReadOnlyList<MarkRecord> SeedMarks,
  IReadOnlyList<PropagatedMarkRecord> PropagatedMarks,
  IReadOnlyList<RuleDecision> Decisions,
  IReadOnlyList<RewriteEdit> Edits,
  string RewrittenSource,
  string DiffText,
  string? DiffFilePath);
