using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Rewrite;

namespace RoslynPrototype.Application;

internal sealed class DeleteClassPostRewriteCleanupService
{
    private readonly PrototypeRewriter _rewriter = new();

    internal PrototypeAnalysisResult ApplyUsingCleanup(
      string filePath,
      PrototypeAnalysisResult result,
      Dictionary<string, string> projectSourcesByPath)
    {
        var currentSource = result.RewrittenSource;
        var cleanupEdits = new List<RewriteEdit>();
        var baselineDiagnostics =
          DeletionPostRewriteDiagnostics.GetStableErrorDiagnosticKeys(projectSourcesByPath);
        var changed = true;

        while (changed)
        {
            changed = false;
            var tree = CSharpSyntaxTree.ParseText(currentSource, path: filePath);
            if (tree.GetRoot() is not CompilationUnitSyntax root)
            {
                break;
            }

            foreach (var usingDirective in root.Usings)
            {
                if (usingDirective.Alias is not null ||
                    usingDirective.StaticKeyword != default ||
                    usingDirective.GlobalKeyword != default)
                {
                    continue;
                }

                var candidateRoot = root.RemoveNode(usingDirective, SyntaxRemoveOptions.KeepNoTrivia);
                if (candidateRoot is null)
                {
                    continue;
                }

                var candidateSource = candidateRoot.ToFullString();
                var candidateProjectSources = new Dictionary<string, string>(
                  projectSourcesByPath,
                  StringComparer.Ordinal)
                {
                    [filePath] = candidateSource
                };
                var candidateDiagnostics =
                  DeletionPostRewriteDiagnostics.GetStableErrorDiagnosticKeys(candidateProjectSources);
                if (!baselineDiagnostics.SetEquals(candidateDiagnostics))
                {
                    continue;
                }

                cleanupEdits.Add(new RewriteEdit(
                  filePath,
                  usingDirective.Span,
                  usingDirective.WithoutTrivia().ToFullString(),
                  string.Empty));
                currentSource = candidateSource;
                projectSourcesByPath[filePath] = candidateSource;
                changed = true;
                break;
            }
        }

        return MergeCleanupEdits(result, currentSource, cleanupEdits);
    }

    internal PrototypeAnalysisResult ApplyEmptyNamespaceCleanup(
      string filePath,
      PrototypeAnalysisResult result,
      Dictionary<string, string> projectSourcesByPath)
    {
        var currentSource = result.RewrittenSource;
        var cleanupEdits = new List<RewriteEdit>();
        var changed = true;

        while (changed)
        {
            changed = false;
            var tree = CSharpSyntaxTree.ParseText(currentSource, path: filePath);
            if (tree.GetRoot() is not CompilationUnitSyntax root)
            {
                break;
            }

            var emptyNamespace = root.DescendantNodes()
              .OfType<NamespaceDeclarationSyntax>()
              .OrderByDescending(node => node.Span.Length)
              .FirstOrDefault(namespaceNode =>
                namespaceNode.Members.Count == 0 &&
                namespaceNode.Usings.Count == 0 &&
                namespaceNode.Externs.Count == 0);
            if (emptyNamespace is null)
            {
                break;
            }

            var candidateRoot = root.RemoveNode(emptyNamespace, SyntaxRemoveOptions.KeepNoTrivia);
            if (candidateRoot is null)
            {
                break;
            }

            var candidateSource = candidateRoot.ToFullString();
            var candidateProjectSources = new Dictionary<string, string>(
              projectSourcesByPath,
              StringComparer.Ordinal)
            {
                [filePath] = candidateSource
            };
            var baselineDiagnostics =
              DeletionPostRewriteDiagnostics.GetStableErrorDiagnosticKeys(projectSourcesByPath);
            var candidateDiagnostics =
              DeletionPostRewriteDiagnostics.GetStableErrorDiagnosticKeys(candidateProjectSources);
            if (!baselineDiagnostics.SetEquals(candidateDiagnostics))
            {
                break;
            }

            cleanupEdits.Add(new RewriteEdit(
              filePath,
              emptyNamespace.Span,
              emptyNamespace.WithoutTrivia().ToFullString(),
              string.Empty));
            currentSource = candidateSource;
            projectSourcesByPath[filePath] = candidateSource;
            changed = true;
        }

        return MergeCleanupEdits(result, currentSource, cleanupEdits);
    }

    private PrototypeAnalysisResult MergeCleanupEdits(
      PrototypeAnalysisResult result,
      string currentSource,
      IReadOnlyList<RewriteEdit> cleanupEdits)
    {
        if (cleanupEdits.Count == 0)
        {
            return result;
        }

        var edits = result.Edits.Concat(cleanupEdits).ToList();
        return result with
        {
            Edits = edits,
            RewrittenSource = currentSource,
            DiffText = _rewriter.BuildDiffText(edits)
        };
    }
}
