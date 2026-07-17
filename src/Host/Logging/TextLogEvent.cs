namespace RoslynPrototype.Application.Logging;

internal sealed record TextLogEvent(
  DateTimeOffset TimestampUtc,
  TextLogLevel Level,
  TextLogCategory Category,
  TextLogEventType EventType,
  string Message,
  string RunId,
  string? Operation = null,
  string? InputKind = null,
  string? InputPath = null,
  string? Source = null,
  string? FilePath = null,
  string? Phase = null,
  int? Dop = null,
  IReadOnlyList<TextLogField>? Fields = null);
