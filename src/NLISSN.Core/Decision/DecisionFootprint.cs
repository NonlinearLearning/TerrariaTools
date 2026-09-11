using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Decision;

/// <summary>Describes how a candidate may relate to other candidates.</summary>
public enum DecisionComposition
{
  Independent,
  Composable,
  OpaqueDominates,
  Exclusive,
  AtomicTransaction,
  Unknown
}

/// <summary>
/// The original-tree footprint of one candidate. Replacement nodes are not
/// part of ConsumedNodeKeys; they belong to the output tree instead.
/// </summary>
public sealed record DecisionFootprint
{
  public DecisionFootprint(
    string candidateId,
    string anchorNodeKey,
    DecisionActionKind action,
    IEnumerable<string>? consumedNodeKeys = null,
    DecisionComposition composition = DecisionComposition.Unknown,
    string? proofKind = null,
    bool dominatesChildren = false)
  {
    if (string.IsNullOrWhiteSpace(candidateId))
    {
      throw new ArgumentException("A decision candidate ID cannot be empty.", nameof(candidateId));
    }

    if (string.IsNullOrWhiteSpace(anchorNodeKey))
    {
      throw new ArgumentException("A decision footprint must have an anchor key.", nameof(anchorNodeKey));
    }

    CandidateId = candidateId;
    AnchorNodeKey = anchorNodeKey;
    Action = action;
    var consumed = new HashSet<string>(
      (consumedNodeKeys ?? new[] { anchorNodeKey }).Where(key => !string.IsNullOrWhiteSpace(key)),
      StringComparer.Ordinal);
    consumed.Add(anchorNodeKey);
    ConsumedNodeKeys = consumed;
    Composition = composition;
    ProofKind = proofKind;
    DominatesChildren = dominatesChildren;
  }

  public string CandidateId { get; }

  public string AnchorNodeKey { get; }

  public DecisionActionKind Action { get; }

  public IReadOnlySet<string> ConsumedNodeKeys { get; }

  public DecisionComposition Composition { get; }

  public string? ProofKind { get; }

  public bool DominatesChildren { get; }

  public bool HasProof(string proofKind)
  {
    return !string.IsNullOrWhiteSpace(ProofKind) &&
      (string.Equals(ProofKind, proofKind, StringComparison.Ordinal) ||
       ProofKind.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
         .Contains(proofKind, StringComparer.Ordinal));
  }

  public static DecisionFootprint Create(
    string ruleId,
    SyntaxNode anchor,
    DecisionActionKind action,
    IEnumerable<string>? consumedNodeKeys = null,
    DecisionComposition composition = DecisionComposition.Unknown,
    string? proofKind = null,
    bool dominatesChildren = false,
    string? candidateDiscriminator = null)
  {
    ArgumentNullException.ThrowIfNull(anchor);
    var anchorKey = DecisionCpgFactory.BuildNodeKey(anchor);
    var consumed = (consumedNodeKeys ?? new[] { anchorKey }).ToArray();
    var candidateId = BuildCandidateId(
      ruleId,
      anchorKey,
      action,
      consumed,
      composition,
      proofKind,
      dominatesChildren,
      candidateDiscriminator);
    return new DecisionFootprint(
      candidateId,
      anchorKey,
      action,
      consumed,
      composition,
      proofKind,
      dominatesChildren);
  }

  public static DecisionFootprint Compatibility(
    string ruleId,
    SyntaxNode anchor,
    DecisionActionKind action)
  {
    return Create(
      ruleId,
      anchor,
      action,
      new[] { DecisionCpgFactory.BuildNodeKey(anchor) },
      DecisionComposition.Unknown,
      proofKind: null,
      dominatesChildren: false);
  }

  private static string BuildCandidateId(
    string ruleId,
    string anchorKey,
    DecisionActionKind action,
    IEnumerable<string> consumedNodeKeys,
    DecisionComposition composition,
    string? proofKind,
    bool dominatesChildren,
    string? candidateDiscriminator)
  {
    var identity = string.Join(
      "|",
      ruleId,
      anchorKey,
      action,
      composition,
      proofKind ?? string.Empty,
      dominatesChildren,
      candidateDiscriminator ?? string.Empty,
      string.Join(";", consumedNodeKeys.Order(StringComparer.Ordinal)));
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
    return $"candidate:{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
  }
}
