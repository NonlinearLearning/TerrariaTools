using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Application;
using NLISSN.Core.Decision;
using NLISSN.Core.Marking;
using NLISSN.Core.Propagation;
using NLISSN.Core.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.Cli;
using RoslynPrototype.Tests.TestCodeSet.Reachability;
using RoslynPrototype.Tests.TestCodeSet.Target;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class GraphAnalyzerTests
{
    private readonly string _graphAnalyzerDiffFilePath;

    public GraphAnalyzerTests()
    {
        _graphAnalyzerDiffFilePath = BuildDiffArtifactWriter.GetDiffFilePath(
          "GraphAnalyzerTests.cs",
          "Target");
        BuildDiffArtifactWriter.InitializeDiffFile(_graphAnalyzerDiffFilePath);
    }

    [Fact]
    public void Analyze_TargetNameSample_DeletesLiftedSeedMarks()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.AtomicNameSource;

        var result = application.Analyze(source, "delete-s-object-sample.cs", CreateOptions("s"));

        Assert.Equal(2, result.SeedMarks.Count);
        Assert.NotEmpty(result.PropagatedMarks);
        Assert.All(result.SeedMarks, mark => Assert.NotNull(mark.PrimaryGraphNode));
        AssertContainsPropagatedKind(result, SyntaxKind.IfStatement);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);

        Assert.Equal(2, result.Decisions.Count);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.LocalDeclarationStatement));

        Assert.Equal(2, result.Edits.Count);
        TextDiffAssert.Contains("--- original #1 delete-s-object-sample.cs:", result.Diff, result.Diff);
        TextDiffAssert.Contains("var value = s.Seed + offset;", result.Diff, result.Diff);
        TextDiffAssert.Contains("+++ rewritten #1", result.Diff, result.Diff);
        TextDiffAssert.Contains("<deleted>", result.Diff, result.Diff);
        TextDiffAssert.DoesNotContain("var value =", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("if (s.IsReady)", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_DefinitionAssignment_DeletesLocalDeclarationStatement()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.DefinitionAssignmentSource;

        var result = application.Analyze(source, "definition-assignment.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_DefinitionAssignment_DeletesLocalDeclarationStatement), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.LocalDeclarationStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("var value = s.Seed + offset;", result.Diff, result.Diff);
        TextDiffAssert.DoesNotContain("var value = s.Seed + offset;", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_AssignmentStatement_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.AssignmentStatementSource;

        var result = application.Analyze(source, "assignment-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_AssignmentStatement_DeletesExpressionStatement), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.DoesNotContain(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.ExpressionStatement));
        Assert.Empty(result.Decisions);
        TextDiffAssert.Contains("offset += s.Seed;", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_ComplexDefinitionAssignment_PreservesLocalDeclarationWithSurvivingReturnReference()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.ComplexDefinitionAssignmentSource;

        var result = application.Analyze(source, "complex-definition-assignment.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ComplexDefinitionAssignment_PreservesLocalDeclarationWithSurvivingReturnReference), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          IsNodeKind(decision.FinalNode, SyntaxKind.LocalDeclarationStatement));
        TextDiffAssert.Contains("var value = (s.Seed + offset) * values[offset];", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return value;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_ChainedAssignmentStatement_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.ChainedAssignmentStatementSource;

        var result = application.Analyze(source, "chained-assignment-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ChainedAssignmentStatement_DeletesExpressionStatement), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.DoesNotContain(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.ExpressionStatement));
        Assert.Empty(result.Decisions);
        TextDiffAssert.Contains("left = right = s.Seed;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_DeconstructionAssignmentStatement_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.DeconstructionAssignmentStatementSource;

        var result = application.Analyze(source, "deconstruction-assignment-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_DeconstructionAssignmentStatement_DeletesExpressionStatement), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.ExpressionStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("(left, right) = (s.Seed, offset);", result.Diff, result.Diff);
        TextDiffAssert.DoesNotContain("(left, right) = (s.Seed, offset);", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_ObjectInitializerDefinitionAssignment_PreservesLocalDeclarationWithSurvivingReturnReference()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.ObjectInitializerDefinitionAssignmentSource;

        var result = application.Analyze(source, "object-initializer-definition-assignment.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ObjectInitializerDefinitionAssignment_PreservesLocalDeclarationWithSurvivingReturnReference), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.ObjectCreationExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          IsNodeKind(decision.FinalNode, SyntaxKind.LocalDeclarationStatement));
        TextDiffAssert.Contains("var holder = new Holder", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return holder;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_ComplexCompoundAssignmentStatement_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.ComplexCompoundAssignmentStatementSource;

        var result = application.Analyze(source, "complex-compound-assignment-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ComplexCompoundAssignmentStatement_DeletesExpressionStatement), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.DoesNotContain(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.ExpressionStatement));
        Assert.Empty(result.Decisions);
        TextDiffAssert.Contains("offset += s.Seed + offset * 2;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_AssignmentLeftOperand_StopsWithoutRewrite()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.AssignmentLeftOperandSource;

        var result = application.Analyze(source, "assignment-left-operand.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_AssignmentLeftOperand_StopsWithoutRewrite), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.ElementAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.Empty(result.Decisions);
        TextDiffAssert.Contains("values[s.Seed] = offset;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_DefinitionLeftOperand_DeletesLocalDeclarationStatement()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.DefinitionLeftOperandSource;

        var result = application.Analyze(source, "definition-left-operand.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_DefinitionLeftOperand_DeletesLocalDeclarationStatement), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.VariableDeclarator, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.LocalDeclarationStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("var s = offset + 1;", result.Diff, result.Diff);
        TextDiffAssert.DoesNotContain("var s = offset + 1;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_CallArgumentStatement_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.CallArgumentStatementSource;

        var result = application.Analyze(source, "call-argument-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_CallArgumentStatement_DeletesExpressionStatement), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.InvocationExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.ExpressionStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("Fun(s.Seed, 3);", result.Diff, result.Diff);
        TextDiffAssert.DoesNotContain("Fun(s.Seed, 3);", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_PropertyAccessDefinition_WhenPropertyNameMatches_DeletesLocalDeclarationStatement()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.PropertyAccessDefinitionSource;

        var result = application.Analyze(source, "property-access-definition.cs", CreateOptions("Seed"));
        AppendUnitTestDiff(nameof(Analyze_PropertyAccessDefinition_WhenPropertyNameMatches_DeletesLocalDeclarationStatement), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.LocalDeclarationStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("var value = holder.Seed;", result.Diff, result.Diff);
        TextDiffAssert.DoesNotContain("var value = holder.Seed;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_IndexAccessDefinition_WhenBaseOrIndexMatches_DeletesLocalDeclarationStatement()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.IndexAccessDefinitionSource;

        var result = application.Analyze(source, "index-access-definition.cs", CreateOptions("values"));
        AppendUnitTestDiff(nameof(Analyze_IndexAccessDefinition_WhenBaseOrIndexMatches_DeletesLocalDeclarationStatement), result.Diff);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.ElementAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.LocalDeclarationStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("var value = values[s.Seed];", result.Diff, result.Diff);
        TextDiffAssert.DoesNotContain("var value = values[s.Seed];", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_UnreachableMethodsSample_DeletesConfiguredMethods()
    {
        var application = CreateApplication(enableUnreachableMethodDeletion: true);
        var source = ReachabilitySources.UnreachableMethodsSource;

        var result = application.Analyze(source, "unreachable-method-sample.cs", CreateOptions(unreachableMethods: "DeadA,DeadB"));

        Assert.Equal(2, result.SeedMarks.Count);
        Assert.Empty(result.PropagatedMarks);
        Assert.All(result.SeedMarks, mark => Assert.Equal("Method", mark.PrimaryGraphNode!.DisplayKind));

        Assert.Equal(2, result.Decisions.Count);
        Assert.All(result.Decisions, decision =>
        {
            Assert.Equal(DecisionActionKind.Delete, decision.Action);
            Assert.Equal(SyntaxKind.MethodDeclaration, GetNodeKind(decision.FinalNode));
        });

        Assert.Equal(2, result.Edits.Count);
        TextDiffAssert.Contains("Main", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("Live", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("DeadA", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("DeadB", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_LogicalAndCondition_RightTargetDeletesIf()
    {
        var application = CreateApplication();
        var source = AtomicLogicalSources.LogicalAndConditionSource;

        var result = application.Analyze(source, "logical-and-sample.cs", CreateOptions("s"));

        Assert.Contains(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        TextDiffAssert.DoesNotContain("if (ready && s.IsReady)", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("return offset;", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("s.IsReady", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_WhileCondition_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.WhileConditionSource;

        var result = application.Analyze(source, "while-host-sample.cs", CreateOptions("s"));

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.WhileStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.WhileStatement));
        TextDiffAssert.DoesNotContain("while (s.IsReady)", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_WhileBody_DoesNotDeleteLoopWhenConditionIsUncovered()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.WhileBodySource;

        var result = application.Analyze(source, "while-body-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_WhileBody_DoesNotDeleteLoopWhenConditionIsUncovered), result.Diff);

        Assert.Single(result.SeedMarks);
        Assert.DoesNotContain(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.WhileStatement));
        Assert.DoesNotContain(result.Decisions, decision => IsNodeKind(decision.FinalNode, SyntaxKind.WhileStatement));
        TextDiffAssert.Contains("while (offset > 0)", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_DoCondition_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.DoConditionSource;

        var result = application.Analyze(source, "do-host-sample.cs", CreateOptions("s"));

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.DoStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.DoStatement));
        TextDiffAssert.DoesNotContain("while (s.IsReady)", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_DoBody_DoesNotDeleteLoopWhenConditionIsUncovered()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.DoBodySource;

        var result = application.Analyze(source, "do-body-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_DoBody_DoesNotDeleteLoopWhenConditionIsUncovered), result.Diff);

        Assert.Single(result.SeedMarks);
        Assert.DoesNotContain(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.DoStatement));
        Assert.DoesNotContain(result.Decisions, decision => IsNodeKind(decision.FinalNode, SyntaxKind.DoStatement));
        TextDiffAssert.Contains("while (offset > 0)", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_SwitchCondition_DoesNotDeleteSwitchWithoutSectionCoverage()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.SwitchConditionSource;

        var result = application.Analyze(source, "switch-condition-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchCondition_DoesNotDeleteSwitchWithoutSectionCoverage), result.Diff);

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.SwitchStatement);
        Assert.DoesNotContain(result.Decisions, decision => IsNodeKind(decision.FinalNode, SyntaxKind.SwitchStatement));
    }

    [Fact]
    public void Analyze_SwitchCaseSingleStatement_DeletesSwitchSection()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.SwitchCaseSingleStatementSource;

        var result = application.Analyze(source, "switch-case-single-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchCaseSingleStatement_DeletesSwitchSection), result.Diff);

        Assert.DoesNotContain(result.LiftedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.SwitchSection));
    }

    [Fact]
    public void Analyze_SwitchCaseBlockStatement_DeletesSwitchSection()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.SwitchCaseBlockStatementSource;

        var result = application.Analyze(source, "switch-case-block-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchCaseBlockStatement_DeletesSwitchSection), result.Diff);

        Assert.DoesNotContain(result.LiftedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.SwitchSection));
    }

    [Fact]
    public void Analyze_SwitchCaseWithoutBreak_DoesNotDeleteSwitchSectionWhenNotFullyMarked()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.SwitchCaseWithoutBreakSource;

        var result = application.Analyze(source, "switch-case-without-break.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchCaseWithoutBreak_DoesNotDeleteSwitchSectionWhenNotFullyMarked), result.Diff);

        Assert.DoesNotContain(result.LiftedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.SwitchSection));
    }

    [Fact]
    public void Analyze_SwitchCaseWithoutBreakFullyMarked_DeletesSwitchSection()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.SwitchCaseWithoutBreakFullyMarkedSource;

        var result = application.Analyze(source, "switch-case-without-break-fully-marked.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchCaseWithoutBreakFullyMarked_DeletesSwitchSection), result.Diff);

        Assert.DoesNotContain(result.LiftedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.SwitchSection));
    }

    [Fact]
    public void Analyze_SwitchAllNonDefaultCasesMarked_DeletesWholeSwitch()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.SwitchAllNonDefaultCasesMarkedSource;

        var result = application.Analyze(source, "switch-all-non-default-cases.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchAllNonDefaultCasesMarked_DeletesWholeSwitch), result.Diff);

        Assert.DoesNotContain(result.LiftedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.SwitchStatement));
    }

    [Fact]
    public void Analyze_ForCondition_DoesNotDeleteLoopWhenIncrementorIsUncovered()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.ForConditionSource;

        var result = application.Analyze(source, "for-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ForCondition_DoesNotDeleteLoopWhenIncrementorIsUncovered), result.Diff);

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.ForStatement);
        Assert.DoesNotContain(result.Decisions, decision => IsNodeKind(decision.FinalNode, SyntaxKind.ForStatement));
        TextDiffAssert.Contains("for (; s.IsReady;", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_ForInitializerDeclaration_DoesNotDeleteLoopWhenConditionIsUncovered()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.ForInitializerDeclarationSource;

        var result = application.Analyze(source, "for-initializer-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ForInitializerDeclaration_DoesNotDeleteLoopWhenConditionIsUncovered), result.Diff);

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.ForStatement);
        Assert.DoesNotContain(result.Decisions, decision => IsNodeKind(decision.FinalNode, SyntaxKind.ForStatement));
        TextDiffAssert.Contains("for (var value = s.Seed;", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return 0;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_ForIncrementor_DoesNotDeleteLoopWhenInitializerAndConditionAreUncovered()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.ForIncrementorSource;

        var result = application.Analyze(source, "for-incrementor-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ForIncrementor_DoesNotDeleteLoopWhenInitializerAndConditionAreUncovered), result.Diff);

        Assert.Single(result.SeedMarks);
        Assert.DoesNotContain(result.PropagatedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.ForStatement));
        Assert.DoesNotContain(result.Decisions, decision => IsNodeKind(decision.FinalNode, SyntaxKind.ForStatement));
        TextDiffAssert.Contains("value += s.Seed", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return 0;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_ReturnExpression_DeletesReturnStatementHost()
    {
        var application = CreateApplication();
        var source = AtomicExpressionSources.ReturnExpressionSource;

        var result = application.Analyze(source, "return-host-sample.cs", CreateOptions("s"));

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.ReturnStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.ReturnStatement));
        TextDiffAssert.DoesNotContain("return s.Seed;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_LogicalOrCondition_RewritesToRemainingOperand()
    {
        var application = CreateApplication();
        var source = AtomicLogicalSources.LogicalOrConditionSource;

        var result = application.Analyze(source, "logical-or-sample.cs", CreateOptions("s"));

        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.LogicalOrExpression));
        TextDiffAssert.Contains("ready", result.Diff, result.Diff);
        TextDiffAssert.Contains("s.IsReady", result.Diff, result.Diff);
        TextDiffAssert.Contains("if (ready)", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("s.IsReady", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_IfWithElse_RewritesToElseBody()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.IfElseOnlySource;

        var result = application.Analyze(source, "if-else-only.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_IfWithElse_RewritesToElseBody), result.Diff);

        AssertContainsPropagatedKind(result, SyntaxKind.IfStatement);
        AssertContainsPropagatedKind(result, SyntaxKind.ElseClause);
        Assert.Contains(EnumerateEffectiveNodes(result), node => IsNodeKind(node, SyntaxKind.Block) && node.ToString().Contains("return value + 2;", StringComparison.Ordinal));
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        TextDiffAssert.Contains("--- original #1 if-else-only.cs:", result.Diff, result.Diff);
        TextDiffAssert.Contains("if (s.IsReady)", result.Diff, result.Diff);
        TextDiffAssert.Contains("else", result.Diff, result.Diff);
        AssertStandaloneStatementDiff(result.Diff);
        TextDiffAssert.Contains("<deleted>", result.Diff, result.Diff);
        TextDiffAssert.DoesNotContain("if (s.IsReady)", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("else", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("return value + 2;", result.RewrittenSource, result.Diff);
        Assert.Contains("public int Compute(Box s, int value)", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("{\r\n    }\r\n}", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_IfWithElseIfElse_RewritesToRemainingElseIfChain()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.IfElseIfElseSource;

        var result = application.Analyze(source, "if-elseif-else.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_IfWithElseIfElse_RewritesToRemainingElseIfChain), result.Diff);

        AssertContainsPropagatedKind(result, SyntaxKind.IfStatement);
        Assert.Contains(EnumerateEffectiveNodes(result), node => IsNodeKind(node, SyntaxKind.IfStatement) && node.Parent is ElseClauseSyntax);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        TextDiffAssert.DoesNotContain("if (s.IsReady)", result.RewrittenSource, result.Diff);
        Assert.StartsWith("namespace Demo;", result.RewrittenSource, StringComparison.Ordinal);
        TextDiffAssert.Contains("if (fallback)", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return 3;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_ElseIfWithElse_RewritesElseIfToElse()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.ElseIfElseSource;

        var exception = Record.Exception(() => application.Analyze(source, "elseif-else.cs", CreateOptions("s")));
        Assert.Null(exception);
        var result = application.Analyze(source, "elseif-else.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ElseIfWithElse_RewritesElseIfToElse), result.Diff);

        Assert.Contains(EnumerateEffectiveNodes(result), node => IsNodeKind(node, SyntaxKind.IfStatement) && node.Parent is ElseClauseSyntax);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.ElseClause));
        Assert.DoesNotContain(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement) && decision.FinalNode.Parent is ElseClauseSyntax);
        TextDiffAssert.Contains("--- original #1 elseif-else.cs:", result.Diff, result.Diff);
        TextDiffAssert.Contains("if (ready)", result.Diff, result.Diff);
        TextDiffAssert.Contains("else if (s.IsReady)", result.Diff, result.Diff);
        AssertStandaloneStatementDiff(result.Diff);
        TextDiffAssert.Contains("if (ready)", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("else if (s.IsReady)", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("else", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return 3;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_ElseIfWithoutTail_DeletesOwningElseClause()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.ElseIfWithoutTailSource;

        var result = application.Analyze(source, "elseif-no-tail.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ElseIfWithoutTail_DeletesOwningElseClause), result.Diff);

        Assert.Contains(EnumerateEffectiveNodes(result), node => IsNodeKind(node, SyntaxKind.IfStatement) && node.Parent is ElseClauseSyntax);
        AssertContainsPropagatedKind(result, SyntaxKind.ElseClause);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.ElseClause));
        TextDiffAssert.Contains("if (ready)", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("else if (s.IsReady)", result.RewrittenSource, result.Diff);
        TextDiffAssert.DoesNotContain("return 2;", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("return 3;", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void Analyze_UnreachableMethodsWithoutEntryPoint_ProducesNoMarks()
    {
        var application = CreateApplication(enableUnreachableMethodDeletion: true);
        var source = ReachabilitySources.NoEntryPointSource;

        var result = application.Analyze(source, "no-entry-point.cs", CreateOptions(unreachableMethods: "Dead"));

        Assert.Empty(result.SeedMarks);
        Assert.Empty(result.PropagatedMarks);
        Assert.Empty(result.Decisions);
        Assert.Empty(result.Edits);
        TextDiffAssert.Contains("MainEntry", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("Dead();", result.RewrittenSource, result.Diff);
        TextDiffAssert.Contains("public static void Dead()", result.RewrittenSource, result.Diff);
    }

    [Fact]
    public void AnalyzeFromArgs_ExplicitDiffOutPath_SuppressesUnsafeLocalDeclarationRewrite()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"roslyn-prototype-explicit-diff-{Guid.NewGuid():N}.cs");
        var rawDiffPath = Path.Combine(Path.GetTempPath(), $"roslyn-prototype-explicit-diff-{Guid.NewGuid():N}.txt");
        var aggregateDiffPath = BuildDiffArtifactWriter.GetDiffFilePath(
            "GraphAnalyzerTests.cs",
            "Cli");
        BuildDiffArtifactWriter.InitializeDiffFile(aggregateDiffPath);
        File.WriteAllText(filePath, CliInputSources.ExplicitDiffOutSource);

        try
        {
            var application = CreateApplication();
            var result = CreateCommandHost().AnalyzeFromArgs(new[]
            {
                filePath,
                "--target-name",
                "s",
                "--diff-out",
                rawDiffPath
            });

            Assert.Empty(result.Edits);
            Assert.Null(result.DiffFilePath);
            Assert.False(File.Exists(rawDiffPath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            if (File.Exists(rawDiffPath))
            {
                File.Delete(rawDiffPath);
            }
        }
    }

    [Fact]
    public void Analyze_UnrelatedConflictDomains_KeepMultipleFinalDecisions()
    {
        var application = CreateApplication();
        var source = AtomicControlFlowSources.MultipleDomainsSource;

        var result = application.Analyze(source, "multiple-domains.cs", CreateOptions("s"));

        Assert.Equal(2, result.SeedMarks.Count);
        AssertContainsPropagatedKind(result, SyntaxKind.IfStatement);
        AssertContainsPropagatedKind(result, SyntaxKind.WhileStatement);
        Assert.Equal(2, result.Decisions.Count);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.WhileStatement));
    }

    [Fact]
    public void Analyze_WhenRuleEmitsUnsupportedNodeKind_ThrowsInvalidOperationException()
    {
        var application = new  ApplicationService(
          new RuleDefinitionMark[] { new InvalidNodeKindRule() },
          Array.Empty<RuleDefinitionPropagate>(),
          Array.Empty<RuleDefinitionLift>(),
          Array.Empty<RuleDefinitionPropose>());
        var source = "class C { void M() { if (true) { } } }";

        var exception = Assert.Throws<InvalidOperationException>(() => application.Analyze(source, "invalid-node-kind.cs", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

        Assert.Contains("TEST-INVALID-001", exception.Message);
        Assert.Contains("IfStatement", exception.Message);
        Assert.Contains("MethodDeclaration", exception.Message);
    }

    private static ApplicationService CreateApplication(bool enableUnreachableMethodDeletion = false)
    {
        return new ApplicationService(
          RuleRegistry.CreateDefaultRules(
            enableUnreachableMethodDeletion: enableUnreachableMethodDeletion));
    }

    private static  CommandHost CreateCommandHost()
    {
        return new  CommandHost(RuleRegistry.CreateDefaultRules());
    }

    private static Dictionary<string, string> CreateOptions(string? targetName = null, string? unreachableMethods = null)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(targetName))
        {
            options["target-name"] = targetName;
        }

        if (!string.IsNullOrWhiteSpace(unreachableMethods))
        {
            options["unreachable-methods"] = unreachableMethods;
        }

        return options;
    }

    private static bool IsNodeKind(SyntaxNode node, SyntaxKind kind)
    {
        return node.RawKind == (int)kind;
    }

    private static void AssertContainsPropagatedKind(PrototypeAnalysisResult result, SyntaxKind kind)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node => IsNodeKind(node, kind));
    }

    private static IEnumerable<SyntaxNode> EnumerateEffectiveNodes(PrototypeAnalysisResult result)
    {
        return result.SeedMarks
          .Select(mark => mark.SyntaxNode)
          .Concat(result.PropagatedMarks.Select(mark => mark.Mark.SyntaxNode))
          .Concat(result.LiftedMarks.Select(mark => mark.Mark.SyntaxNode));
    }

    private void AppendUnitTestDiff(string unitTestName, string diffText)
    {
        BuildDiffArtifactWriter.AppendDiffFragment(
          _graphAnalyzerDiffFilePath,
          unitTestName,
          diffText);
    }

    private static SyntaxKind GetNodeKind(SyntaxNode node)
    {
        return (SyntaxKind)node.RawKind;
    }

    private static void AssertStandaloneStatementDiff(string diffText)
    {
        var originalText = ExtractDiffSection(diffText, "--- original #1", "+++ rewritten #1");
        var rewrittenText = ExtractDiffSection(diffText, "+++ rewritten #1", null);
        AssertStandaloneStatement(originalText);
        if (!string.Equals(rewrittenText, "<deleted>", StringComparison.Ordinal))
        {
            AssertStandaloneStatement(rewrittenText);
        }
    }

    private static string ExtractDiffSection(string diffText, string startMarker, string? endMarker)
    {
        var startIndex = diffText.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Diff text must contain marker '{startMarker}'.");
        var contentStart = diffText.IndexOf('\n', startIndex);
        Assert.True(contentStart >= 0, $"Diff marker '{startMarker}' must end with a newline.");
        contentStart++;

        var contentEnd = string.IsNullOrEmpty(endMarker)
          ? diffText.Length
          : diffText.IndexOf(endMarker, contentStart, StringComparison.Ordinal);
        if (contentEnd < 0)
        {
            contentEnd = diffText.Length;
        }

        return diffText[contentStart..contentEnd].Trim();
    }

    private static void AssertStandaloneStatement(string statementText)
    {
        var statement = SyntaxFactory.ParseStatement(statementText);
        Assert.NotNull(statement);
        Assert.True(
          !statement.ContainsDiagnostics,
          $"Diff fragment must be a standalone valid statement.{Environment.NewLine}{statementText}");
    }

    private sealed class InvalidNodeKindRule : RuleDefinitionMark
    {
        public override string RuleId { get; } = "TEST-INVALID-001";

        public override string Name { get; } = "Emit unsupported node kind";

        public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
          new[] { SyntaxKind.MethodDeclaration };

        public override IEnumerable<MarkRecord> Mark(IMarkRuleContext context, SyntaxNode root)
        {
            var node = root.DescendantNodes().OfType<IfStatementSyntax>().Single();
            yield return new MarkRecord(
              RuleId,
              node,
              null,
              null,
              "Emit invalid node kind for validation test.");
        }

    }
}

