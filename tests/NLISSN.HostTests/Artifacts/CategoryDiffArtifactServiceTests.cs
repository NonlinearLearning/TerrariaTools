using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Rewrite;
using Xunit;

namespace RoslynPrototype.Tests.Artifacts;

public sealed class CategoryDiffArtifactServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"category-diff-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Write_ExpressionDecisionsShareOneCategoryDiff()
    {
        // Arrange
        const string source = "class Sample { int Run(int left, int right) => left + right; }";
        var inputRoot = Path.Combine(_temporaryDirectory, "input");
        var sourcePath = Path.Combine(inputRoot, "Sample.cs");
        var diffRoot = Path.Combine(_temporaryDirectory, "Diff");
        Directory.CreateDirectory(inputRoot);
        File.WriteAllText(sourcePath, source);
        var tree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
        var root = tree.GetRoot();
        var identifiers = root.DescendantNodes().OfType<IdentifierNameSyntax>()
          .Where(identifier => identifier.Identifier.ValueText is "left" or "right")
          .ToDictionary(identifier => identifier.Identifier.ValueText, StringComparer.Ordinal);
        var decisions = new[]
        {
            new RuleDecision(identifiers["left"], identifiers["left"], DecisionActionKind.Delete, "atomic")
            {
                RuleId = "DEL-SOBJ-PROPOSE-DEFAULT-001",
            },
            new RuleDecision(identifiers["right"], identifiers["right"], DecisionActionKind.Delete, "control-flow")
            {
                RuleId = "DEL-SOBJ-PROPOSE-IF-001",
            },
        };

        // Act
        new CategoryDiffArtifactService().Write(inputRoot, sourcePath, source, decisions, diffRoot, "legacy");

        // Assert
        var expressionDiff = File.ReadAllText(Path.Combine(diffRoot, "ExpressionControlFlow", "Sample.rewrite.diff"));
        Assert.Contains("left", expressionDiff, StringComparison.Ordinal);
        Assert.Contains("right", expressionDiff, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(diffRoot, "AtomicExpression")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }
}
