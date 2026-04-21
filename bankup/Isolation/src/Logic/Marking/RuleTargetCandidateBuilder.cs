using Domain.Analysis;
using Domain.Marking;
using Domain.Propagation;
using Domain.Rules;
using Logic.Rules;

namespace Logic.Marking;

/// <summary>
/// 基于规则目标构造候选执行输入。
/// </summary>
public sealed class RuleTargetCandidateBuilder : IRuleTargetCandidateBuilder
{
    private readonly IChangeCandidateMarker changeCandidateMarker;
    private readonly IEnabledRuleFactory enabledRuleFactory;
    private readonly IRuleCatalog ruleCatalog;

    public RuleTargetCandidateBuilder(
        IChangeCandidateMarker changeCandidateMarker,
        IEnabledRuleFactory enabledRuleFactory,
        IRuleCatalog ruleCatalog)
    {
        this.changeCandidateMarker = changeCandidateMarker;
        this.enabledRuleFactory = enabledRuleFactory;
        this.ruleCatalog = ruleCatalog;
    }


    public RuleExecutionResult Build(RuleTargetCandidateBuildInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.RuleTarget);

        RuleDescriptor descriptor = ResolveDescriptor(input.RuleTarget.RuleCode);
        RuleSet ruleSet = RuleSet.Create(input.RuleSetName, descriptor.RuleExecutionPolicy);
        ruleSet.AddRule(enabledRuleFactory.Create(new EnabledRuleActivationInput
        {
            RuleCode = input.RuleTarget.RuleCode.Value,
            DisplayName = input.RuleTarget.RuleCode.Value,
            TargetKinds = [MapTargetKind(input.RuleTarget.Node.NodeType)],
            StageScopes = [RuleStageScope.Marking],
            Boundary = RuleBoundary.CurrentWorkspace,
            PropagationAllowance = RulePropagationAllowance.CallPropagation,
        }));

        return changeCandidateMarker.Execute(new RuleExecutionInput
        {
            RuleSet = ruleSet,
            RuleTarget = input.RuleTarget,
            CandidateKind = MapCandidateKind(input.RuleTarget.Node.NodeType),
            ScenarioTags = input.ScenarioTags,
        });
    }

    private static CandidateKind MapCandidateKind(CpgType nodeType)
    {
        return nodeType switch
        {
            CpgType.TypeDecl => CandidateKind.Type,
            CpgType.Method => CandidateKind.Method,
            _ => CandidateKind.Member,
        };
    }

    private static RuleTargetKind MapTargetKind(CpgType nodeType)
    {
        return nodeType switch
        {
            CpgType.TypeDecl => RuleTargetKind.Class,
            CpgType.Method => RuleTargetKind.Method,
            _ => RuleTargetKind.Member,
        };
    }

    private RuleDescriptor ResolveDescriptor(RuleCode ruleCode)
    {
        return ruleCatalog.Get(ruleCode);
    }
}
