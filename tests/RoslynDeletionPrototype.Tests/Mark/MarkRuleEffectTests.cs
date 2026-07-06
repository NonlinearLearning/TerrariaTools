using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using RoslynPrototype.Application;
using RoslynPrototype.Rewrite;
using RoslynPrototype.Tests.TestCodeSet.SObject;
using Rules;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class MarkRuleEffectTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _sObjectDiffFilePath;

    public MarkRuleEffectTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"roslyn-mark-rule-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _sObjectDiffFilePath = BuildDiffArtifactWriter.GetDiffFilePath(
            "MarkRuleEffectTests.cs",
            "SObject");
        BuildDiffArtifactWriter.InitializeDiffFile(_sObjectDiffFilePath);
    }

    [Fact]
    public void AnalyzeFromArgs_TargetNameSource_ProducesExpectedMarksAndDiffFile()
    {
        var filePath = WriteSourceFile(
            "target-name-source.cs",
            SObjectExpressionSources.TargetNameSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "target-name-source.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_TargetNameSource_ProducesExpectedMarksAndDiffFile),
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_sObjectDiffFilePath);
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
            SObjectExpressionSources.DefinitionAssignmentSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "definition-assignment.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_DefinitionAssignmentSource_MarksLocalDeclarationStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_AssignmentStatementSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "assignment-statement.cs",
            SObjectExpressionSources.AssignmentStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "assignment-statement.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_AssignmentStatementSource_MarksExpressionStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_ComplexDefinitionAssignmentSource_MarksLocalDeclarationStatement()
    {
        var filePath = WriteSourceFile(
            "complex-definition-assignment.cs",
            SObjectExpressionSources.ComplexDefinitionAssignmentSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "complex-definition-assignment.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_ComplexDefinitionAssignmentSource_MarksLocalDeclarationStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_ChainedAssignmentStatementSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "chained-assignment-statement.cs",
            SObjectExpressionSources.ChainedAssignmentStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "chained-assignment-statement.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_ChainedAssignmentStatementSource_MarksExpressionStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_DeconstructionAssignmentStatementSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "deconstruction-assignment-statement.cs",
            SObjectExpressionSources.DeconstructionAssignmentStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "deconstruction-assignment-statement.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_DeconstructionAssignmentStatementSource_MarksExpressionStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_ObjectInitializerDefinitionAssignmentSource_MarksLocalDeclarationStatement()
    {
        var filePath = WriteSourceFile(
            "object-initializer-definition-assignment.cs",
            SObjectExpressionSources.ObjectInitializerDefinitionAssignmentSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "object-initializer-definition-assignment.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_ObjectInitializerDefinitionAssignmentSource_MarksLocalDeclarationStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_ComplexCompoundAssignmentStatementSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "complex-compound-assignment-statement.cs",
            SObjectExpressionSources.ComplexCompoundAssignmentStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "complex-compound-assignment-statement.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_ComplexCompoundAssignmentStatementSource_MarksExpressionStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_AssignmentLeftOperandSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "assignment-left-operand.cs",
            SObjectExpressionSources.AssignmentLeftOperandSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "assignment-left-operand.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.ElementAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ExpressionStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_AssignmentLeftOperandSource_MarksExpressionStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_DefinitionLeftOperandSource_MarksLocalDeclarationStatement()
    {
        var filePath = WriteSourceFile(
            "definition-left-operand.cs",
            SObjectExpressionSources.DefinitionLeftOperandSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "definition-left-operand.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_DefinitionLeftOperandSource_MarksLocalDeclarationStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_CallArgumentStatementSource_MarksExpressionStatement()
    {
        var filePath = WriteSourceFile(
            "call-argument-statement.cs",
            SObjectExpressionSources.CallArgumentStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "call-argument-statement.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_CallArgumentStatementSource_MarksExpressionStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_PropertyAccessDefinitionSource_WhenPropertyNameMatches_MarksLocalDeclarationStatement()
    {
        var filePath = WriteSourceFile(
            "property-access-definition.cs",
            SObjectExpressionSources.PropertyAccessDefinitionSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "property-access-definition.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_PropertyAccessDefinitionSource_WhenPropertyNameMatches_MarksLocalDeclarationStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_IndexAccessDefinitionSource_WhenBaseMatches_MarksLocalDeclarationStatement()
    {
        var filePath = WriteSourceFile(
            "index-access-definition.cs",
            SObjectExpressionSources.IndexAccessDefinitionSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "index-access-definition.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_IndexAccessDefinitionSource_WhenBaseMatches_MarksLocalDeclarationStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_IfElseOnlySource_MarksElseClauseAndElseBodyTogether()
    {
        var filePath = WriteSourceFile(
            "if-else-only.cs",
            SObjectControlFlowSources.IfElseOnlySource);
        var rawDiffPath = Path.Combine(_tempDirectory, "if-else-only.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
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
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_IfElseOnlySource_MarksElseClauseAndElseBodyTogether),
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_sObjectDiffFilePath);
        Assert.Contains(
            "UnitTest: AnalyzeFromArgs_IfElseOnlySource_MarksElseClauseAndElseBodyTogether",
            diffText,
            StringComparison.Ordinal);
        TextDiffAssert.Contains("if (s.IsReady)", diffText, diffText);
        TextDiffAssert.Contains("return value + 2;", diffText, diffText);
    }

    [Fact]
    public void AnalyzeFromArgs_ForInitializerDeclarationSource_MarksWholeForStatement()
    {
        var filePath = WriteSourceFile(
            "for-initializer-host-sample.cs",
            SObjectControlFlowSources.ForInitializerDeclarationSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "for-initializer-host-sample.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ForStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_ForInitializerDeclarationSource_MarksWholeForStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_ForIncrementorSource_MarksWholeForStatement()
    {
        var filePath = WriteSourceFile(
            "for-incrementor-host-sample.cs",
            SObjectControlFlowSources.ForIncrementorSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "for-incrementor-host-sample.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.ForStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_ForIncrementorSource_MarksWholeForStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_WhileBodySource_MarksWholeWhileStatement()
    {
        var filePath = WriteSourceFile(
            "while-body-host-sample.cs",
            SObjectControlFlowSources.WhileBodySource);
        var rawDiffPath = Path.Combine(_tempDirectory, "while-body-host-sample.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.WhileStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_DoBodySource_MarksWholeDoStatement()
    {
        var filePath = WriteSourceFile(
            "do-body-host-sample.cs",
            SObjectControlFlowSources.DoBodySource);
        var rawDiffPath = Path.Combine(_tempDirectory, "do-body-host-sample.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.DoStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_SwitchConditionSource_MarksWholeSwitchStatement()
    {
        var filePath = WriteSourceFile(
            "switch-condition-host-sample.cs",
            SObjectControlFlowSources.SwitchConditionSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "switch-condition-host-sample.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        var seedMark = Assert.Single(result.SeedMarks);
        Assert.Equal(SyntaxKind.SimpleMemberAccessExpression, (SyntaxKind)seedMark.SyntaxNode.RawKind);
        AssertContainsPropagatedKind(result, SyntaxKind.SwitchStatement);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_SwitchConditionSource_MarksWholeSwitchStatement),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_SwitchCaseSingleStatementSource_MarksWholeSwitchSection()
    {
        var filePath = WriteSourceFile(
            "switch-case-single-statement.cs",
            SObjectControlFlowSources.SwitchCaseSingleStatementSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "switch-case-single-statement.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        AssertContainsPropagatedKind(result, SyntaxKind.SwitchSection);

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_SwitchCaseSingleStatementSource_MarksWholeSwitchSection),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_SwitchCaseWithoutBreakFullyMarkedSource_MarksWholeSwitchSection()
    {
        var filePath = WriteSourceFile(
            "switch-case-without-break-fully-marked.cs",
            SObjectControlFlowSources.SwitchCaseWithoutBreakFullyMarkedSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "switch-case-without-break-fully-marked.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "s",
            "--diff-out",
            rawDiffPath
        });

        AssertContainsPropagatedKind(result, SyntaxKind.SwitchSection);

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_SwitchCaseWithoutBreakFullyMarkedSource_MarksWholeSwitchSection),
            File.ReadAllText(rawDiffPath));
    }

    [Fact]
    public void AnalyzeFromArgs_LogicalMixedPrecedenceSource_ProducesLogicalOrMarkAndDiffFile()
    {
        var filePath = WriteSourceFile(
            "logical-mixed-precedence.cs",
            SObjectLogicalSources.LogicalMixedPrecedenceSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "logical-mixed-precedence.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "b",
            "--diff-out",
            rawDiffPath
        });

        var logicalMark = AssertSinglePropagatedLogicalOr(result, "a && b || c || !b");
        Assert.Single(result.Decisions);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_LogicalMixedPrecedenceSource_ProducesLogicalOrMarkAndDiffFile),
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_sObjectDiffFilePath);
        Assert.Contains(
            "UnitTest: AnalyzeFromArgs_LogicalMixedPrecedenceSource_ProducesLogicalOrMarkAndDiffFile",
            diffText,
            StringComparison.Ordinal);
        TextDiffAssert.Contains("a && b || c || !b", diffText, diffText);
        TextDiffAssert.Contains("c", diffText, diffText);
        Assert.Equal("c", result.Decisions[0].ReplacementNode?.ToString());
        TextDiffAssert.Contains("if (c)", result.RewrittenSource, diffText);
        TextDiffAssert.DoesNotContain("c || !b", result.RewrittenSource, diffText);
    }

    [Fact]
    public void AnalyzeFromArgs_LogicalMixedPrecedenceWithParenthesesSource_ProducesLogicalOrMarkAndDiffFile()
    {
        var filePath = WriteSourceFile(
            "logical-mixed-precedence-parenthesized.cs",
            SObjectLogicalSources.LogicalMixedPrecedenceWithParenthesesSource);
        var rawDiffPath = Path.Combine(
            _tempDirectory,
            "logical-mixed-precedence-parenthesized.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "b",
            "--diff-out",
            rawDiffPath
        });

        var logicalMark = AssertSinglePropagatedLogicalOr(result, "(a && b) || c || !b");
        Assert.Single(result.Decisions);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_LogicalMixedPrecedenceWithParenthesesSource_ProducesLogicalOrMarkAndDiffFile),
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_sObjectDiffFilePath);
        Assert.Contains(
            "UnitTest: AnalyzeFromArgs_LogicalMixedPrecedenceWithParenthesesSource_ProducesLogicalOrMarkAndDiffFile",
            diffText,
            StringComparison.Ordinal);
        TextDiffAssert.Contains("(a && b) || c || !b", diffText, diffText);
        Assert.Equal("c", result.Decisions[0].ReplacementNode?.ToString());
        TextDiffAssert.Contains("if (c)", result.RewrittenSource, diffText);
        TextDiffAssert.DoesNotContain("c || !b", result.RewrittenSource, diffText);
    }

    [Fact]
    public void AnalyzeFromArgs_MultiTargetGroupWithFiveHits_ProducesSingleLogicalOrMarkAndDiffFile()
    {
        var filePath = WriteSourceFile(
            "logical-multi-target-group.cs",
            SObjectLogicalSources.LogicalMultiTargetGroupFiveHitsSource);
        var rawDiffPath = Path.Combine(_tempDirectory, "logical-multi-target-group.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "b,c,d,e,f",
            "--diff-out",
            rawDiffPath
        });

        var logicalMark = AssertSinglePropagatedLogicalOr(result, "a || b || c || d || e || f || g || h");
        Assert.Single(result.Decisions);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            nameof(AnalyzeFromArgs_MultiTargetGroupWithFiveHits_ProducesSingleLogicalOrMarkAndDiffFile),
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_sObjectDiffFilePath);
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
    public void AnalyzeFromArgs_LargeParenthesizedCases_ProduceSingleLogicalOrMark(
        string caseName,
        string source,
        string expectedMarkedText,
        string expectedReplacementText)
    {
        var filePath = WriteSourceFile(
            $"{caseName}.cs",
            source);
        var rawDiffPath = Path.Combine(_tempDirectory, $"{caseName}.raw.diff");
        var application = CreateApplication();

        var result = application.AnalyzeFromArgs(new[]
        {
            filePath,
            "--target-name",
            "b",
            "--diff-out",
            rawDiffPath
        });

        var logicalMark = AssertSinglePropagatedLogicalOr(result, expectedMarkedText);
        Assert.Single(result.Decisions);
        Assert.NotNull(result.DiffFilePath);
        Assert.Equal(Path.GetFullPath(rawDiffPath), result.DiffFilePath);
        Assert.True(File.Exists(rawDiffPath));

        BuildDiffArtifactWriter.AppendDiffFragment(
            _sObjectDiffFilePath,
            $"{nameof(AnalyzeFromArgs_LargeParenthesizedCases_ProduceSingleLogicalOrMark)}:{caseName}",
            File.ReadAllText(rawDiffPath));

        var diffText = File.ReadAllText(_sObjectDiffFilePath);
        Assert.Contains(
            $"UnitTest: {nameof(AnalyzeFromArgs_LargeParenthesizedCases_ProduceSingleLogicalOrMark)}:{caseName}",
            diffText,
            StringComparison.Ordinal);
        Assert.Equal(expectedReplacementText, result.Decisions[0].ReplacementNode?.ToString());
        Assert.Contains(
            RemoveWhitespace(expectedReplacementText),
            RemoveWhitespace(diffText),
            StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> LargeParenthesizedLogicalEffectCases()
    {
        yield return CreateEffectCase(
            nameof(SObjectLogicalSources.LogicalMixedPrecedenceLargeCase1Source),
            SObjectLogicalSources.LogicalMixedPrecedenceLargeCase1Source,
            "(a && b) || (c && d) || !b || e || (f && g) || h || i || j || k || l",
            "(c && d)||e||(f && g)||h||i||j||k||l");
        yield return CreateEffectCase(
            nameof(SObjectLogicalSources.LogicalMixedPrecedenceLargeCase2Source),
            SObjectLogicalSources.LogicalMixedPrecedenceLargeCase2Source,
            "((a || b) && (c || !b)) || d || e || (f && g) || h || i || j || k || l || m",
            "d||e||(f && g)||h||i||j||k||l||m");
        yield return CreateEffectCase(
            nameof(SObjectLogicalSources.LogicalMixedPrecedenceLargeCase3Source),
            SObjectLogicalSources.LogicalMixedPrecedenceLargeCase3Source,
            "(a && b) || c || d || (!b && e) || f || g || h || (i && j) || k || l",
            "c||d||f||g||h||(i && j)||k||l");
        yield return CreateEffectCase(
            nameof(SObjectLogicalSources.LogicalMixedPrecedenceLargeCase4Source),
            SObjectLogicalSources.LogicalMixedPrecedenceLargeCase4Source,
            "((a && b) || c) || d || e || ((f || !b) && g) || h || i || j || k || l",
            "d||e||h||i||j||k||l");
        yield return CreateEffectCase(
            nameof(SObjectLogicalSources.LogicalMixedPrecedenceLargeCase5Source),
            SObjectLogicalSources.LogicalMixedPrecedenceLargeCase5Source,
            "(a && (b || c)) || d || e || !b || (f && g) || h || i || j || k || l",
            "d||e||(f && g)||h||i||j||k||l");
    }

    private static DeletionApplicationService CreateApplication()
    {
        return new DeletionApplicationService(RuleRegistry.CreateDefaultRules());
    }

    private string WriteSourceFile(string fileName, string source)
    {
        var filePath = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(filePath, source);
        return filePath;
    }

    private static object[] CreateEffectCase(
        string caseName,
        string source,
        string expectedMarkedText,
        string expectedReplacementText)
    {
        return new object[] { caseName, source, expectedMarkedText, expectedReplacementText };
    }

    private static bool IsNodeKind(Microsoft.CodeAnalysis.SyntaxNode node, SyntaxKind kind)
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

    private static Microsoft.CodeAnalysis.SyntaxNode AssertSinglePropagatedLogicalOr(
        PrototypeAnalysisResult result,
        string expectedText)
    {
        var exactLogicalMarks = result.PropagatedMarks
            .Where(mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.LogicalOrExpression))
            .Where(mark => string.Equals(mark.Mark.SyntaxNode.ToString(), expectedText, StringComparison.Ordinal))
            .Select(mark => mark.Mark.SyntaxNode)
            .ToList();
        if (exactLogicalMarks.Count > 0)
        {
            return Assert.Single(exactLogicalMarks);
        }

        var logicalMarks = result.PropagatedMarks
            .Where(mark => IsNodeKind(mark.Mark.SyntaxNode, SyntaxKind.LogicalOrExpression))
            .Select(mark => mark.Mark.SyntaxNode)
            .OrderByDescending(node => node.Span.Length)
            .ToList();
        Assert.NotEmpty(logicalMarks);
        return logicalMarks[0];
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
