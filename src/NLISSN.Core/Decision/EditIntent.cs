using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Decision;

public enum EditIntentStatus
{
  Complete,
  Unknown,
  Rejected
}

/// <summary>
/// A proposal candidate with explicit original-tree read/erase/write/preserve
/// sets. It is still a draft until DecisionPlanner selects it.
/// </summary>
public sealed record EditIntent
{
  public EditIntent(
    string candidateId,
    SyntaxNode anchor,
    DecisionActionKind action,
    IEnumerable<string>? readSet = null,
    IEnumerable<string>? eraseSet = null,
    IEnumerable<string>? writeSet = null,
    IEnumerable<string>? preserveSet = null,
    IEnumerable<string>? proofReferences = null,
    BehaviorBudget? behaviorBudget = null,
    DecisionComposition composition = DecisionComposition.Unknown,
    ResidualMapping? residualMapping = null,
    string? atomicGroup = null,
    string? sourceVersion = null,
    EditIntentStatus status = EditIntentStatus.Complete,
    bool dominatesChildren = false,
    bool declarationBoundary = false)
  {
    if (string.IsNullOrWhiteSpace(candidateId))
    {
      throw new ArgumentException("An edit intent candidate ID cannot be empty.", nameof(candidateId));
    }

    ArgumentNullException.ThrowIfNull(anchor);
    CandidateId = candidateId;
    Anchor = anchor;
    AnchorNodeKey = DecisionCpgFactory.BuildNodeKey(anchor);
    Action = action;
    ReadSet = ToSet(readSet);
    var erased = ToSet(eraseSet);
    erased.Add(AnchorNodeKey);
    EraseSet = erased;
    WriteSet = ToSet(writeSet);
    PreserveSet = ToSet(preserveSet);
    ProofReferences = ToSet(proofReferences);
    BehaviorBudget = behaviorBudget ?? BehaviorBudget.Empty;
    Composition = composition;
    ResidualMapping = residualMapping;
    AtomicGroup = atomicGroup;
    SourceVersion = sourceVersion ?? anchor.SyntaxTree?.FilePath ?? string.Empty;
    Status = status;
    DominatesChildren = dominatesChildren;
    DeclarationBoundary = declarationBoundary;
  }

  public string CandidateId { get; }

  public SyntaxNode Anchor { get; }

  public string AnchorNodeKey { get; }

  public DecisionActionKind Action { get; }

  public IReadOnlySet<string> ReadSet { get; }

  public IReadOnlySet<string> EraseSet { get; }

  public IReadOnlySet<string> WriteSet { get; }

  public IReadOnlySet<string> PreserveSet { get; }

  public IReadOnlySet<string> ProofReferences { get; }

  public BehaviorBudget BehaviorBudget { get; }

  public DecisionComposition Composition { get; }

  public ResidualMapping? ResidualMapping { get; }

  public string? AtomicGroup { get; }

  public string SourceVersion { get; }

  public EditIntentStatus Status { get; }

  public bool DominatesChildren { get; }

  /// <summary>
  /// Marks a declaration-boundary delete whose entire original declaration
  /// region is consumed. This is distinct from StructureComplete, which is
  /// reserved for Lift-owned control-structure proofs.
  /// </summary>
  public bool DeclarationBoundary { get; }

  public bool HasCompleteProof => Status == EditIntentStatus.Complete && ProofReferences.Count > 0;

  public bool HasStructureCompleteProof =>
    ProofReferences.Any(reference => reference.Contains("StructureComplete", StringComparison.Ordinal));

  public bool HasCompleteDeclarationBoundaryProof =>
    DeclarationBoundary &&
    Status == EditIntentStatus.Complete &&
    ProofReferences.Any(reference =>
      reference.StartsWith("proof:DeclarationBoundary:", StringComparison.Ordinal)) &&
    Anchor.DescendantNodesAndSelf()
      .Select(DecisionCpgFactory.BuildNodeKey)
      .All(EraseSet.Contains);

  public bool Erases(string nodeKey) => EraseSet.Contains(nodeKey);

  public bool IsPreserveCompatibleWith(EditIntent other)
  {
    ArgumentNullException.ThrowIfNull(other);
    return BehaviorBudget.IsCompatibleWith(other.BehaviorBudget) &&
      !PreserveSet.Intersect(other.EraseSet).Any() &&
      !other.PreserveSet.Intersect(EraseSet).Any();
  }

  public static EditIntent Create(
    string candidateId,
    SyntaxNode anchor,
    DecisionActionKind action,
    IEnumerable<string>? consumedNodeKeys = null,
    IEnumerable<string>? eraseNodeKeys = null,
    IEnumerable<string>? writeNodeKeys = null,
    IEnumerable<string>? preserveNodeKeys = null,
    IEnumerable<string>? proofReferences = null,
    BehaviorBudget? behaviorBudget = null,
    DecisionComposition composition = DecisionComposition.Unknown,
    ResidualMapping? residualMapping = null,
    string? atomicGroup = null,
    string? sourceVersion = null,
    EditIntentStatus status = EditIntentStatus.Complete,
    bool dominatesChildren = false,
    bool declarationBoundary = false)
  {
    var anchorKey = DecisionCpgFactory.BuildNodeKey(anchor);
    return new EditIntent(
      candidateId,
      anchor,
      action,
      readSet: consumedNodeKeys,
      eraseSet: eraseNodeKeys ?? consumedNodeKeys ?? new[] { anchorKey },
      writeSet: writeNodeKeys,
      preserveSet: preserveNodeKeys,
      proofReferences,
      behaviorBudget,
      composition,
      residualMapping,
      atomicGroup,
      sourceVersion,
      status,
      dominatesChildren,
      declarationBoundary);
  }

  public static EditIntent Compatibility(DecisionUnit unit)
  {
    ArgumentNullException.ThrowIfNull(unit);
    var anchor = unit.SyntaxBindings[unit.Fragments[0].NodeId!.Value];
    var replacement = unit.Fragments
      .FirstOrDefault(fragment => string.Equals(fragment.Name, "replacement", StringComparison.Ordinal));
    return Create(
      DecisionFootprint.Compatibility(unit.RuleId, anchor, unit.Action).CandidateId,
      anchor,
      unit.Action,
      consumedNodeKeys: new[] { DecisionCpgFactory.BuildNodeKey(anchor) },
      writeNodeKeys: replacement is { NodeId: { } replacementId } && unit.SyntaxBindings.TryGetValue(replacementId, out var replacementNode)
        ? new[] { DecisionCpgFactory.BuildNodeKey(replacementNode) }
        : Array.Empty<string>(),
      composition: DecisionComposition.Unknown,
      status: EditIntentStatus.Unknown);
  }

  private static HashSet<string> ToSet(IEnumerable<string>? values)
  {
    return new HashSet<string>(
      (values ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)),
      StringComparer.Ordinal);
  }
}
