using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;
using NLISSN.Core.Propagation;

namespace NLISSN.Core.Lifting;

/// <summary>
/// A fact capability describes what an observation can participate in. It is
/// intentionally not an action-authority enum: StructureComplete does not
/// belong here.
/// </summary>
public enum FactCapability
{
  Unknown = 0,
  AtomicTarget = 1,
  TopologyHost = 2,
  ChildComposable = 3,
  GlobalTarget = 4
}

/// <summary>
/// Certainty of an observed fact. Every value other than Available is
/// potentially unsafe for a destructive proof.
/// </summary>
public enum FactCertainty
{
  Available = 0,
  Truncated = 1,
  Unavailable = 2,
  Unresolved = 3,
  Unsupported = 4,
  Failed = 5
}

public static class FactCapabilityRules
{
  public static FactCapability For(RuleFactKind? factKind)
  {
    return factKind switch
    {
      RuleFactKind.FlowLogicalExpression => FactCapability.TopologyHost,
      RuleFactKind.LiftExpressionHost => FactCapability.TopologyHost,
      RuleFactKind.LiftLogicalReduction => FactCapability.ChildComposable,
      RuleFactKind.FlowUnaryExpression or RuleFactKind.FlowConditionalExpression => FactCapability.ChildComposable,
      RuleFactKind.TargetExpression or RuleFactKind.TargetTypeSyntax or RuleFactKind.TargetDeclaration => FactCapability.AtomicTarget,
      RuleFactKind.UnreachableMethod or RuleFactKind.UnreferencedMethod or
        RuleFactKind.InternalOnlyPublicMethodMarked or RuleFactKind.InternalOnlyPublicMethodPropagated or
        RuleFactKind.InternalOnlyPublicMethodLifted or RuleFactKind.UnusedInterfaceImplementationMarked or
        RuleFactKind.UnusedInterfaceImplementationPropagated or RuleFactKind.UnusedInterfaceImplementationLifted => FactCapability.GlobalTarget,
      _ => FactCapability.Unknown
    };
  }

  public static FactCapability For(MarkRecord mark)
  {
    ArgumentNullException.ThrowIfNull(mark);
    return mark.Capability == FactCapability.Unknown
      ? For(mark.FactKind)
      : mark.Capability;
  }
}

/// <summary>
/// Stable identity of one fact input. Payload and provenance are part of the
/// identity so two facts on the same syntax node cannot erase each other.
/// </summary>
public sealed record FactIdentity(
  string SourceTreeVersion,
  string AnchorNodeKey,
  RuleFactKind? FactKind,
  string PayloadIdentity,
  string ProvenanceIdentity)
{
  public string StableKey => string.Join(
    "|",
    SourceTreeVersion,
    AnchorNodeKey,
    FactKind?.ToString() ?? string.Empty,
    PayloadIdentity,
    ProvenanceIdentity);

  public static FactIdentity Create(
    SyntaxNode anchor,
    string sourceTreeVersion,
    RuleFactKind? factKind,
    object? payload,
    FactProvenance provenance)
  {
    ArgumentNullException.ThrowIfNull(anchor);
    ArgumentNullException.ThrowIfNull(provenance);
    return new FactIdentity(
      sourceTreeVersion,
      BuildNodeKey(anchor),
      factKind,
      ComputePayloadIdentity(payload),
      provenance.Identity);
  }

  public static string ForSourceTree(SyntaxNode syntaxNode)
  {
    ArgumentNullException.ThrowIfNull(syntaxNode);
    var text = syntaxNode.SyntaxTree?.GetText().ToString() ?? string.Empty;
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
    return $"tree:{Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant()}";
  }

  public static string BuildNodeKey(SyntaxNode syntaxNode)
  {
    return string.Join(
      ":",
      syntaxNode.SyntaxTree?.FilePath ?? string.Empty,
      syntaxNode.SpanStart.ToString(CultureInfo.InvariantCulture),
      syntaxNode.Span.Length.ToString(CultureInfo.InvariantCulture),
      syntaxNode.RawKind.ToString(CultureInfo.InvariantCulture));
  }

  public static string ComputePayloadIdentity(object? payload)
  {
    if (payload is null)
    {
      return string.Empty;
    }

    var serialized = BuildStablePayloadProjection(payload, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(serialized));
    return Convert.ToHexString(bytes).ToLowerInvariant();
  }

  private static string BuildStablePayloadProjection(
    object? value,
    ISet<object> visited,
    int depth)
  {
    if (value is null)
    {
      return "null";
    }

    if (depth > 8)
    {
      return value.GetType().FullName ?? value.GetType().Name;
    }

    if (value is SyntaxNode syntaxNode)
    {
      return $"syntax:{value.GetType().FullName}:{BuildNodeKey(syntaxNode)}:{syntaxNode.ToString()}";
    }

    if (value is string text)
    {
      return $"string:{text}";
    }

    var type = value.GetType();
    if (type.IsPrimitive || type.IsEnum || value is decimal or DateTime or DateTimeOffset or Guid)
    {
      return $"{type.FullName}:{value}";
    }

    if (value is System.Collections.IEnumerable enumerable)
    {
      var items = enumerable
        .Cast<object?>()
        .Select(item => BuildStablePayloadProjection(item, visited, depth + 1));
      return $"{type.FullName}:[{string.Join(",", items)}]";
    }

    if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
    {
      return $"{type.FullName}:{value}";
    }

    if (!visited.Add(value))
    {
      return $"cycle:{type.FullName}";
    }

    var properties = type
      .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
      .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
      .OrderBy(property => property.Name, StringComparer.Ordinal)
      .Select(property =>
      {
        try
        {
          return property.Name + "=" + BuildStablePayloadProjection(property.GetValue(value), visited, depth + 1);
        }
        catch
        {
          return property.Name + "=<unavailable>";
        }
      });
    return $"{type.FullName}:{{{string.Join(",", properties)}}}";
  }
}

/// <summary>
/// A typed view of a Mark/Propagate/Lift fact. It preserves the full source
/// record and payload instead of projecting a propagated fact to its syntax node.
/// </summary>
public sealed record CoverageEvidence
{
  public CoverageEvidence(
    FactIdentity identity,
    FactCapability capability,
    FactCertainty certainty,
    MarkRecord mark,
    MarkRecord? sourceMark,
    object? payload,
    int depth,
    FactProvenance provenance)
  {
    Identity = identity ?? throw new ArgumentNullException(nameof(identity));
    Capability = capability;
    Certainty = certainty;
    Mark = mark ?? throw new ArgumentNullException(nameof(mark));
    SourceMark = sourceMark;
    Payload = payload;
    Depth = depth;
    Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
  }

  public FactIdentity Identity { get; }

  public FactCapability Capability { get; }

  public FactCertainty Certainty { get; }

  public MarkRecord Mark { get; }

  public MarkRecord? SourceMark { get; }

  public object? Payload { get; }

  public int Depth { get; }

  public FactProvenance Provenance { get; }

  public string AnchorNodeKey => Identity.AnchorNodeKey;

  public bool IsAvailable => Certainty == FactCertainty.Available;

  public static CoverageEvidence FromMark(
    MarkRecord mark,
    object? payload = null,
    FactProvenance? provenance = null,
    string? sourceTreeVersion = null)
  {
    ArgumentNullException.ThrowIfNull(mark);
    var resolvedProvenance = provenance ?? mark.Provenance ?? FactProvenance.ForMark(mark.RuleId);
    var resolvedCapability = FactCapabilityRules.For(mark);
    var identity = FactIdentity.Create(
      mark.SyntaxNode,
      sourceTreeVersion ?? mark.SourceTreeVersion,
      mark.FactKind,
      payload,
      resolvedProvenance);
    return new CoverageEvidence(
      identity,
      resolvedCapability,
      mark.Certainty,
      mark,
      null,
      payload,
      0,
      resolvedProvenance);
  }

  public static CoverageEvidence FromPropagated(
    PropagatedMarkRecord propagated,
    string? sourceTreeVersion = null)
  {
    ArgumentNullException.ThrowIfNull(propagated);
    var provenance = propagated.Provenance ?? FactProvenance.ForPropagation(
      propagated.RuleId,
      propagated.SourceMark.Provenance,
      propagated.Depth);
    var identity = FactIdentity.Create(
      propagated.Mark.SyntaxNode,
      sourceTreeVersion ?? propagated.Mark.SourceTreeVersion,
      propagated.Mark.FactKind,
      propagated.Payload,
      provenance);
    return new CoverageEvidence(
      identity,
      FactCapabilityRules.For(propagated.Mark),
      propagated.Mark.Certainty,
      propagated.Mark,
      propagated.SourceMark,
      propagated.Payload,
      propagated.Depth,
      provenance);
  }

  public static CoverageEvidence FromLifted(
    LiftedMarkRecord lifted,
    string? sourceTreeVersion = null)
  {
    ArgumentNullException.ThrowIfNull(lifted);
    var provenance = lifted.Provenance ?? new FactProvenance(
      lifted.RuleId,
      FactSourceStage.Lift,
      new[] { lifted.SourceMark.RuleId },
      depth: lifted.Depth);
    var identity = FactIdentity.Create(
      lifted.Mark.SyntaxNode,
      sourceTreeVersion ?? lifted.Mark.SourceTreeVersion,
      lifted.Mark.FactKind,
      lifted.Payload,
      provenance);
    return new CoverageEvidence(
      identity,
      FactCapabilityRules.For(lifted.Mark),
      lifted.Mark.Certainty,
      lifted.Mark,
      lifted.SourceMark,
      lifted.Payload,
      lifted.Depth,
      provenance);
  }
}
