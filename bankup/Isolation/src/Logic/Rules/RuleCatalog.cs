using Domain.Rules;

namespace Logic.Rules;

/// <summary>
/// 提供工作区默认规则的稳定目录。
/// </summary>
public sealed class RuleCatalog : IRuleCatalog
{
    private readonly IReadOnlyDictionary<RuleCode, RuleDescriptor> descriptors;

    public RuleCatalog()
    {
        RuleScope workspaceMarkingScope = new(
            [RuleTargetKind.Method],
            [RuleStageScope.Marking],
            RuleBoundary.CurrentWorkspace,
            RulePropagationAllowance.CallPropagation);
        RuleExecutionPolicy candidateExecutionPolicy = new(
            RuleParticipationMode.Candidate,
            RuleConflictMode.PreferHigherPriority,
            RuleFailureMode.Warn,
            RuleSafetyLevel.Balanced,
            RuleEvidenceMode.AttachReason);

        Dictionary<RuleCode, RuleDescriptor> entries = new();
        Register(entries, "workflow.rule", workspaceMarkingScope, candidateExecutionPolicy);
        Register(entries, "marking.rule-target", workspaceMarkingScope, candidateExecutionPolicy);
        Register(entries, "propagation.rule", workspaceMarkingScope, candidateExecutionPolicy);
        Register(entries, "workflow.guard", workspaceMarkingScope, candidateExecutionPolicy);
        Register(entries, "decision.protect", workspaceMarkingScope, candidateExecutionPolicy);
        Register(entries, "public-contract", workspaceMarkingScope, candidateExecutionPolicy);
        Register(entries, "unknown", workspaceMarkingScope, candidateExecutionPolicy);

        descriptors = entries;
    }

    public RuleDescriptor Get(RuleCode ruleCode)
    {
        return descriptors.TryGetValue(ruleCode, out RuleDescriptor? descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"未注册规则目录项：{ruleCode.Value}");
    }

    public bool TryGet(RuleCode ruleCode, out RuleDescriptor? descriptor)
    {
        return descriptors.TryGetValue(ruleCode, out descriptor);
    }

    public bool Contains(RuleCode ruleCode)
    {
        return descriptors.ContainsKey(ruleCode);
    }

    private static void Register(
        IDictionary<RuleCode, RuleDescriptor> entries,
        string ruleCode,
        RuleScope ruleScope,
        RuleExecutionPolicy ruleExecutionPolicy)
    {
        RuleCode code = RuleCode.Create(ruleCode);
        entries[code] = new RuleDescriptor(
            code,
            ruleCode,
            RulePriority.Normal,
            ruleScope,
            ruleExecutionPolicy);
    }
}
