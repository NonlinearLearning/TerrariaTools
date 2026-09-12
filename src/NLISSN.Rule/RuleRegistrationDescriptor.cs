namespace NLISSN.Core.Pipeline;

/// <summary>
/// Runtime data for one compile-time discovered stage rule.
/// </summary>
public sealed record RuleRegistration<TStage>(
    string RuleId,
    string TypeName,
    string FullyQualifiedName,
    RuleFeature Feature,
    Func<TStage> Factory)
    where TStage : class, IRuleDefinition
{
    /// <summary>
    /// Preserves the descriptor shape used by the original core catalog.
    /// </summary>
    public RuleRegistration(
      string ruleId,
      string typeName,
      string fullyQualifiedName,
      Func<TStage> factory)
      : this(ruleId, typeName, fullyQualifiedName, RuleFeature.Core, factory)
    {
    }
}
