using Xunit;

namespace RoslynPrototype.Tests.Artifacts;

public sealed class RuleDiffCategoryTests
{
    [Fact]
    public void Resolve_AtomicExpressionProposal_UsesExpressionControlFlowCategory()
    {
        var category = RuleDiffCategoryRegistry.Resolve("DEL-SOBJ-PROPOSE-DEFAULT-001");

        Assert.Equal(RuleDiffCategory.ExpressionControlFlow, category);
    }

    [Fact]
    public void Resolve_KnownProposalRuleId_ReturnsItsStableCategory()
    {
        // Act
        var category = RuleDiffCategoryRegistry.Resolve("DEL-CLASS-PROP-PARAM-001");

        // Assert
        Assert.Equal(RuleDiffCategory.ParameterShrink, category);
    }

    [Fact]
    public void Resolve_UnknownProposalRuleId_Throws()
    {
        // Act
        var exception = Assert.Throws<InvalidOperationException>(
          () => RuleDiffCategoryRegistry.Resolve("DEL-UNKNOWN-PROP-001"));

        // Assert
        Assert.Contains("DEL-UNKNOWN-PROP-001", exception.Message, StringComparison.Ordinal);
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
