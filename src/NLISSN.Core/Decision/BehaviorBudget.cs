namespace NLISSN.Core.Decision;

/// <summary>Typed obligations that a destructive edit must preserve.</summary>
public enum BehaviorObligationKind
{
  Binding,
  Type,
  Overload,
  Scope,
  ControlFlow,
  ShortCircuit,
  GetterCall,
  Await,
  Assignment,
  Exception,
  MethodGroup,
  DelegateConversion,
  Effect
}

/// <summary>
/// Describes which obligations an edit must preserve and which behavior
/// changes it is explicitly allowed to make.
/// </summary>
public sealed record BehaviorBudget
{
  public BehaviorBudget(
    IEnumerable<BehaviorObligationKind>? requiredPreservations = null,
    IEnumerable<BehaviorObligationKind>? allowedChanges = null)
  {
    RequiredPreservations = (requiredPreservations ?? Array.Empty<BehaviorObligationKind>())
      .Distinct()
      .Order()
      .ToArray();
    AllowedChanges = (allowedChanges ?? Array.Empty<BehaviorObligationKind>())
      .Distinct()
      .Order()
      .ToArray();
  }

  public IReadOnlyList<BehaviorObligationKind> RequiredPreservations { get; }

  public IReadOnlyList<BehaviorObligationKind> AllowedChanges { get; }

  public static BehaviorBudget Empty { get; } = new();

  public static BehaviorBudget For(params BehaviorObligationKind[] requiredPreservations)
  {
    return new BehaviorBudget(requiredPreservations);
  }

  public bool IsCompatibleWith(BehaviorBudget other)
  {
    ArgumentNullException.ThrowIfNull(other);
    return !RequiredPreservations
      .Intersect(other.AllowedChanges)
      .Any();
  }
}
