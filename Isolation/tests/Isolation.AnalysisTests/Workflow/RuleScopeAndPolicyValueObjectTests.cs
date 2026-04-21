using Domain.Rules;
using Logic.Rules;
using Logic.Workspaces;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class RuleScopeAndPolicyValueObjectTests
{
    [Fact]
    public void RuleExecutionPolicy_keeps_candidate_and_blocking_semantics_after_split()
    {
        RuleExecutionPolicy candidatePolicy = new(
            RuleParticipationMode.Candidate,
            RuleConflictMode.PreferHigherPriority,
            RuleFailureMode.Warn,
            RuleSafetyLevel.Balanced,
            RuleEvidenceMode.AttachReason);
        RuleExecutionPolicy evidenceOnlyPolicy = new(
            RuleParticipationMode.EvidenceOnly,
            RuleConflictMode.BlockOnConflict,
            RuleFailureMode.BlockWorkflow,
            RuleSafetyLevel.Conservative,
            RuleEvidenceMode.AttachRisk);

        Assert.True(candidatePolicy.CanProduceCandidate());
        Assert.False(candidatePolicy.CanBlockWorkflow());
        Assert.False(candidatePolicy.IsEvidenceOnly());

        Assert.False(evidenceOnlyPolicy.CanProduceCandidate());
        Assert.True(evidenceOnlyPolicy.CanBlockWorkflow());
        Assert.True(evidenceOnlyPolicy.IsEvidenceOnly());
    }

    [Fact]
    public void RuleScope_keeps_target_and_stage_membership_semantics_after_split()
    {
        RuleScope scope = new(
            [RuleTargetKind.Method, RuleTargetKind.Member],
            [RuleStageScope.Marking, RuleStageScope.Decision],
            RuleBoundary.CurrentWorkspace,
            RulePropagationAllowance.CallPropagation);

        Assert.True(scope.CanTarget(RuleTargetKind.Method));
        Assert.False(scope.CanTarget(RuleTargetKind.Class));
        Assert.True(scope.CanRunAt(RuleStageScope.Decision));
        Assert.False(scope.CanRunAt(RuleStageScope.Evidence));
    }

    [Fact]
    public void Workspace_rule_defaults_keep_split_rule_vocabulary_stable()
    {
        WorkspaceContextBuilder builder = new(
            new WorkspaceDefaultRulePreset(),
            new WorkspaceRuleDefaultsBuilder(new EnabledRuleFactory(new RuleCatalog())));

        WorkspaceContextBuildInput input = new()
        {
            SolutionPath = "demo.sln",
            LanguageVersion = "latest",
            RunMode = Domain.Workspaces.RunMode.FullWorkflow,
        };

        Domain.Workspaces.WorkspaceContext context = builder.Build(input);
        EnabledRule rule = Assert.Single(context.RuleSet.EnabledRules);

        Assert.Equal(RuleParticipationMode.Candidate, rule.RuleExecutionPolicy.ParticipationMode);
        Assert.Equal(RuleFailureMode.Warn, rule.RuleExecutionPolicy.FailureMode);
        Assert.Equal(RuleBoundary.CurrentWorkspace, rule.RuleScope.Boundary);
        Assert.Equal(RulePropagationAllowance.CallPropagation, rule.RuleScope.PropagationAllowance);
    }
}
