using RoslynPrototype.Marking;

namespace RoslynPrototype.Propagation;

public sealed record PropagatedMarkRecord(
  string RuleId,
  MarkRecord Mark,
  MarkRecord SourceMark,
  int Depth);
