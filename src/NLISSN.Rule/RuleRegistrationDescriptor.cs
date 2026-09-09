namespace NLISSN.Core.Pipeline;

/// <summary>
/// Runtime data for one compile-time discovered stage rule.
/// </summary>
public sealed record RuleRegistration<TStage>(
    string RuleId,
    string TypeName,
    string FullyQualifiedName,
    Func<TStage> Factory)
    where TStage : class, IRuleDefinition;
