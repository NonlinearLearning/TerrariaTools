using Domain.Decision;
using Domain.Rules;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class RewriteDecisionValueObjectTests
{
    [Fact]
    public void Decision_supporting_types_normalize_strings_and_reset_candidate_resolution()
    {
        Guid candidateId = Guid.NewGuid();
        Guid rightCandidateId = Guid.NewGuid();
        RuleCode ruleCode = RuleCode.Create("decision.protect");
        RewriteDecision decision = RewriteDecision.Create(" demo ", ConfidenceLevel.Low);

        decision.Approve(candidateId, ApprovalReason.PropagationBounded);
        decision.AddProtection(new DecisionProtection(candidateId, ruleCode, " keep public api "));
        decision.AddConflict(new DecisionConflict(candidateId, rightCandidateId, " overlap detected "));

        RewriteDecisionOutcome outcome = new(
            candidateId,
            ApprovalReason.ClosureIntegrityVerified,
            null,
            [new DecisionProtection(candidateId, ruleCode, " verified boundary ")],
            [new DecisionConflict(candidateId, Guid.NewGuid(), " shadow overlap ")]);

        bool approved = decision.ApplyOutcome(outcome, Guid.NewGuid());

        Assert.True(approved);
        Assert.Equal("demo", decision.DecisionName);
        Assert.Equal(ApprovalReason.ClosureIntegrityVerified, decision.Approvals[candidateId]);
        Assert.Empty(decision.Rejections);

        DecisionProtection protection = Assert.Single(decision.Protections);
        Assert.Equal("verified boundary", protection.Description);
        Assert.Equal(ruleCode, protection.RuleCode);

        DecisionConflict conflict = Assert.Single(decision.Conflicts);
        Assert.Equal(candidateId, conflict.LeftCandidateId);
        Assert.Equal("shadow overlap", conflict.Description);
    }
}
