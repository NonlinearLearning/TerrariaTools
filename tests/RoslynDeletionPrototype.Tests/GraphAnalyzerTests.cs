using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Application;
using RoslynPrototype.Decision;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using RoslynPrototype.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.Cli;
using RoslynPrototype.Tests.TestCodeSet.Reachability;
using RoslynPrototype.Tests.TestCodeSet.SObject;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class GraphAnalyzerTests
{
    private readonly string _graphAnalyzerDiffFilePath;

    public GraphAnalyzerTests()
    {
        _graphAnalyzerDiffFilePath = BuildDiffArtifactWriter.GetDiffFilePath(
          "GraphAnalyzerTests.cs",
          "SObject");
        BuildDiffArtifactWriter.InitializeDiffFile(_graphAnalyzerDiffFilePath);
    }

    [Fact]
    public void Analyze_TargetNameSample_DeletesLiftedSeedMarks()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.TargetNameSource;

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
        TextDiffAssert.Contains("--- original #1 delete-s-object-sample.cs:", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("var value = s.Seed + offset;", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("+++ rewritten #1", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("<deleted>", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("var value =", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("if (s.IsReady)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_DefinitionAssignment_DeletesLocalDeclarationStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.DefinitionAssignmentSource;

        var result = application.Analyze(source, "definition-assignment.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_DefinitionAssignment_DeletesLocalDeclarationStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.LocalDeclarationStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("var value = s.Seed + offset;", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("var value = s.Seed + offset;", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_AssignmentStatement_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.AssignmentStatementSource;

        var result = application.Analyze(source, "assignment-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_AssignmentStatement_DeletesExpressionStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.ExpressionStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("offset += s.Seed;", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("offset += s.Seed;", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_ComplexDefinitionAssignment_DeletesLocalDeclarationStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.ComplexDefinitionAssignmentSource;

        var result = application.Analyze(source, "complex-definition-assignment.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ComplexDefinitionAssignment_DeletesLocalDeclarationStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.LocalDeclarationStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("var value = (s.Seed + offset) * values[offset];", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("var value = (s.Seed + offset) * values[offset];", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_ChainedAssignmentStatement_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.ChainedAssignmentStatementSource;

        var result = application.Analyze(source, "chained-assignment-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ChainedAssignmentStatement_DeletesExpressionStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.ExpressionStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("left = right = s.Seed;", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("left = right = s.Seed;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_DeconstructionAssignmentStatement_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.DeconstructionAssignmentStatementSource;

        var result = application.Analyze(source, "deconstruction-assignment-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_DeconstructionAssignmentStatement_DeletesExpressionStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.ExpressionStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("(left, right) = (s.Seed, offset);", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("(left, right) = (s.Seed, offset);", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_ObjectInitializerDefinitionAssignment_DeletesLocalDeclarationStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.ObjectInitializerDefinitionAssignmentSource;

        var result = application.Analyze(source, "object-initializer-definition-assignment.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ObjectInitializerDefinitionAssignment_DeletesLocalDeclarationStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.ObjectCreationExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.LocalDeclarationStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("var holder = new Holder", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("Value = s.Seed", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("var holder = new Holder", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_ComplexCompoundAssignmentStatement_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.ComplexCompoundAssignmentStatementSource;

        var result = application.Analyze(source, "complex-compound-assignment-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ComplexCompoundAssignmentStatement_DeletesExpressionStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.ExpressionStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("offset += s.Seed + offset * 2;", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("offset += s.Seed + offset * 2;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_AssignmentLeftOperand_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.AssignmentLeftOperandSource;

        var result = application.Analyze(source, "assignment-left-operand.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_AssignmentLeftOperand_DeletesExpressionStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.ElementAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.ExpressionStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("values[s.Seed] = offset;", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("values[s.Seed] = offset;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_DefinitionLeftOperand_DeletesLocalDeclarationStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.DefinitionLeftOperandSource;

        var result = application.Analyze(source, "definition-left-operand.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_DefinitionLeftOperand_DeletesLocalDeclarationStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.VariableDeclarator, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.LocalDeclarationStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("var s = offset + 1;", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("var s = offset + 1;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_CallArgumentStatement_DeletesExpressionStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.CallArgumentStatementSource;

        var result = application.Analyze(source, "call-argument-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_CallArgumentStatement_DeletesExpressionStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.InvocationExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.ExpressionStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("Fun(s.Seed, 3);", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("Fun(s.Seed, 3);", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_PropertyAccessDefinition_WhenPropertyNameMatches_DeletesLocalDeclarationStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.PropertyAccessDefinitionSource;

        var result = application.Analyze(source, "property-access-definition.cs", CreateOptions("Seed"));
        AppendUnitTestDiff(nameof(Analyze_PropertyAccessDefinition_WhenPropertyNameMatches_DeletesLocalDeclarationStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.LocalDeclarationStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("var value = holder.Seed;", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("var value = holder.Seed;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_IndexAccessDefinition_WhenBaseOrIndexMatches_DeletesLocalDeclarationStatement()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.IndexAccessDefinitionSource;

        var result = application.Analyze(source, "index-access-definition.cs", CreateOptions("values"));
        AppendUnitTestDiff(nameof(Analyze_IndexAccessDefinition_WhenBaseOrIndexMatches_DeletesLocalDeclarationStatement), result.DiffText);

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.ElementAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        var decision = Assert.Single(result.Decisions);
        Assert.Equal(DecisionActionKind.Delete, decision.Action);
        Assert.Equal(SyntaxKind.LocalDeclarationStatement, GetNodeKind(decision.FinalNode));
        TextDiffAssert.Contains("var value = values[s.Seed];", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("var value = values[s.Seed];", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_UnreachableMethodsSample_DeletesConfiguredMethods()
    {
        var application = CreateApplication();
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
        TextDiffAssert.Contains("Main", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("Live", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("DeadA", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("DeadB", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_LogicalAndCondition_RewritesToRemainingOperand()
    {
        var application = CreateApplication();
        var source = SObjectLogicalSources.LogicalAndConditionSource;

        var result = application.Analyze(source, "logical-and-sample.cs", CreateOptions("s"));

        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.LogicalAndExpression));
        TextDiffAssert.Contains("ready", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("s.IsReady", result.DiffText, result.DiffText);
        Assert.DoesNotContain(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        TextDiffAssert.Contains("if (ready)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("s.IsReady", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_WhileCondition_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.WhileConditionSource;

        var result = application.Analyze(source, "while-host-sample.cs", CreateOptions("s"));

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.WhileStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.WhileStatement));
        TextDiffAssert.DoesNotContain("while (s.IsReady)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_WhileBody_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.WhileBodySource;

        var result = application.Analyze(source, "while-body-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_WhileBody_DeletesLoopHost), result.DiffText);

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.WhileStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.WhileStatement));
        TextDiffAssert.DoesNotContain("while (offset > 0)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_DoCondition_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.DoConditionSource;

        var result = application.Analyze(source, "do-host-sample.cs", CreateOptions("s"));

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.DoStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.DoStatement));
        TextDiffAssert.DoesNotContain("while (s.IsReady)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_DoBody_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.DoBodySource;

        var result = application.Analyze(source, "do-body-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_DoBody_DeletesLoopHost), result.DiffText);

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.DoStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.DoStatement));
        TextDiffAssert.DoesNotContain("while (offset > 0)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_SwitchCondition_DeletesSwitchHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.SwitchConditionSource;

        var result = application.Analyze(source, "switch-condition-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchCondition_DeletesSwitchHost), result.DiffText);

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.SwitchStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.SwitchStatement));
    }

    [Fact]
    public void Analyze_SwitchCaseSingleStatement_DeletesSwitchSection()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.SwitchCaseSingleStatementSource;

        var result = application.Analyze(source, "switch-case-single-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchCaseSingleStatement_DeletesSwitchSection), result.DiffText);

        AssertContainsPropagatedKind(result, SyntaxKind.SwitchSection);
    }

    [Fact]
    public void Analyze_SwitchCaseBlockStatement_DeletesSwitchSection()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.SwitchCaseBlockStatementSource;

        var result = application.Analyze(source, "switch-case-block-statement.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchCaseBlockStatement_DeletesSwitchSection), result.DiffText);

        AssertContainsPropagatedKind(result, SyntaxKind.SwitchSection);
    }

    [Fact]
    public void Analyze_SwitchCaseWithoutBreak_DoesNotDeleteSwitchSectionWhenNotFullyMarked()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.SwitchCaseWithoutBreakSource;

        var result = application.Analyze(source, "switch-case-without-break.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchCaseWithoutBreak_DoesNotDeleteSwitchSectionWhenNotFullyMarked), result.DiffText);

        Assert.DoesNotContain(result.LiftedMarks, mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.SwitchSection));
    }

    [Fact]
    public void Analyze_SwitchCaseWithoutBreakFullyMarked_DeletesSwitchSection()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.SwitchCaseWithoutBreakFullyMarkedSource;

        var result = application.Analyze(source, "switch-case-without-break-fully-marked.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchCaseWithoutBreakFullyMarked_DeletesSwitchSection), result.DiffText);

        AssertContainsPropagatedKind(result, SyntaxKind.SwitchSection);
    }

    [Fact]
    public void Analyze_SwitchAllNonDefaultCasesMarked_DeletesWholeSwitch()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.SwitchAllNonDefaultCasesMarkedSource;

        var result = application.Analyze(source, "switch-all-non-default-cases.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_SwitchAllNonDefaultCasesMarked_DeletesWholeSwitch), result.DiffText);

        AssertContainsPropagatedKind(result, SyntaxKind.SwitchStatement);
    }

    [Fact]
    public void Analyze_ForCondition_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.ForConditionSource;

        var result = application.Analyze(source, "for-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ForCondition_DeletesLoopHost), result.DiffText);

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.ForStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.ForStatement));
        TextDiffAssert.DoesNotContain("for (; s.IsReady;", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return offset;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_ForInitializerDeclaration_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.ForInitializerDeclarationSource;

        var result = application.Analyze(source, "for-initializer-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ForInitializerDeclaration_DeletesLoopHost), result.DiffText);

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.ForStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.ForStatement));
        TextDiffAssert.DoesNotContain("for (var value = s.Seed;", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return 0;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_ForIncrementor_DeletesLoopHost()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.ForIncrementorSource;

        var result = application.Analyze(source, "for-incrementor-host-sample.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ForIncrementor_DeletesLoopHost), result.DiffText);

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.ForStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.ForStatement));
        TextDiffAssert.DoesNotContain("value += s.Seed", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return 0;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_ReturnExpression_DeletesReturnStatementHost()
    {
        var application = CreateApplication();
        var source = SObjectExpressionSources.ReturnExpressionSource;

        var result = application.Analyze(source, "return-host-sample.cs", CreateOptions("s"));

        Assert.Single(result.SeedMarks);
        AssertContainsPropagatedKind(result, SyntaxKind.ReturnStatement);
        Assert.Single(result.Decisions);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.ReturnStatement));
        TextDiffAssert.DoesNotContain("return s.Seed;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_LogicalOrCondition_RewritesToRemainingOperand()
    {
        var application = CreateApplication();
        var source = SObjectLogicalSources.LogicalOrConditionSource;

        var result = application.Analyze(source, "logical-or-sample.cs", CreateOptions("s"));

        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.LogicalOrExpression));
        TextDiffAssert.Contains("ready", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("s.IsReady", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("if (ready)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("s.IsReady", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_IfWithElse_RewritesToElseBody()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.IfElseOnlySource;

        var result = application.Analyze(source, "if-else-only.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_IfWithElse_RewritesToElseBody), result.DiffText);

        AssertContainsPropagatedKind(result, SyntaxKind.IfStatement);
        AssertContainsPropagatedKind(result, SyntaxKind.ElseClause);
        Assert.Contains(EnumerateEffectiveNodes(result), node => IsNodeKind(node, SyntaxKind.Block) && node.ToString().Contains("return value + 2;", StringComparison.Ordinal));
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        TextDiffAssert.Contains("--- original #1 if-else-only.cs:", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("if (s.IsReady)", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("else", result.DiffText, result.DiffText);
        AssertStandaloneStatementDiff(result.DiffText);
        TextDiffAssert.Contains("<deleted>", result.DiffText, result.DiffText);
        TextDiffAssert.DoesNotContain("if (s.IsReady)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("else", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("return value + 2;", result.RewrittenSource, result.DiffText);
        Assert.Contains("public int Compute(Box s, int value)", result.RewrittenSource, StringComparison.Ordinal);
        Assert.Contains("{\r\n    }\r\n}", result.RewrittenSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_IfWithElseIfElse_RewritesToRemainingElseIfChain()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.IfElseIfElseSource;

        var result = application.Analyze(source, "if-elseif-else.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_IfWithElseIfElse_RewritesToRemainingElseIfChain), result.DiffText);

        AssertContainsPropagatedKind(result, SyntaxKind.IfStatement);
        Assert.Contains(EnumerateEffectiveNodes(result), node => IsNodeKind(node, SyntaxKind.IfStatement) && node.Parent is ElseClauseSyntax);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement));
        TextDiffAssert.DoesNotContain("if (s.IsReady)", result.RewrittenSource, result.DiffText);
        Assert.StartsWith("namespace Demo;", result.RewrittenSource, StringComparison.Ordinal);
        TextDiffAssert.Contains("if (fallback)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return 3;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_ElseIfWithElse_RewritesElseIfToElse()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.ElseIfElseSource;

        var exception = Record.Exception(() => application.Analyze(source, "elseif-else.cs", CreateOptions("s")));
        Assert.Null(exception);
        var result = application.Analyze(source, "elseif-else.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ElseIfWithElse_RewritesElseIfToElse), result.DiffText);

        Assert.Contains(EnumerateEffectiveNodes(result), node => IsNodeKind(node, SyntaxKind.IfStatement) && node.Parent is ElseClauseSyntax);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.ElseClause));
        Assert.DoesNotContain(result.Decisions, decision => decision.Action == DecisionActionKind.Replace && IsNodeKind(decision.FinalNode, SyntaxKind.IfStatement) && decision.FinalNode.Parent is ElseClauseSyntax);
        TextDiffAssert.Contains("--- original #1 elseif-else.cs:", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("if (ready)", result.DiffText, result.DiffText);
        TextDiffAssert.Contains("else if (s.IsReady)", result.DiffText, result.DiffText);
        AssertStandaloneStatementDiff(result.DiffText);
        TextDiffAssert.Contains("if (ready)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("else if (s.IsReady)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("else", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return 3;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_ElseIfWithoutTail_DeletesOwningElseClause()
    {
        var application = CreateApplication();
        var source = SObjectControlFlowSources.ElseIfWithoutTailSource;

        var result = application.Analyze(source, "elseif-no-tail.cs", CreateOptions("s"));
        AppendUnitTestDiff(nameof(Analyze_ElseIfWithoutTail_DeletesOwningElseClause), result.DiffText);

        Assert.Contains(EnumerateEffectiveNodes(result), node => IsNodeKind(node, SyntaxKind.IfStatement) && node.Parent is ElseClauseSyntax);
        AssertContainsPropagatedKind(result, SyntaxKind.ElseClause);
        Assert.Contains(result.Decisions, decision => decision.Action == DecisionActionKind.Delete && IsNodeKind(decision.FinalNode, SyntaxKind.ElseClause));
        TextDiffAssert.Contains("if (ready)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("else if (s.IsReady)", result.RewrittenSource, result.DiffText);
        TextDiffAssert.DoesNotContain("return 2;", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("return 3;", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void Analyze_UnreachableMethodsWithoutEntryPoint_ProducesNoMarks()
    {
        var application = CreateApplication();
        var source = ReachabilitySources.NoEntryPointSource;

        var result = application.Analyze(source, "no-entry-point.cs", CreateOptions(unreachableMethods: "Dead"));

        Assert.Empty(result.SeedMarks);
        Assert.Empty(result.PropagatedMarks);
        Assert.Empty(result.Decisions);
        Assert.Empty(result.Edits);
        TextDiffAssert.Contains("MainEntry", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("Dead();", result.RewrittenSource, result.DiffText);
        TextDiffAssert.Contains("public static void Dead()", result.RewrittenSource, result.DiffText);
    }

    [Fact]
    public void AnalyzeFromArgs_HonorsExplicitDiffOutPath()
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
            var result = application.AnalyzeFromArgs(new[]
            {
                filePath,
                "--target-name",
                "s",
                "--diff-out",
                rawDiffPath
            });

            Assert.NotNull(result.DiffFilePath);
            Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
            Assert.True(File.Exists(rawDiffPath));
            BuildDiffArtifactWriter.AppendDiffFragment(
                aggregateDiffPath,
                nameof(AnalyzeFromArgs_HonorsExplicitDiffOutPath),
                File.ReadAllText(rawDiffPath));
            var aggregateDiffText = File.ReadAllText(aggregateDiffPath);
            TextDiffAssert.Contains(
              "UnitTest: AnalyzeFromArgs_HonorsExplicitDiffOutPath",
              aggregateDiffText,
              aggregateDiffText);
            TextDiffAssert.Contains("+++ rewritten #1", aggregateDiffText, aggregateDiffText);
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
        var source = SObjectControlFlowSources.MultipleDomainsSource;

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
        var application = new DeletionApplicationService(
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

    private static DeletionApplicationService CreateApplication()
    {
        return new DeletionApplicationService(RuleRegistry.CreateDefaultRules());
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

    private static void AssertContainsPropagatedKind(
      PrototypeAnalysisResult result,
      SyntaxKind kind)
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

    private static string ExtractDiffSection(
      string diffText,
      string startMarker,
      string? endMarker)
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

        public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
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
