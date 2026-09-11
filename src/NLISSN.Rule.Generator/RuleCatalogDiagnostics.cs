using Microsoft.CodeAnalysis;

namespace NLISSN.Rule.Generator;

internal static class RuleCatalogDiagnostics
{
    public static readonly DiagnosticDescriptor MissingContract = new(
      "NLRCG001",
      "Rule catalog contract is missing",
      "The target compilation is missing required rule catalog contract type '{0}'",
      "RuleCatalog",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidStage = new(
      "NLRCG002",
      "Rule does not belong to a supported stage",
      "Rule type '{0}' implements IRuleDefinition but does not belong to exactly one supported rule stage",
      "RuleCatalog",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingRegistration = new(
      "NLRCG003",
      "Rule registration metadata is missing",
      "Concrete rule type '{0}' has no RuleRegistration metadata and will not be included in the generated catalog",
      "RuleCatalog",
      DiagnosticSeverity.Warning,
      isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidFeature = new(
      "NLRCG004",
      "Rule feature metadata is invalid",
      "Rule type '{0}' has an invalid RuleFeature value",
      "RuleCatalog",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidRuleId = new(
      "NLRCG005",
      "RuleId is not a compile-time string",
      "Rule type '{0}' must expose a non-blank compile-time string RuleId",
      "RuleCatalog",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateRuleId = new(
      "NLRCG006",
      "RuleId is duplicated",
      "RuleId '{0}' is declared by more than one generated rule registration",
      "RuleCatalog",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InaccessibleRule = new(
      "NLRCG007",
      "Rule cannot be constructed by generated code",
      "Rule type '{0}' must have an accessible type and accessible parameterless constructor",
      "RuleCatalog",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidRuleType = new(
      "NLRCG008",
      "Rule type cannot be generated",
      "Registered rule type '{0}' must be a non-abstract, closed class",
      "RuleCatalog",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CatalogConflict = new(
      "NLRCG009",
      "Generated catalog type conflicts with an existing type",
      "The fixed generated catalog type 'NLISSN.Rules.GeneratedRuleCatalog' already exists in the target compilation",
      "RuleCatalog",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidMetadata = new(
      "NLRCG010",
      "Rule catalog metadata is contradictory",
      "Rule type '{0}' cannot combine RuleRegistration and RuleCatalogIgnore metadata or repeat registration metadata",
      "RuleCatalog",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FeatureMissingStage = new(
      "NLRCG011",
      "Rule feature is missing a stage",
      "Registered rule feature '{0}' is missing required stage '{1}'",
      "RuleCatalog",
      DiagnosticSeverity.Error,
      isEnabledByDefault: true);
}
