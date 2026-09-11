namespace NLISSN.Core.Lifting;

/// <summary>
/// Identifies the pipeline stage that produced a fact.
/// </summary>
public enum FactSourceStage
{
  Unknown = 0,
  Mark = 1,
  Propagate = 2,
  Lift = 3,
  Propose = 4
}

/// <summary>
/// Stable, structured provenance for a fact. Explanatory text is deliberately
/// not part of the identity used for fact de-duplication.
/// </summary>
public sealed record FactProvenance
{
  public FactProvenance(
    string ruleId,
    FactSourceStage stage,
    IReadOnlyList<string>? sourceFactIds = null,
    IReadOnlyList<string>? propagationPath = null,
    int depth = 0)
  {
    if (string.IsNullOrWhiteSpace(ruleId))
    {
      throw new ArgumentException("A fact provenance rule id cannot be empty.", nameof(ruleId));
    }

    if (depth < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(depth));
    }

    RuleId = ruleId;
    Stage = stage;
    SourceFactIds = (sourceFactIds ?? Array.Empty<string>()).ToArray();
    PropagationPath = (propagationPath ?? Array.Empty<string>()).ToArray();
    Depth = depth;
  }

  public string RuleId { get; }

  public FactSourceStage Stage { get; }

  public IReadOnlyList<string> SourceFactIds { get; }

  public IReadOnlyList<string> PropagationPath { get; }

  public int Depth { get; }

  public string Identity => string.Join(
    "|",
    RuleId,
    Stage,
    Depth,
    string.Join(",", SourceFactIds.Order(StringComparer.Ordinal)),
    string.Join(",", PropagationPath));

  public static FactProvenance ForMark(string ruleId, int depth = 0)
  {
    return new FactProvenance(ruleId, FactSourceStage.Mark, depth: depth);
  }

  public static FactProvenance ForPropagation(
    string ruleId,
    FactProvenance? source,
    int depth)
  {
    var sourceIds = source is null
      ? Array.Empty<string>()
      : new[] { source.Identity };
    var path = source is null
      ? new[] { ruleId }
      : source.PropagationPath.Concat(new[] { ruleId }).ToArray();
    return new FactProvenance(ruleId, FactSourceStage.Propagate, sourceIds, path, depth);
  }
}
