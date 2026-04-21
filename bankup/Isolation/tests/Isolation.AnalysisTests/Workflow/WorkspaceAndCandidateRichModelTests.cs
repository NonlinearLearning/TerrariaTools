using Domain.Common;
using Domain.Execution;
using Domain.Propagation;
using Domain.Rules;
using Domain.Workspaces;
using Logic.Rules;
using Logic.Workspaces;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class WorkspaceAndCandidateRichModelTests
{
    [Fact]
    public void WorkspaceContext_resolves_relative_analysis_paths_from_solution_directory()
    {
        WorkspaceContext context = WorkspaceContext.Create("D:/repo/demo.sln", "latest");
        context.RegisterProject(new ProjectDescriptor("Core", "src/Core/Core.csproj"));
        context.RegisterDocument(DocumentPath.Create("src/Core/Entry.cs"));

        string resolvedProjectPath = context.ResolveAnalysisSourcePath(null);
        IReadOnlyCollection<string> resolvedDocuments = context.ResolveAnalysisDocuments(2);

        Assert.Equal("D:/repo/src/Core/Core.csproj", resolvedProjectPath);
        Assert.Contains("D:/repo/src/Core/Entry.cs", resolvedDocuments);
    }

    [Fact]
    public void WorkspaceContext_keeps_registration_idempotent()
    {
        WorkspaceContext context = WorkspaceContext.Create("demo.sln", "latest");

        context.RegisterProject(new ProjectDescriptor("Core", "src/Core/Core.csproj"));
        context.RegisterProject(new ProjectDescriptor("Core", "src/Core/Core.csproj"));
        context.RegisterDocument(DocumentPath.Create("src/Core/Entry.cs"));
        context.RegisterDocument(DocumentPath.Create("src/Core/Entry.cs"));
        context.RegisterReference(new ReferenceDescriptor("xunit", "2.0.0"));
        context.RegisterReference(new ReferenceDescriptor("xunit", "2.0.0"));

        Assert.Single(context.Projects);
        Assert.Single(context.Documents);
        Assert.Single(context.References);
    }

    [Fact]
    public void WorkspaceContextBuilder_uses_workspace_default_preset_when_rule_inputs_are_empty()
    {
        WorkspaceContextBuilder builder = new(
            new WorkspaceDefaultRulePreset(),
            new WorkspaceRuleDefaultsBuilder(new EnabledRuleFactory(new RuleCatalog())));

        WorkspaceContext context = builder.Build(new WorkspaceContextBuildInput
        {
            SolutionPath = "demo.sln",
            LanguageVersion = "latest",
            RunMode = RunMode.FullWorkflow,
        });

        EnabledRule rule = Assert.Single(context.RuleSet.EnabledRules);
        Assert.Equal("workflow.rule", rule.RuleCode.Value);
    }

    [Fact]
    public void WorkspaceContextBuilder_merges_explicit_rules_over_workspace_default_without_duplicates()
    {
        WorkspaceContextBuilder builder = new(
            new WorkspaceDefaultRulePreset(),
            new WorkspaceRuleDefaultsBuilder(new EnabledRuleFactory(new RuleCatalog())));

        WorkspaceContext context = builder.Build(new WorkspaceContextBuildInput
        {
            SolutionPath = "demo.sln",
            LanguageVersion = "latest",
            RunMode = RunMode.FullWorkflow,
            RuleInputs =
            [
                new WorkspaceEnabledRuleInput
                {
                    RuleCode = "workflow.rule",
                    DisplayName = "Workflow Rule",
                },
            ],
        });

        EnabledRule rule = Assert.Single(context.RuleSet.EnabledRules);
        Assert.Equal("workflow.rule", rule.RuleCode.Value);
        Assert.Equal("Workflow Rule", rule.DisplayName);
    }

    [Fact]
    public void WorkspaceContext_prepare_records_native_event_and_normalizes_same_project_path()
    {
        WorkspaceContext context = WorkspaceContext.Create("D:/repo/demo.sln", "latest");
        Guid correlationId = Guid.NewGuid();

        context.RegisterProject(new ProjectDescriptor("Core", "src/Core/Core.csproj"));
        context.RegisterProject(new ProjectDescriptor("Core", "D:/repo/src/Core/Core.csproj"));
        context.RegisterDocument(DocumentPath.Create("src/Core/Entry.cs"));
        context.Prepare(correlationId);

        Assert.Single(context.Projects);
        Assert.Contains(context.DomainEvents, item => item.EventName == "WorkspacePrepared" && item.CorrelationId == correlationId);
    }

    [Fact]
    public void RuleSet_rejects_blocking_evidence_rule_and_last_rule_removal()
    {
        RuleSet ruleSet = RuleSet.Create(
            "default",
            new RuleExecutionPolicy(
                RuleParticipationMode.Candidate,
                RuleConflictMode.PreferHigherPriority,
                RuleFailureMode.Warn,
                RuleSafetyLevel.Balanced,
                RuleEvidenceMode.AttachReason));
        EnabledRule validRule = CreateRule("workflow.rule", RuleParticipationMode.Candidate, RuleFailureMode.Warn);

        ruleSet.AddRule(validRule);

        Assert.Throws<InvalidOperationException>(() =>
            ruleSet.AddRule(CreateRule("workflow.evidence", RuleParticipationMode.EvidenceOnly, RuleFailureMode.BlockWorkflow)));
        Assert.Throws<InvalidOperationException>(() => ruleSet.RemoveRule(validRule.RuleCode));
    }

    [Fact]
    public void ChangeCandidate_registers_propagation_without_self_loops_or_duplicates()
    {
        ChangeCandidate candidate = ChangeCandidate.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            "PlayerTools.Entry",
            CandidateKind.Method,
            CandidateReason.CallChainMatched,
            ScenarioTag.MethodDeletion);

        candidate.RegisterPropagation(TargetName.Create("PlayerTools.Entry"), "self", 1);
        candidate.RegisterPropagation(TargetName.Create("PlayerTools.Helper"), "linked", 1);
        candidate.RegisterPropagation(TargetName.Create("PlayerTools.Helper"), "linked", 2);

        Assert.Single(candidate.PropagationTraces);
        Assert.Equal("PlayerTools.Helper", candidate.PropagationTraces.Single().TargetName);
    }

    [Fact]
    public void ChangeCandidate_rejects_conflicting_parent_coverage()
    {
        ChangeCandidate candidate = ChangeCandidate.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            "PlayerTools.Entry",
            CandidateKind.Method,
            CandidateReason.CallChainMatched,
            ScenarioTag.MethodDeletion);

        Guid firstParent = Guid.NewGuid();
        candidate.MarkCoveredByParentAction(firstParent);

        Assert.Throws<InvalidOperationException>(() => candidate.MarkCoveredByParentAction(Guid.NewGuid()));
    }

    [Fact]
    public void ChangeCandidate_rejects_propagation_mutation_after_parent_coverage()
    {
        ChangeCandidate candidate = ChangeCandidate.Create(
            Guid.NewGuid(),
            RuleCode.Create("workflow.rule"),
            "PlayerTools.Entry",
            CandidateKind.Method,
            CandidateReason.CallChainMatched,
            ScenarioTag.MethodDeletion);
        candidate.MarkCoveredByParentAction(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => candidate.SetSliceBoundary(
            new SliceBoundary("ClosureBoundary", SliceDirection.Forward, 1, false)));
        Assert.Throws<InvalidOperationException>(() => candidate.AddPropagationTrace(
            new PropagationTrace("PlayerTools.Entry", "PlayerTools.Helper", "linked", 1)));
    }

    [Fact]
    public void SliceBoundary_and_propagationTrace_require_explicit_positive_propagation_shape()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SliceBoundary("invalid", SliceDirection.Unknown, 1, false));
        Assert.Throws<InvalidOperationException>(() =>
            new SliceBoundary("invalid", SliceDirection.Forward, 0, false));
        Assert.Throws<InvalidOperationException>(() =>
            new PropagationTrace("PlayerTools.Entry", "PlayerTools.Helper", "linked", 0));
    }

    [Fact]
    public void PlanTarget_exposes_value_objects_and_compatibility_strings()
    {
        PlanTarget target = new(
            DocumentPath.Create("demo.cs"),
            "PlayerTools.Helper",
            "Helper(int)",
            "Helper");

        Assert.Equal("PlayerTools.Helper", target.TargetName);
        Assert.Equal("PlayerTools.Helper", target.TargetNameValue.Value);
        Assert.Equal("Helper(int)", target.MemberSignature);
        Assert.Equal("Helper(int)", target.MemberSignatureValue?.Value);
    }

    private static EnabledRule CreateRule(
        string ruleCode,
        RuleParticipationMode participationMode,
        RuleFailureMode failureMode)
    {
        return new EnabledRule(
            RuleCode.Create(ruleCode),
            ruleCode,
            RulePriority.Normal,
            new RuleScope(
                [RuleTargetKind.Method],
                [RuleStageScope.Marking],
                RuleBoundary.CurrentWorkspace,
                RulePropagationAllowance.CallPropagation),
            new RuleExecutionPolicy(
                participationMode,
                RuleConflictMode.PreferHigherPriority,
                failureMode,
                RuleSafetyLevel.Balanced,
                RuleEvidenceMode.AttachReason));
    }
}
