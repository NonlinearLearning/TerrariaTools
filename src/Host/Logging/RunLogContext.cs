namespace RoslynPrototype.Application.Logging;

internal sealed record RunLogContext(
  string RunId,
  string Operation,
  string InputKind,
  string? InputPath,
  int? Dop);
