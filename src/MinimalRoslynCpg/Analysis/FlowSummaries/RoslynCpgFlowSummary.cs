namespace MinimalRoslynCpg.Analysis.FlowSummaries;

public enum RoslynCpgFlowSummaryEndpointKind
{
  Receiver,
  Parameter,
  Return,
  PassThrough,
  Block,
}

public sealed record RoslynCpgFlowSummaryEndpoint(
  RoslynCpgFlowSummaryEndpointKind Kind,
  int ParameterOrdinal = 0)
{
  public static RoslynCpgFlowSummaryEndpoint Receiver { get; } = new(RoslynCpgFlowSummaryEndpointKind.Receiver);

  public static RoslynCpgFlowSummaryEndpoint Return { get; } = new(RoslynCpgFlowSummaryEndpointKind.Return, -1);

  public static RoslynCpgFlowSummaryEndpoint Parameter(int ordinal) =>
    new(RoslynCpgFlowSummaryEndpointKind.Parameter, ordinal);
}

/// <summary>
/// Conservative call-flow contract. Absent summaries are cuts unless a caller explicitly chooses another policy.
/// </summary>
public sealed record RoslynCpgFlowSummary(
  string AssemblyIdentity,
  string ContainingMetadataName,
  string MethodName,
  int GenericArity,
  IReadOnlyList<RoslynCpgFlowSummaryEndpoint> Sources,
  RoslynCpgFlowSummaryEndpoint Target)
{
  public string StableKey => string.Join(
    "|",
    AssemblyIdentity,
    ContainingMetadataName,
    MethodName,
    GenericArity.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// Identifies the provenance of a summary resolution without treating absent flow as a proven flow.
/// </summary>
public enum RoslynCpgFlowSummaryResolution
{
  Project,
  Framework,
  Unknown,
}

public sealed record RoslynCpgFlowSummaryLookupResult(
  RoslynCpgFlowSummaryResolution Resolution,
  RoslynCpgFlowSummary? Summary)
{
  public static RoslynCpgFlowSummaryLookupResult Unknown { get; } = new(
    RoslynCpgFlowSummaryResolution.Unknown,
    null);
}

/// <summary>
/// Resolves explicit project contracts before framework contracts. Missing contracts remain unknown.
/// </summary>
public sealed class RoslynCpgFlowSummaryRegistry
{
  private readonly IReadOnlyDictionary<string, RoslynCpgFlowSummary> _projectOverrides;
  private readonly IReadOnlyDictionary<string, RoslynCpgFlowSummary> _frameworkSummaries;

  public RoslynCpgFlowSummaryRegistry(
    IEnumerable<RoslynCpgFlowSummary>? projectOverrides = null,
    IEnumerable<RoslynCpgFlowSummary>? frameworkSummaries = null)
  {
    _projectOverrides = ToStableKeyIndex(projectOverrides);
    _frameworkSummaries = ToStableKeyIndex(frameworkSummaries);
  }

  public RoslynCpgFlowSummaryLookupResult Resolve(string stableKey)
  {
    ArgumentException.ThrowIfNullOrEmpty(stableKey);
    if (_projectOverrides.TryGetValue(stableKey, out var project))
    {
      return new RoslynCpgFlowSummaryLookupResult(RoslynCpgFlowSummaryResolution.Project, project);
    }

    if (_frameworkSummaries.TryGetValue(stableKey, out var framework))
    {
      return new RoslynCpgFlowSummaryLookupResult(RoslynCpgFlowSummaryResolution.Framework, framework);
    }

    return RoslynCpgFlowSummaryLookupResult.Unknown;
  }

  private static IReadOnlyDictionary<string, RoslynCpgFlowSummary> ToStableKeyIndex(
    IEnumerable<RoslynCpgFlowSummary>? summaries)
  {
    return (summaries ?? Array.Empty<RoslynCpgFlowSummary>())
      .OrderBy(summary => summary.StableKey, StringComparer.Ordinal)
      .ToDictionary(summary => summary.StableKey, StringComparer.Ordinal);
  }
}
