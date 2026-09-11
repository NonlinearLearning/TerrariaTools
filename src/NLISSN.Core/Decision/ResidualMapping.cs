namespace NLISSN.Core.Decision;

public enum ResidualMappingKind
{
  Retained,
  Replaced,
  Removed,
  Unknown
}

public sealed record ResidualNodeMapping(
  string OldNodeKey,
  ResidualMappingKind Kind,
  string? NewNodeKey = null,
  string? Reason = null);

/// <summary>Maps original-tree nodes through a parent replacement.</summary>
public sealed record ResidualMapping
{
  public ResidualMapping(IEnumerable<ResidualNodeMapping>? entries = null)
  {
    Entries = (entries ?? Array.Empty<ResidualNodeMapping>())
      .GroupBy(entry => entry.OldNodeKey, StringComparer.Ordinal)
      .Select(group => group.Last())
      .ToDictionary(entry => entry.OldNodeKey, StringComparer.Ordinal);
  }

  public IReadOnlyDictionary<string, ResidualNodeMapping> Entries { get; }

  public bool TryMap(string oldNodeKey, out ResidualNodeMapping mapping)
  {
    return Entries.TryGetValue(oldNodeKey, out mapping!);
  }

  public static ResidualMapping Retains(string oldNodeKey, string newNodeKey)
  {
    return new ResidualMapping(new[]
    {
      new ResidualNodeMapping(oldNodeKey, ResidualMappingKind.Retained, newNodeKey)
    });
  }

  public static ResidualMapping Replaces(string oldNodeKey, string newNodeKey)
  {
    return new ResidualMapping(new[]
    {
      new ResidualNodeMapping(oldNodeKey, ResidualMappingKind.Replaced, newNodeKey)
    });
  }

  public static ResidualMapping Removes(string oldNodeKey)
  {
    return new ResidualMapping(new[]
    {
      new ResidualNodeMapping(oldNodeKey, ResidualMappingKind.Removed)
    });
  }

  public static ResidualMapping Unknown(string oldNodeKey, string reason)
  {
    return new ResidualMapping(new[]
    {
      new ResidualNodeMapping(oldNodeKey, ResidualMappingKind.Unknown, Reason: reason)
    });
  }
}
