using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using NLISSN.Application;
using NLISSN.Core.Lifting;
using NLISSN.Core.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.Target;
using NLISSN.Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class MarkRuleEffectTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _targetDiffFilePath;

    public MarkRuleEffectTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"roslyn-mark-rule-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _targetDiffFilePath = BuildDiffArtifactWriter.GetDiffFilePath(
            "MarkRuleEffectTests.cs",
            "Target");
        BuildDiffArtifactWriter.InitializeDiffFile(_targetDiffFilePath);
    }

    [Fact]
    public void AnalyzeFromArgs_TargetNameSource_ProducesExpectedMarksAndDiffFile()
    {
        var filePath = WriteSourceFile(
            "target-name-source.cs",
            AtomicExpressionSources.AtomicNameSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "target-name-source.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        Assert.Equal(2, result.SeedMarks.Count);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        AssertContainsPropagatedKind(result, SyntaxKind.IfStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_TargetNameSource_ProducesExpectedMarksAndDiffFile),
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_targetDiffFilePath);
        Assert.Contains(
            "UnitTest: AnalyzeFromArgs_TargetNameSource_ProducesExpectedMarksAndDiffFile",
            diffText,
            StringComparison.Ordinal);
        TextDiffAssert.Contains("var value = s.Seed + offset;", diffText, diffText);
        TextDiffAssert.Contains("if (s.IsReady)", diffText, diffText);
        TextDiffAssert.Contains("<deleted>", diffText, diffText);
    }

    [Fact]
    public void AnalyzeFromArgs_DefinitionAssignmentSource_MarksLocalDeclarationStatement()
    {
        var filePath = WriteSourceFile(
            "definition-assignment.cs",
            AtomicExpressionSources.DefinitionAssignmentSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "definition-assignment.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_DefinitionAssignmentSource_MarksLocalDeclarationStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_AssignmentStatementSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "assignment-statement.cs",
            AtomicExpressionSources.AssignmentStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "assignment-statement.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.DoesNotContain(result.PropagatedMarks, mark => mark.Mark.SyntaxNode.IsKind(SyntaxKind.ExpressionStatement));
        Assert.Empty(result.Decisions);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_ComplexDefinitionAssignmentSource_PreservesLocalDeclarationWithoutDiff()
    {
        var filePath = WriteSourceFile(
            "complex-definition-assignment.cs",
            AtomicExpressionSources.ComplexDefinitionAssignmentSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "complex-definition-assignment.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode.IsKind(SyntaxKind.LocalDeclarationStatement));
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_ChainedAssignmentStatementSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "chained-assignment-statement.cs",
            AtomicExpressionSources.ChainedAssignmentStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "chained-assignment-statement.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.DoesNotContain(result.PropagatedMarks, mark => mark.Mark.SyntaxNode.IsKind(SyntaxKind.ExpressionStatement));
        Assert.Empty(result.Decisions);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_DeconstructionAssignmentStatementSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "deconstruction-assignment-statement.cs",
            AtomicExpressionSources.DeconstructionAssignmentStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "deconstruction-assignment-statement.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_DeconstructionAssignmentStatementSource_MarksExpressionStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_ObjectInitializerDefinitionAssignmentSource_PreservesLocalDeclarationWithoutDiff()
    {
        var filePath = WriteSourceFile(
            "object-initializer-definition-assignment.cs",
            AtomicExpressionSources.ObjectInitializerDefinitionAssignmentSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "object-initializer-definition-assignment.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.ObjectCreationExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        Assert.DoesNotContain(result.Decisions, decision =>
          decision.Action == DecisionActionKind.Delete &&
          decision.FinalNode.IsKind(SyntaxKind.LocalDeclarationStatement));
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_ComplexCompoundAssignmentStatementSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "complex-compound-assignment-statement.cs",
            AtomicExpressionSources.ComplexCompoundAssignmentStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "complex-compound-assignment-statement.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.DoesNotContain(result.PropagatedMarks, mark => mark.Mark.SyntaxNode.IsKind(SyntaxKind.ExpressionStatement));
        Assert.Empty(result.Decisions);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_AssignmentLeftOperandSource_StopsWithoutRewrite()
    {
        var filePath = WriteSourceFile(
            "assignment-left-operand.cs",
            AtomicExpressionSources.AssignmentLeftOperandSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "assignment-left-operand.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.ElementAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        Assert.Empty(result.Decisions);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_DefinitionLeftOperandSource_MarksLocalDeclarationStatement()
    {
        var filePath = WriteSourceFile(
            "definition-left-operand.cs",
            AtomicExpressionSources.DefinitionLeftOperandSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "definition-left-operand.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.VariableDeclarator, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_DefinitionLeftOperandSource_MarksLocalDeclarationStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_CallArgumentStatementSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "call-argument-statement.cs",
            AtomicExpressionSources.CallArgumentStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "call-argument-statement.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.InvocationExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_CallArgumentStatementSource_MarksExpressionStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_PropertyAccessDefinitionSource_WhenPropertyNameMatches_MarksLocalDeclarationStatement()
    {
        var filePath = WriteSourceFile(
            "property-access-definition.cs",
            AtomicExpressionSources.PropertyAccessDefinitionSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "property-access-definition.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "Seed",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_PropertyAccessDefinitionSource_WhenPropertyNameMatches_MarksLocalDeclarationStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_IndexAccessDefinitionSource_WhenBaseMatches_MarksLocalDeclarationStatement()
    {
        var filePath = WriteSourceFile(
            "index-access-definition.cs",
            AtomicExpressionSources.IndexAccessDefinitionSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "index-access-definition.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "values",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.ElementAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.LocalDeclarationStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_IndexAccessDefinitionSource_WhenBaseMatches_MarksLocalDeclarationStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_IfElseOnlySource_MarksElseClauseAndElseBodyTogether()
    {
        var filePath = WriteSourceFile(
            "if-else-only.cs",
            AtomicControlFlowSources.IfElseOnlySource);
        var rawDiffPath = Path.Combine(_tempDirectory, "if-else-only.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        AssertContainsPropagatedKind(result, SyntaxKind.IfStatement);
        AssertContainsPropagatedKind(result, SyntaxKind.ElseClause);
        Assert.Contains(EnumerateEffectiveNodes(result), node =>
            IsNodeKind(node, SyntaxKind.Block) &&
            node.ToString().Contains("return value + 2;", StringComparison.Ordinal));
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_IfElseOnlySource_MarksElseClauseAndElseBodyTogether),
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_targetDiffFilePath);
        Assert.Contains(
            "UnitTest: AnalyzeFromArgs_IfElseOnlySource_MarksElseClauseAndElseBodyTogether",
            diffText,
            StringComparison.Ordinal);
        TextDiffAssert.Contains("if (s.IsReady)", diffText, diffText);
        TextDiffAssert.Contains("return value + 2;", diffText, diffText);
    }

    [Fact]
    public void AnalyzeFromArgs_ForInitializerDeclarationSource_DoesNotLiftForStatementWithoutCompleteHeaderCoverage()
    {
        var filePath = WriteSourceFile(
            "for-initializer-host-sample.cs",
            AtomicControlFlowSources.ForInitializerDeclarationSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "for-initializer-host-sample.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertDoesNotContainLiftedStructure(result, SyntaxKind.ForStatement, StructuralKind.Loop);
        Assert.Empty(result.Decisions);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_ForIncrementorSource_DoesNotLiftForStatementWithoutCompleteHeaderCoverage()
    {
        var filePath = WriteSourceFile(
            "for-incrementor-host-sample.cs",
            AtomicControlFlowSources.ForIncrementorSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "for-incrementor-host-sample.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertDoesNotContainLiftedStructure(result, SyntaxKind.ForStatement, StructuralKind.Loop);
        Assert.Empty(result.Decisions);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_WhileBodySource_DoesNotLiftWhileStatementWithoutConditionCoverage()
    {
        var filePath = WriteSourceFile(
            "while-body-host-sample.cs",
            AtomicControlFlowSources.WhileBodySource);
        var rawDiffPath = Path.Combine(_tempDirectory, "while-body-host-sample.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertDoesNotContainLiftedStructure(result, SyntaxKind.WhileStatement, StructuralKind.Loop);
        Assert.Empty(result.Decisions);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_DoBodySource_DoesNotLiftDoStatementWithoutConditionCoverage()
    {
        var filePath = WriteSourceFile(
            "do-body-host-sample.cs",
            AtomicControlFlowSources.DoBodySource);
        var rawDiffPath = Path.Combine(_tempDirectory, "do-body-host-sample.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertDoesNotContainLiftedStructure(result, SyntaxKind.DoStatement, StructuralKind.Loop);
        Assert.Empty(result.Decisions);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_SwitchConditionSource_DoesNotLiftSwitchStatementWithoutSectionCoverage()
    {
        var filePath = WriteSourceFile(
            "switch-condition-host-sample.cs",
            AtomicControlFlowSources.SwitchConditionSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "switch-condition-host-sample.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertDoesNotContainLiftedStructure(result, SyntaxKind.SwitchStatement, StructuralKind.Switch);
        Assert.Empty(result.Decisions);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_SwitchCaseSingleStatementSource_MarksWholeSwitchSection()
    {
        var filePath = WriteSourceFile(
            "switch-case-single-statement.cs",
            AtomicControlFlowSources.SwitchCaseSingleStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "switch-case-single-statement.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        AssertDoesNotContainLiftedStructure(result, SyntaxKind.SwitchSection, StructuralKind.Switch);
        Assert.Empty(result.Decisions);
        Assert.Null(result.DiffFilePath);
        Assert.False(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_SwitchCaseWithoutBreakFullyMarkedSource_MarksWholeSwitchSection()
    {
        var filePath = WriteSourceFile(
            "switch-case-without-break-fully-marked.cs",
            AtomicControlFlowSources.SwitchCaseWithoutBreakFullyMarkedSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "switch-case-without-break-fully-marked.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        AssertDoesNotContainLiftedStructure(result, SyntaxKind.SwitchSection, StructuralKind.Switch);

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_SwitchCaseWithoutBreakFullyMarkedSource_MarksWholeSwitchSection),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_LogicalMixedPrecedenceSource_ProducesLogicalOrMarkAndDiffFile()
    {
        var filePath = WriteSourceFile(
            "logical-mixed-precedence.cs",
            AtomicLogicalSources.LogicalMixedPrecedenceSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "logical-mixed-precedence.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "b",
            "--diff-out",
            rawDiffPath
        });

        AssertSingleLiftedLogicalOr(result, "a && b || c || !b");
        Assert.Single(result.Decisions);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_LogicalMixedPrecedenceSource_ProducesLogicalOrMarkAndDiffFile),
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_targetDiffFilePath);
        Assert.Contains(
            "UnitTest: AnalyzeFromArgs_LogicalMixedPrecedenceSource_ProducesLogicalOrMarkAndDiffFile",
            diffText,
            StringComparison.Ordinal);
        TextDiffAssert.Contains("a && b || c || !b", diffText, diffText);
        TextDiffAssert.Contains("c || !b", diffText, diffText);
        Assert.Equal("c||!b", result.Decisions[0].ReplacementNode?.ToString());
        TextDiffAssert.Contains("if (c || !b)", result.RewrittenSource, diffText);
    }

    [Fact]
    public void AnalyzeFromArgs_LogicalMixedPrecedenceWithParenthesesSource_ProducesLogicalOrMarkAndDiffFile()
    {
        var filePath = WriteSourceFile(
            "logical-mixed-precedence-parenthesized.cs",
            AtomicLogicalSources.LogicalMixedPrecedenceWithParenthesesSource);
        var rawDiffPath = Path.Combine(
            _tempDirectory,
            "logical-mixed-precedence-parenthesized.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "b",
            "--diff-out",
            rawDiffPath
        });

        AssertSingleLiftedLogicalOr(result, "(a && b) || c || !b");
        Assert.Single(result.Decisions);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_LogicalMixedPrecedenceWithParenthesesSource_ProducesLogicalOrMarkAndDiffFile),
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_targetDiffFilePath);
        Assert.Contains(
            "UnitTest: AnalyzeFromArgs_LogicalMixedPrecedenceWithParenthesesSource_ProducesLogicalOrMarkAndDiffFile",
            diffText,
            StringComparison.Ordinal);
        TextDiffAssert.Contains("(a && b) || c || !b", diffText, diffText);
        Assert.Equal("c||!b", result.Decisions[0].ReplacementNode?.ToString());
        TextDiffAssert.Contains("if (c || !b)", result.RewrittenSource, diffText);
    }

    [Fact]
    public void AnalyzeFromArgs_MultiTargetGroupWithFiveHits_ProducesSingleLogicalOrMarkAndDiffFile()
    {
        var filePath = WriteSourceFile(
            "logical-multi-target-group.cs",
            AtomicLogicalSources.LogicalMultiTargetGroupFiveHitsSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "logical-multi-target-group.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "b,c,d,e,f",
            "--diff-out",
            rawDiffPath
        });

        AssertSingleLiftedLogicalOr(result, "a || b || c || d || e || f || g || h");
        Assert.Single(result.Decisions);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            nameof(AnalyzeFromArgs_MultiTargetGroupWithFiveHits_ProducesSingleLogicalOrMarkAndDiffFile),
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_targetDiffFilePath);
        Assert.Contains(
            "UnitTest: AnalyzeFromArgs_MultiTargetGroupWithFiveHits_ProducesSingleLogicalOrMarkAndDiffFile",
            diffText,
            StringComparison.Ordinal);
        TextDiffAssert.Contains("a || b || c || d || e || f || g || h", diffText, diffText);
        Assert.Equal("a||g||h", result.Decisions[0].ReplacementNode?.ToString());
        TextDiffAssert.Contains("a || g || h", diffText, diffText);
    }

    [Theory]
    [MemberData(nameof(LargeParenthesizedLogicalEffectCases))]
    public void AnalyzeFromArgs_LargeParenthesizedCases_ProduceSingleLogicalOrMark(string caseName, string source, string expectedMarkedText)
    {
        var filePath = WriteSourceFile(
            $"{caseName}.cs",
            source);
        var rawDiffPath = Path.Combine(_tempDirectory, $"{caseName}.raw.diff");
        var application = CreateApplication();

        var result = CreateCommandHost().AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "b",
            "--diff-out",
            rawDiffPath
        });

        AssertSingleLiftedLogicalOr(result, expectedMarkedText);
        Assert.Single(result.Decisions);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _targetDiffFilePath,
            $"{nameof(AnalyzeFromArgs_LargeParenthesizedCases_ProduceSingleLogicalOrMark)}:{caseName}",
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_targetDiffFilePath);
        Assert.Contains(
            $"UnitTest: {nameof(AnalyzeFromArgs_LargeParenthesizedCases_ProduceSingleLogicalOrMark)}:{caseName}",
            diffText,
            StringComparison.Ordinal);
        Assert.Null(result.Decisions[0].ReplacementNode);
    }

    public static IEnumerable<object[]> LargeParenthesizedLogicalEffectCases()
    {
        yield return CreateEffectCase(
            nameof(AtomicLogicalSources.LogicalMixedPrecedenceLargeCase1Source),
            AtomicLogicalSources.LogicalMixedPrecedenceLargeCase1Source,
            "(a && b) || (c && d) || !b || e || (f && g) || h || i || j || k || l");
        yield return CreateEffectCase(
            nameof(AtomicLogicalSources.LogicalMixedPrecedenceLargeCase2Source),
            AtomicLogicalSources.LogicalMixedPrecedenceLargeCase2Source,
            "((a || b) && (c || !b)) || d || e || (f && g) || h || i || j || k || l || m");
        yield return CreateEffectCase(
            nameof(AtomicLogicalSources.LogicalMixedPrecedenceLargeCase3Source),
            AtomicLogicalSources.LogicalMixedPrecedenceLargeCase3Source,
            "(a && b) || c || d || (!b && e) || f || g || h || (i && j) || k || l");
        yield return CreateEffectCase(
            nameof(AtomicLogicalSources.LogicalMixedPrecedenceLargeCase4Source),
            AtomicLogicalSources.LogicalMixedPrecedenceLargeCase4Source,
            "((a && b) || c) || d || e || ((f || !b) && g) || h || i || j || k || l");
        yield return CreateEffectCase(
            nameof(AtomicLogicalSources.LogicalMixedPrecedenceLargeCase5Source),
            AtomicLogicalSources.LogicalMixedPrecedenceLargeCase5Source,
            "(a && (b || c)) || d || e || !b || (f && g) || h || i || j || k || l");
    }

    private static  ApplicationService CreateApplication()
    {
        return new  ApplicationService(RuleRegistry.CreateDefaultRules());
    }

    private static  CommandHost CreateCommandHost()
    {
        return new  CommandHost(RuleRegistry.CreateDefaultRules());
    }

    private string WriteSourceFile(string fileName, string source)
    {
        var filePath = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(filePath, source);
        return filePath;
    }

    private static object[] CreateEffectCase(string caseName, string source, string expectedMarkedText)
    {
        return new object[] { caseName, source, expectedMarkedText };
    }

    private static bool IsNodeKind(Microsoft.CodeAnalysis.SyntaxNode node, SyntaxKind kind)
    {
        return node.RawKind == (int)kind;
    }

    private static void AssertContainsPropagatedKind(PrototypeAnalysisResult result, SyntaxKind kind)
    {
        Assert.Contains(EnumerateEffectiveNodes(result), node => IsNodeKind(node, kind));
    }

    private static void AssertContainsLiftedStructure(
        PrototypeAnalysisResult result,
        SyntaxKind syntaxKind,
        StructuralKind structureKind)
    {
        Assert.Contains(result.LiftedMarks, mark =>
            IsNodeKind(mark.Mark.SyntaxNode, syntaxKind) &&
            mark.StructureKind == structureKind);
    }

    private static void AssertDoesNotContainLiftedStructure(
        PrototypeAnalysisResult result,
        SyntaxKind syntaxKind,
        StructuralKind structureKind)
    {
        Assert.DoesNotContain(result.LiftedMarks, mark =>
            IsNodeKind(mark.Mark.SyntaxNode, syntaxKind) &&
            mark.StructureKind == structureKind);
    }

    private static IEnumerable<SyntaxNode> EnumerateEffectiveNodes(PrototypeAnalysisResult result)
    {
        return result.SeedMarks
            .Select(mark => mark.SyntaxNode)
            .Concat(result.PropagatedMarks.Select(mark => mark.Mark.SyntaxNode))
            .Concat(result.LiftedMarks.Select(mark => mark.Mark.SyntaxNode));
    }

    private static LiftedMarkRecord AssertSingleLiftedLogicalOr(PrototypeAnalysisResult result, string expectedText)
    {
        var mark = Assert.Single(result.LiftedMarks, mark =>
            IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.LogicalOrExpression) &&
            string.Equals(mark.Mark.SyntaxNode.ToString(), expectedText, StringComparison.Ordinal) &&
            mark.Mark.FactKind == RuleFactKind.LiftLogicalReduction &&
            mark.Payload is LogicalExpressionReductionPayload);
        var payload = Assert.IsType<LogicalExpressionReductionPayload>(mark.Payload);
        Assert.Same(mark.Mark.SyntaxNode, payload.Host);
        Assert.NotEmpty(payload.RemovableOperands);
        Assert.NotEmpty(payload.SurvivorOperands);
        return mark;
    }

    private static string RemoveWhitespace(string text)
    {
        return new string(text.Where(character => !char.IsWhiteSpace(character)).ToArray());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
