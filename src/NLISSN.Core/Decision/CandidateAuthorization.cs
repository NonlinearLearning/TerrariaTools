using Microsoft.CodeAnalysis;
using NLISSN.Core.Lifting;
using NLISSN.Core.Marking;

namespace NLISSN.Core.Decision;

/// <summary>
/// The result of checking whether one producer-owned proof may authorize one
/// concrete edit. Authorization is deliberately separate from both facts and
/// the final plan.
/// </summary>
public sealed record CandidateAuthorization
{
  public CandidateAuthorization(
    string candidateId,
    SyntaxNode target,
    DecisionActionKind action,
    EditIntentStatus status,
    string reason,
    IEnumerable<DecisionActionKind>? allowedActions = null,
    IEnumerable<string>? proofReferences = null,
    IEnumerable<string>? readSet = null,
    IEnumerable<string>? eraseSet = null,
    IEnumerable<string>? writeSet = null,
    IEnumerable<string>? preserveSet = null,
    BehaviorBudget? behaviorBudget = null,
    DecisionComposition composition = DecisionComposition.Unknown,
    ResidualMapping? residualMapping = null,
    string? atomicGroup = null,
    string? sourceVersion = null,
    EditIntent? intent = null)
  {
    if (string.IsNullOrWhiteSpace(candidateId))
    {
      throw new ArgumentException("An authorization candidate ID cannot be empty.", nameof(candidateId));
    }

    ArgumentNullException.ThrowIfNull(target);
    CandidateId = candidateId;
    Target = target;
    Action = action;
    Status = status;
    Reason = reason ?? string.Empty;
    AllowedActions = ToSet(allowedActions ?? new[] { action });
    ProofReferences = ToSet(proofReferences);
    ReadSet = ToSet(readSet);
    EraseSet = ToSet(eraseSet);
    WriteSet = ToSet(writeSet);
    PreserveSet = ToSet(preserveSet);
    BehaviorBudget = behaviorBudget ?? BehaviorBudget.Empty;
    Composition = composition;
    ResidualMapping = residualMapping;
    AtomicGroup = atomicGroup;
    SourceVersion = sourceVersion ?? target.SyntaxTree?.FilePath ?? string.Empty;
    Intent = intent;
  }

  public string CandidateId { get; }

  public SyntaxNode Target { get; }

  public DecisionActionKind Action { get; }

  public EditIntentStatus Status { get; }

  public string Reason { get; }

  public IReadOnlySet<DecisionActionKind> AllowedActions { get; }

  public IReadOnlySet<string> ProofReferences { get; }

  public IReadOnlySet<string> ReadSet { get; }

  public IReadOnlySet<string> EraseSet { get; }

  public IReadOnlySet<string> WriteSet { get; }

  public IReadOnlySet<string> PreserveSet { get; }

  public BehaviorBudget BehaviorBudget { get; }

  public DecisionComposition Composition { get; }

  public ResidualMapping? ResidualMapping { get; }

  public string? AtomicGroup { get; }

  public string SourceVersion { get; }

  /// <summary>The validated intent, when this authorization was materialized from one.</summary>
  public EditIntent? Intent { get; }

  public bool IsComplete => Status == EditIntentStatus.Complete;

  public bool Allows(DecisionActionKind action)
  {
    return AllowedActions.Contains(action);
  }

  public static CandidateAuthorization Rejected(
    string candidateId,
    SyntaxNode target,
    DecisionActionKind action,
    string reason)
  {
    return new CandidateAuthorization(
      candidateId,
      target,
      action,
      EditIntentStatus.Rejected,
      reason,
      allowedActions: Array.Empty<DecisionActionKind>());
  }

  public static CandidateAuthorization Unknown(
    string candidateId,
    SyntaxNode target,
    DecisionActionKind action,
    string reason)
  {
    return new CandidateAuthorization(
      candidateId,
      target,
      action,
      EditIntentStatus.Unknown,
      reason,
      allowedActions: Array.Empty<DecisionActionKind>());
  }

  private static HashSet<T> ToSet<T>(IEnumerable<T>? values)
  {
    return new HashSet<T>(values ?? Array.Empty<T>());
  }
}
