using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Decision;
using NLISSN.Core.Rewrite;
using System.Text;

namespace NLISSN.Artifacts;

/// <summary>
/// Writes category-specific audit diffs from independently rewritten decisions.
/// </summary>
internal sealed class CategoryDiffArtifactService
{
    private readonly TextDiffRenderer _renderer = new();

    internal int Write(
      string inputRootPath,
      string filePath,
      string source,
      IReadOnlyList<RuleDecision> decisions,
      string diffRootPath,
      string diffView)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentException.ThrowIfNullOrWhiteSpace(diffRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(diffView);

        var categorizedDecisions = decisions
          .Where(decision => decision.Action != DecisionActionKind.Skip)
          .GroupBy(GetCategory)
          .OrderBy(group => group.Key)
          .ToArray();
        if (categorizedDecisions.Length == 0)
        {
            return 0;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var compilation = CSharpCompilation.Create(
          "CategoryDiff",
          new[] { syntaxTree },
          new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
          new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var root = syntaxTree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var rewriter = new PrototypeRewriter();
        var writtenDiffCount = 0;

        foreach (var group in categorizedDecisions)
        {
            var result = rewriter.Rewrite(
              root,
              semanticModel,
              group.Select(decision => RebindDecision(root, decision)));
            if (result.Diff.Files.Count == 0)
            {
                continue;
            }

            var diffPath = DiffPathResolver.ResolveFileDiffPath(
              inputRootPath,
              filePath,
              diffRootPath,
              group.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(diffPath)!);
            File.WriteAllText(
              diffPath,
              _renderer.Render(result.Diff.Files.Single(), diffView),
              new UTF8Encoding(false));
            writtenDiffCount++;
        }

        return writtenDiffCount;
    }

    private static RuleDiffCategory GetCategory(RuleDecision decision)
    {
        if (string.IsNullOrWhiteSpace(decision.RuleId))
        {
            throw new InvalidOperationException("Category diff rendering requires a proposal rule ID.");
        }

        return RuleDiffCategoryRegistry.Resolve(decision.RuleId);
    }

    private static RuleDecision RebindDecision(SyntaxNode root, RuleDecision decision)
    {
        return decision with
        {
            OriginalNode = FindEquivalentNode(root, decision.OriginalNode),
            FinalNode = FindEquivalentNode(root, decision.FinalNode),
            ReplacementNode = decision.ReplacementNode,
        };
    }

    private static SyntaxNode FindEquivalentNode(SyntaxNode root, SyntaxNode node)
    {
        return root.DescendantNodesAndSelf()
          .Single(candidate =>
            candidate.RawKind == node.RawKind &&
            candidate.Span == node.Span);
    }
}
