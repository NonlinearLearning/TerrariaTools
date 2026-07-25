namespace Deletion.Logging;

public sealed record RunLogContext(
  string RunId,
  string Operation,
  string InputKind,
  string? InputPath,
  int? Dop);
