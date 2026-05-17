namespace Rules;

public sealed record RuleMetadata(
  string RuleId,
  string Name,
  bool EnabledByDefault);
