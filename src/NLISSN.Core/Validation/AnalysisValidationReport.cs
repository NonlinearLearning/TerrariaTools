using NLCPG.Model;

namespace NLISSN.Core.Validation;

public enum ValidationSeverity
{
  Error,
  Warning,
  Info,
}

public sealed record ValidationIssue(
  string Code,
  ValidationSeverity Severity,
  string StableKey,
  string Message,
  string? RuleId = null,
  NodeId? NodeId = null);

/// <summary>
/// Carries deterministic validation diagnostics across the analysis pipeline.
/// </summary>
public sealed record AnalysisValidationReport(IReadOnlyList<ValidationIssue> Issues)
{
  public static AnalysisValidationReport Empty { get; } = new(Array.Empty<ValidationIssue>());

  public bool IsValid => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

  public IReadOnlyDictionary<string, int> CountsByCode => Issues
    .GroupBy(issue => issue.Code, StringComparer.Ordinal)
    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

  public AnalysisValidationReport Combine(AnalysisValidationReport other)
  {
    ArgumentNullException.ThrowIfNull(other);
    return Create(Issues.Concat(other.Issues));
  }

  public static AnalysisValidationReport Create(IEnumerable<ValidationIssue> issues)
  {
    ArgumentNullException.ThrowIfNull(issues);
    return new AnalysisValidationReport(issues
      .OrderBy(issue => issue.StableKey, StringComparer.Ordinal)
      .ToArray());
  }
}
