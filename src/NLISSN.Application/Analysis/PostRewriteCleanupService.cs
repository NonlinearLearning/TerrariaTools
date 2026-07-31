using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLISSN.Core.Rewrite;

namespace NLISSN.Application;

/// 在类删除改写之后清理失效 using 指令和空命名空间，并将清理编辑合入原有改写计划。
public sealed class PostRewriteCleanupService
{
    private readonly DiffBuilder _diffBuilder = new();

    // 反复尝试删除失效 using，并在不引入新错误时把清理结果合并回原分析结果。
    public PrototypeAnalysisResult ApplyUsingCleanup(string filePath, string originalSource, PrototypeAnalysisResult result, CleanupProjectState cleanupProjectState)
    {
        var currentSource = result.RewrittenSource;
        if (string.IsNullOrEmpty(currentSource))
        {
            return result;
        }

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
                if (!cleanupProjectState.TryAcceptCandidate(filePath, candidateSource))
                {
                    continue;
                }

                cleanupEdits.Add(new RewriteEdit(
                  filePath,
                  usingDirective.Span,
                  usingDirective.WithoutTrivia().ToFullString(),
                  string.Empty));
                currentSource = candidateSource;
                changed = true;
                break;
            }
        }

        return MergeCleanupEdits(filePath, originalSource, result, currentSource, cleanupEdits);
    }

    // 删除改写后留下的空命名空间声明，并把清理编辑并入现有 rewrite 计划。
    public PrototypeAnalysisResult ApplyEmptyNamespaceCleanup(string filePath, string originalSource, PrototypeAnalysisResult result, CleanupProjectState cleanupProjectState)
    {
        var currentSource = result.RewrittenSource;
        if (string.IsNullOrEmpty(currentSource))
        {
            return result;
        }

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
            if (!cleanupProjectState.TryAcceptCandidate(filePath, candidateSource))
            {
                break;
            }

            cleanupEdits.Add(new RewriteEdit(
              filePath,
              emptyNamespace.Span,
              emptyNamespace.WithoutTrivia().ToFullString(),
              string.Empty));
            currentSource = candidateSource;
            changed = true;
        }

        return MergeCleanupEdits(filePath, originalSource, result, currentSource, cleanupEdits);
    }

    private PrototypeAnalysisResult MergeCleanupEdits(string filePath, string originalSource, PrototypeAnalysisResult result, string currentSource, IReadOnlyList<RewriteEdit> cleanupEdits)
    {
        if (cleanupEdits.Count == 0)
        {
            return result;
        }

        var edits = result.Edits.Concat(cleanupEdits).ToList();
        var diff = _diffBuilder.Build(edits);
        var operations = new[]
        {
          new RewritePlanEdit(0, originalSource.Length, originalSource, currentSource)
        };
        var rewritePlans = new[] { new PrototypeFileRewritePlan(filePath, operations) };
        return result with
        {
            Edits = edits,
            RewrittenSource = currentSource,
            Diff = diff,
            RewritePlans = rewritePlans
        };
    }

    /// 维护当前项目的候选源码，并以编译诊断差集保护后处理清理。
    public sealed class CleanupProjectState
    {
        private readonly Dictionary<string, string> _projectSourcesByPath;
        private readonly HashSet<string> _baselineDiagnostics;

        // 记录当前项目源码快照，并计算清理前的基线错误集合。
        public CleanupProjectState(Dictionary<string, string> projectSourcesByPath)
        {
            _projectSourcesByPath = projectSourcesByPath;
            _baselineDiagnostics =
               PostRewriteDiagnostics.GetStableErrorDiagnosticKeys(projectSourcesByPath);
        }

        // 仅当候选源码不会改变错误诊断差集时接受它，并更新项目快照。
        public bool TryAcceptCandidate(string filePath, string candidateSource)
        {
            var candidateDiagnostics =
               PostRewriteDiagnostics.GetStableErrorDiagnosticKeys(
                _projectSourcesByPath,
                filePath,
                candidateSource);
            if (!_baselineDiagnostics.SetEquals(candidateDiagnostics))
            {
                return false;
            }

            _projectSourcesByPath[filePath] = candidateSource;
            return true;
        }
    }
}
