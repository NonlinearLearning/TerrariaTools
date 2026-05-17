using RoslynPrototype.Marking;

namespace RoslynPrototype.Decision;

public sealed record DecisionCandidate(
  MarkRecord Mark,
  MarkRecord? SourceMark,
  bool IsPropagated);
