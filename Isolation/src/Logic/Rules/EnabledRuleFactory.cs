using Domain.Rules;
using Logic.Workspaces;

namespace Logic.Rules;

/// <summary>
/// 基于规则目录构造启用规则。
/// </summary>
public sealed class EnabledRuleFactory : IEnabledRuleFactory
{
    private readonly IRuleCatalog ruleCatalog;

    public EnabledRuleFactory(IRuleCatalog ruleCatalog)
    {
        this.ruleCatalog = ruleCatalog;
    }

    public EnabledRule Create(WorkspaceEnabledRuleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Create(new EnabledRuleActivationInput
        {
            RuleCode = input.RuleCode,
            DisplayName = input.DisplayName,
        });
    }

    public EnabledRule Create(EnabledRuleActivationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        RuleCode ruleCode = RuleCode.Create(input.RuleCode);
        RuleDescriptor descriptor = ruleCatalog.TryGet(ruleCode, out RuleDescriptor? registeredDescriptor)
            ? registeredDescriptor
            : CreateFallbackDescriptor(ruleCode);
        string displayName = string.IsNullOrWhiteSpace(input.DisplayName)
            ? descriptor.DisplayName
            : input.DisplayName.Trim();
        RuleScope ruleScope = CreateRuleScope(descriptor, input);

        return new EnabledRule(
            descriptor.RuleCode,
            displayName,
            descriptor.Priority,
            ruleScope,
            descriptor.RuleExecutionPolicy,
            descriptor.Tags,
            descriptor.Parameters);
    }

    private static RuleScope CreateRuleScope(RuleDescriptor descriptor, EnabledRuleActivationInput input)
    {
        IReadOnlyCollection<RuleTargetKind> targetKinds = input.TargetKinds ?? descriptor.RuleScope.TargetKinds;
        IReadOnlyCollection<RuleStageScope> stageScopes = input.StageScopes ?? descriptor.RuleScope.StageScopes;
        RuleBoundary boundary = input.Boundary ?? descriptor.RuleScope.Boundary;
        RulePropagationAllowance propagationAllowance =
            input.PropagationAllowance ?? descriptor.RuleScope.PropagationAllowance;

        return new RuleScope(targetKinds, stageScopes, boundary, propagationAllowance);
    }

    private static RuleDescriptor CreateFallbackDescriptor(RuleCode ruleCode)
    {
        return new RuleDescriptor(
            ruleCode,
            ruleCode.Value,
            RulePriority.Normal,
            new RuleScope(
                [RuleTargetKind.Method],
                [RuleStageScope.Marking],
                RuleBoundary.CurrentWorkspace,
                RulePropagationAllowance.CallPropagation),
            new RuleExecutionPolicy(
                RuleParticipationMode.Candidate,
                RuleConflictMode.PreferHigherPriority,
                RuleFailureMode.Warn,
                RuleSafetyLevel.Balanced,
                RuleEvidenceMode.AttachReason));
    }
}
