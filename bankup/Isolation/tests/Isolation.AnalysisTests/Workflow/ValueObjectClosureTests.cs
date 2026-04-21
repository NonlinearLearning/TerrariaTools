using Domain.Analysis;
using Domain.Analysis.Events;
using Domain.Common;
using Domain.Marking.Events;
using Domain.Output.Verification;
using Domain.Propagation;
using Domain.Propagation.Events;
using Domain.Rewrite.Artifacts;
using Domain.Workspaces;
using Domain.Rules;
using Domain.Execution;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class ValueObjectClosureTests
{
    [Fact]
    public void ProjectDescriptor_and_InputDescriptor_expose_workspace_path_value_objects()
    {
        ProjectDescriptor project = new("Core", "src\\Core\\Core.csproj");
        InputDescriptor input = InputDescriptor.Create(
            InputOrigin.Project,
            "src\\Core\\Core.csproj",
            RunMode.FullWorkflow,
            RuleSet.Create(
                "default",
                new RuleExecutionPolicy(
                    RuleParticipationMode.Candidate,
                    RuleConflictMode.PreferHigherPriority,
                    RuleFailureMode.Warn,
                    RuleSafetyLevel.Balanced,
                    RuleEvidenceMode.AttachReason)));

        Assert.Equal("src/Core/Core.csproj", project.Path);
        Assert.Equal("src/Core/Core.csproj", project.PathValue.Value);
        Assert.Equal("src/Core/Core.csproj", input.SourcePath);
        Assert.Equal("src/Core/Core.csproj", input.SourcePathValue.Value);
    }

    [Fact]
    public void ReferenceDescriptor_exposes_reference_value_objects()
    {
        ReferenceDescriptor reference = new("Newtonsoft.Json", "13.0.3");

        Assert.Equal("Newtonsoft.Json", reference.Name);
        Assert.Equal("Newtonsoft.Json", reference.NameValue.Value);
        Assert.Equal("13.0.3", reference.Version);
        Assert.Equal("13.0.3", reference.VersionValue.Value);
    }

    [Fact]
    public void PropagationTrace_and_domain_events_keep_target_value_objects_with_string_projection()
    {
        PropagationTrace trace = new(TargetName.Create("PlayerTools.Entry"), TargetName.Create("PlayerTools.Helper"), "linked", 1);
        LinkedActionDetectedDomainEvent linked = new(Guid.NewGuid(), Guid.NewGuid(), TargetName.Create("PlayerTools.Entry"), TargetName.Create("DeleteMethod"), "linked");
        CandidateCoveredByParentActionDomainEvent covered = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TargetName.Create("PlayerTools.Helper"), TargetName.Create("PlayerTools.Entry"));

        Assert.Equal("PlayerTools.Entry", trace.SourceName);
        Assert.Equal("PlayerTools.Helper", trace.TargetName);
        Assert.Equal("PlayerTools.Entry", linked.TargetNameValue.Value);
        Assert.Equal("DeleteMethod", linked.LinkedActionNameValue.Value);
        Assert.Equal("PlayerTools.Helper", covered.TargetNameValue.Value);
        Assert.Equal("PlayerTools.Entry", covered.ParentTargetNameValue.Value);
    }

    [Fact]
    public void Analysis_input_snapshot_and_events_expose_workspace_and_target_value_objects()
    {
        Guid workspaceContextId = Guid.NewGuid();
        AnalysisInputDescriptor input = AnalysisInputDescriptor.Create(
            workspaceContextId,
            "src\\Core\\Entry.cs",
            AnalysisSourceKind.SourceFile);
        AnalysisCpgSnapshot snapshot = AnalysisCpgSnapshot.Create(
            workspaceContextId,
            MinimumAnalysisTarget.Method,
            TargetName.Create("PlayerTools.Entry"),
            2);
        AnalysisSnapshotBuiltDomainEvent built = new(snapshot.Id, Guid.NewGuid(), TargetName.Create("PlayerTools.Entry"), 2);
        ProgramFactPublishedDomainEvent published = new(snapshot.Id, Guid.NewGuid(), TargetName.Create("PlayerTools.Entry"), 3);
        RuleTargetIdentifiedDomainEvent identified = new(Guid.NewGuid(), Guid.NewGuid(), "workflow.rule", TargetName.Create("PlayerTools.Entry"));

        Assert.Equal("src/Core/Entry.cs", input.SourcePath);
        Assert.Equal("src/Core/Entry.cs", input.SourcePathValue.Value);
        Assert.Equal("PlayerTools.Entry", snapshot.EntrySymbol);
        Assert.Equal("PlayerTools.Entry", snapshot.EntrySymbolValue.Value);
        Assert.Equal("PlayerTools.Entry", built.EntrySymbolValue.Value);
        Assert.Equal("PlayerTools.Entry", published.SubjectNameValue.Value);
        Assert.Equal("PlayerTools.Entry", identified.TargetNameValue.Value);
    }

    [Fact]
    public void Rewrite_and_verification_models_keep_target_value_objects_with_string_projection()
    {
        CodeRewriteResult rewriteResult = CodeRewriteResult.Create(
            CodeRewriteKind.DeleteMethod,
            TargetName.Create("PlayerTools.Entry"),
            "class Demo {}",
            true);
        FileChange fileChange = new(
            DocumentPath.Create("demo.cs"),
            "changed",
            [TargetName.Create("PlayerTools.Entry"), TargetName.Create("PlayerTools.Helper")]);
        StaticReasoningEvidence evidence = new(TargetName.Create("PlayerTools.Entry"), "reason-chain");

        Assert.Equal("PlayerTools.Entry", rewriteResult.TargetName);
        Assert.Equal("PlayerTools.Entry", rewriteResult.TargetNameValue.Value);
        Assert.Equal(["PlayerTools.Entry", "PlayerTools.Helper"], fileChange.AffectedTargets);
        Assert.Equal(["PlayerTools.Entry", "PlayerTools.Helper"], fileChange.AffectedTargetValues.Select(static item => item.Value).ToArray());
        Assert.Equal("PlayerTools.Entry", evidence.SubjectName);
        Assert.Equal("PlayerTools.Entry", evidence.SubjectNameValue.Value);
    }

    [Fact]
    public void RuntimeClosureBoundary_rejects_unknown_integrity_and_self_reference_mapping()
    {
        RuntimeClosure runtimeClosure = RuntimeClosure.Create(
            "PlayerTools",
            "Entry",
            "PlayerToolsRuntimeClosure",
            "class Demo {}");

        Assert.Throws<InvalidOperationException>(() =>
            runtimeClosure.MarkIntegrity(ClosureIntegrityStatus.Unknown));
        Assert.Throws<InvalidOperationException>(() =>
            runtimeClosure.AddReferenceMapping(new ReferenceMapping("PlayerTools.Helper", "PlayerTools.Helper")));
    }
}
