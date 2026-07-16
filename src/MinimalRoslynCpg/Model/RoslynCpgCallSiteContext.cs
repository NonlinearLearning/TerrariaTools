namespace MinimalRoslynCpg.Model;

/// <summary>
/// Captures the structured source location of a callsite-backed edge context.
/// </summary>
public readonly record struct RoslynCpgCallSiteContext(
  string FilePath,
  int SpanStart,
  int SpanEnd,
  string DisplayName)
{
  public RoslynCpgContextId ToContextId()
  {
    return new RoslynCpgContextId(
      $"callsite:{FilePath}:{SpanStart}:{SpanEnd}:{DisplayName}");
  }
}
