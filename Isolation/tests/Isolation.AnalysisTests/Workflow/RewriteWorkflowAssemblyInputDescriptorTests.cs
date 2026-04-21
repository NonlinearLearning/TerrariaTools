using Domain.Decision;
using Domain.Execution;
using Domain.Workspaces;
using Logic.Workflow;
using Xunit;

namespace Isolation.AnalysisTests.Workflow;

public sealed class RewriteWorkflowAssemblyInputDescriptorTests
{
    [Fact]
    public void AssemblyInput_maps_nested_target_and_execution_descriptors_to_stage_inputs()
    {
        RewriteDecision decision = RewriteDecision.Create("demo", ConfidenceLevel.High);
        RewriteWorkflowAssemblyInput input = new()
        {
            RunCorrelationId = Guid.NewGuid(),
            WorkspaceContext = WorkspaceContext.Create("demo.sln", "latest"),
            CandidateId = Guid.NewGuid(),
            Decision = decision,
            Target = new RewriteWorkflowTargetDescriptor
            {
                TargetName = "PlayerTools.Helper",
                DocumentPath = "demo.cs",
                MemberSignature = "Helper(int)",
                AnchorText = "Helper",
            },
            Execution = new RewriteWorkflowExecutionDescriptor
            {
                SourceCode = DemoSource,
                ClassName = "PlayerTools",
                MethodName = "Helper",
                ParameterCount = 1,
            },
            PlanAction = PlanAction.DeleteMethod,
        };

        RewriteWorkflowPlanStageInput plan = input.ToPlanStageInput();
        RewriteWorkflowExecutionStageInput execution = input.ToExecutionStageInput();
        RewriteWorkflowEventStageInput events = input.ToEventStageInput();

        Assert.Equal("PlayerTools.Helper", plan.TargetName);
        Assert.Equal("demo.cs", plan.DocumentPath);
        Assert.Equal("Helper(int)", plan.MemberSignature);
        Assert.Equal("Helper", plan.AnchorText);
        Assert.Equal(DemoSource, execution.SourceCode);
        Assert.Equal("PlayerTools", execution.ClassName);
        Assert.Equal("Helper", execution.MethodName);
        Assert.Equal(1, execution.ParameterCount);
        Assert.Equal("PlayerTools.Helper", events.TargetName);
        Assert.Equal("demo.cs", events.DocumentPath);
    }

    [Fact]
    public void AssemblyInput_keeps_flat_initializers_backward_compatible_by_hydrating_descriptors()
    {
        RewriteWorkflowAssemblyInput input = new()
        {
            TargetName = "PlayerTools.Entry",
            DocumentPath = "entry.cs",
            MemberSignature = "Entry(int)",
            AnchorText = "Entry",
            SourceCode = DemoSource,
            ClassName = "PlayerTools",
            MethodName = "Entry",
            ParameterCount = 1,
        };

        Assert.Equal("PlayerTools.Entry", input.Target.TargetName);
        Assert.Equal("entry.cs", input.Target.DocumentPath);
        Assert.Equal("Entry(int)", input.Target.MemberSignature);
        Assert.Equal("Entry", input.Target.AnchorText);
        Assert.Equal(DemoSource, input.Execution.SourceCode);
        Assert.Equal("PlayerTools", input.Execution.ClassName);
        Assert.Equal("Entry", input.Execution.MethodName);
        Assert.Equal(1, input.Execution.ParameterCount);
    }

    private const string DemoSource = """
using System;

public class PlayerTools
{
    public int Entry(int offset)
    {
        return Helper(offset);
    }

    public int Helper(int value)
    {
        return value + 1;
    }
}
""";
}
