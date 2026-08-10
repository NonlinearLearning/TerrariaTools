using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Analysis.ExpressionPropagation;
using Xunit;

namespace RoslynPrototype.Tests;

public sealed class ExpressionPropagationTopologyTests
{
    [Theory]
    [InlineData("a && b || c", "a", ExpressionPropagationMode.SiblingAndContinue)]
    [InlineData("a || b && c", "a", ExpressionPropagationMode.AggregateOnly)]
    [InlineData("a & b", "a", ExpressionPropagationMode.AggregateOnly)]
    [InlineData("a ?? b", "a", ExpressionPropagationMode.AggregateOnly)]
    public void Analyze_BinaryOperand_UsesDirectRoslynOperands(
        string source,
        string seedText,
        ExpressionPropagationMode expectedMode)
    {
        var path = Analyze(source, seedText);

        var step = Assert.IsType<ExpressionTopologyStep>(path.Steps.First());
        Assert.Equal(ExpressionTopologyKind.Binary, step.Kind);
        Assert.Equal(expectedMode, step.Mode);
        Assert.Equal(2, step.DirectOperands.Count);
        Assert.Equal(seedText, step.Input.ToString());
    }

    [Theory]
    [InlineData("!a && b", "a", ExpressionTopologyKind.Unary, ExpressionPropagationMode.Terminal)]
    [InlineData("a ? b : c && d", "b", ExpressionTopologyKind.Conditional, ExpressionPropagationMode.TerminalWhole)]
    [InlineData("++a", "a", ExpressionTopologyKind.Unary, ExpressionPropagationMode.TerminalMutation)]
    [InlineData("a = b", "a", ExpressionTopologyKind.Binary, ExpressionPropagationMode.Stop)]
    public void Analyze_TerminalBoundary_StopsAtTheOwningExpression(
        string source,
        string seedText,
        ExpressionTopologyKind expectedKind,
        ExpressionPropagationMode expectedMode)
    {
        var path = Analyze(source, seedText);

        var step = path.Steps.Last();
        Assert.Equal(expectedKind, step.Kind);
        Assert.Equal(expectedMode, step.Mode);
        Assert.Equal(ExpressionTopologyTermination.Terminal, path.Termination);
    }

    [Fact]
    public void Analyze_ParenthesizedLogicalOr_PreservesGroupingBeforeOuterAnd()
    {
        var path = Analyze("left && (target || fallback)", "target");

        Assert.Collection(
            path.Steps,
            step =>
            {
                Assert.Equal(ExpressionTopologyKind.Binary, step.Kind);
                Assert.Equal(ExpressionPropagationMode.AggregateOnly, step.Mode);
            },
            step => Assert.Equal(ExpressionTopologyKind.Grouping, step.Kind),
            step =>
            {
                Assert.Equal(ExpressionTopologyKind.Binary, step.Kind);
                Assert.Equal(ExpressionPropagationMode.SiblingAndContinue, step.Mode);
                Assert.Equal("left", step.DirectOperands.Single(operand => !ReferenceEquals(operand, step.Input)).ToString());
            });
    }

    [Fact]
    public void Analyze_UnsupportedShell_RecordsDirectStructuralOwners()
    {
        var path = Analyze("M(a)", "a");

        Assert.Equal(ExpressionTopologyTermination.UnsupportedParent, path.Termination);
        Assert.Contains(path.StructuralOwners, owner => owner is InvocationExpressionSyntax);
    }

    [Fact]
    public void Analyze_ExpressionInsideUsingBody_DoesNotExposeOuterUsingStatement()
    {
        const string source = """
            using System;

            class Sample
            {
                void Check(IDisposable resource, bool target)
                {
                    using (resource)
                    {
                        if (target)
                        {
                        }
                    }
                }
            }
            """;
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var seed = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Single(node => node.Identifier.ValueText == "target");

        var path = new ExpressionPropagationTopology(_ => null).Analyze(seed);

        Assert.Contains(path.StructuralOwners, owner => owner is IfStatementSyntax);
        Assert.DoesNotContain(path.StructuralOwners, owner => owner is UsingStatementSyntax);
    }

    [Fact]
    public void Analyze_UnaryTerminal_DoesNotExposeStructuralOwners()
    {
        var path = Analyze("!a && b", "a");

        Assert.Empty(path.StructuralOwners);
    }

    private static ExpressionTopologyPath Analyze(string source, string seedText)
    {
        var root = SyntaxFactory.ParseExpression(source);
        var seed = root.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Single(node => node.Identifier.ValueText == seedText);
        return new ExpressionPropagationTopology(_ => null).Analyze(seed);
    }
}
