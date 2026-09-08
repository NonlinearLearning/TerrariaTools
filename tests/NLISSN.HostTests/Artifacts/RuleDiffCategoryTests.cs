using Xunit;

namespace RoslynPrototype.Tests.Artifacts;

public sealed class RuleDiffCategoryTests
{
    [Fact]
    public void Resolve_AtomicExpressionProposal_UsesExpressionControlFlowCategory()
    {
        var category = RuleDiffCategoryRegistry.Resolve("propose.default-removal");

        Assert.Equal(RuleDiffCategory.ExpressionControlFlow, category);
    }

    [Fact]
    public void Resolve_KnownProposalRuleId_ReturnsItsStableCategory()
    {
        // Act
        var category = RuleDiffCategoryRegistry.Resolve("propose.type.parameter");

        // Assert
        Assert.Equal(RuleDiffCategory.ParameterShrink, category);
    }

    [Fact]
    public void Resolve_UnknownProposalRuleId_Throws()
    {
        // Act
        var exception = Assert.Throws<InvalidOperationException>(
          () => RuleDiffCategoryRegistry.Resolve("unknown.proposal-rule"));

        // Assert
        Assert.Contains("unknown.proposal-rule", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveFileDiffPath_CategoryAndNestedSource_UsesCategoryDirectory()
    {
        // Act
        var path = DiffPathResolver.ResolveFileDiffPath(
          "C:\\input",
          "C:\\input\\Game\\Player.cs",
          "C:\\result\\Diff",
          RuleDiffCategory.MethodGlobal);

        // Assert
        Assert.Equal(
          Path.Combine("C:\\result\\Diff", "MethodGlobal", "Game", "Player.rewrite.diff"),
          path);
    }
}
