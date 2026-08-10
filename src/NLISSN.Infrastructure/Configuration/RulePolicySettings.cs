namespace NLISSN.Infrastructure.Configuration;

internal sealed record RulePolicySettings(
    string? TargetName,
    string? DeleteClass,
    IReadOnlySet<string> DisabledRuleTypes,
    bool ValidateBindings,
    bool DeleteUnreachableMethods,
    bool DeleteUnreferencedMethods,
    bool ClearUnusedInterfaceImplementations,
    bool PrivatizeInternalOnlyPublicMethods);
