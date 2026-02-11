using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 提供类重构功能，包括删除未引用的类。
    /// </summary>
    public class ClassRefactorer
    {
        private readonly Solution _solution;
        private readonly ConcurrentDictionary<string, byte> _processedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        public ClassRefactorer(Solution solution)
        {
            _solution = solution;
        }

        public class RefactorResult
        {
            public bool AnyChanged => DeletedCount > 0;
            public int DeletedCount;
            public SyntaxNode? NewRoot { get; set; }
            public DocumentId? DocumentId { get; set; }
            public string? FilePath { get; set; }
        }

        public async Task<RefactorResult> ProcessFileAsync(string filePath)
        {
            var result = new RefactorResult { FilePath = filePath };
            if (_processedFiles.ContainsKey(filePath)) return result;
            _processedFiles.TryAdd(filePath, 0);

            var document = _solution.GetDocumentIdsWithFilePath(filePath)
                .Select(id => _solution.GetDocument(id))
                .FirstOrDefault();

            if (document == null)
            {
                document = _solution.Projects.SelectMany(p => p.Documents)
                    .FirstOrDefault(d => string.Equals(d.Name, filePath, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            }

            if (document == null) return result;
            result.DocumentId = document.Id;

            var root = await document.GetSyntaxRootAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (root == null || semanticModel == null) return result;

            var classDecls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
            var toDelete = new List<ClassDeclarationSyntax>();

            foreach (var classDecl in classDecls)
            {
                var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
                if (classSymbol == null) continue;

                // 1. 检查是否是程序的入口点所在的类 (包含 Main 方法)
                if (HasMainMethod(classSymbol, semanticModel.Compilation)) continue;

                // 2. 检查全方案引用
                var references = await SymbolFinder.FindReferencesAsync(classSymbol, _solution);
                bool hasExternalReferences = false;

                foreach (var reference in references)
                {
                    foreach (var location in reference.Locations)
                    {
                        // 检查引用是否在类定义之外
                        if (!IsLocationInsideClass(location, classSymbol))
                        {
                            hasExternalReferences = true;
                            break;
                        }
                    }
                    if (hasExternalReferences) break;
                }

                if (!hasExternalReferences)
                {
                    toDelete.Add(classDecl);
                    Interlocked.Increment(ref result.DeletedCount);
                }
            }

            if (toDelete.Any())
            {
                var finalRoot = root.RemoveNodes(toDelete, SyntaxRemoveOptions.KeepNoTrivia);
                result.NewRoot = finalRoot?.NormalizeWhitespace();
            }

            return result;
        }

        private bool HasMainMethod(INamedTypeSymbol classSymbol, Compilation compilation)
        {
            // 检查显式的 Main 方法
            var hasMain = classSymbol.GetMembers().OfType<IMethodSymbol>()
                .Any(m => m.Name == "Main" && m.IsStatic);

            if (hasMain) return true;

            // 检查是否是 Compilation 的入口点
            var entryPoint = compilation.GetEntryPoint(default);
            if (entryPoint != null && SymbolEqualityComparer.Default.Equals(entryPoint.ContainingType, classSymbol))
            {
                return true;
            }

            return false;
        }

        private bool IsLocationInsideClass(ReferenceLocation location, INamedTypeSymbol classSymbol)
        {
            // 检查引用位置是否在类的任何声明部分内部 (支持 partial 类)
            foreach (var declaringSyntax in classSymbol.DeclaringSyntaxReferences)
            {
                if (declaringSyntax.SyntaxTree == location.Location.SourceTree &&
                    declaringSyntax.Span.Contains(location.Location.SourceSpan))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
