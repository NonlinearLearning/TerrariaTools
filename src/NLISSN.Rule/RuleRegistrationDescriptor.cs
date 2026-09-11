namespace NLISSN.Core.Pipeline;

public sealed record RuleRegistration<TStage>(
    string RuleId,
    string TypeName,
    string FullyQualifiedName,
    RuleFeature Feature,
    Func<TStage> Factory)
    where TStage : class, IRuleDefinition;
