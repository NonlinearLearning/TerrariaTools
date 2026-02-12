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
using System.IO;

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

        /// <summary>
        /// 执行解决方案级别的类重构。
        /// </summary>
        /// <param name="solutionPath">解决方案路径</param>
        /// <param name="loader">用于加载解决方案的加载器</param>
        public static async Task ExecuteSolutionRefactoringAsync(string solutionPath, Load loader)
        {
            bool anyFileChangedInPass;
            int passCount = 0;

            do
            {
                passCount++;
                anyFileChangedInPass = false;
                Console.WriteLine($"\n[信息] 正在启动第 {passCount} 轮类重构迭代...");

                using var workspace = await loader.LoadSolutionAsync(solutionPath);
                if (workspace == null) break;

                var solution = workspace.CurrentSolution;
                var refactorer = new ClassRefactorer(solution);

                var allDocuments = solution.Projects
                    .SelectMany(p => p.Documents)
                    .Where(d => d.FilePath != null && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($"[信息] 发现 {allDocuments.Count} 个待处理的 C# 文件。");

                int totalDeleted = 0;
                int processedCount = 0;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                var startTime = DateTime.Now;
                var results = new ConcurrentBag<RefactorResult>();

                await Parallel.ForEachAsync(allDocuments, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var result = await refactorer.ProcessFileAsync(doc.FilePath!);
                        results.Add(result);

                        if (result.AnyChanged)
                        {
                            anyFileChangedInPass = true;
                            Interlocked.Add(ref totalDeleted, result.DeletedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[错误] 处理文件 {doc.FilePath} 时出错: {ex.Message}");
                    }

                    var currentCount = Interlocked.Increment(ref processedCount);
                    if (currentCount % 100 == 0 || currentCount == allDocuments.Count)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var speed = currentCount / elapsed.TotalSeconds;
                        Console.WriteLine($"[{currentCount}/{allDocuments.Count}] 正在分析类... 速度: {speed:F1} 文件/秒, 待删除类: {totalDeleted}");
                    }
                });

                if (anyFileChangedInPass)
                {
                    Console.WriteLine($"[信息] 正在将本轮类变更保存到磁盘...");
                    var currentSolution = solution;
                    int savedCount = 0;
                    foreach (var res in results.Where(r => r.AnyChanged && r.DocumentId != null && r.NewRoot != null))
                    {
                        try
                        {
                            File.WriteAllText(res.FilePath!, res.NewRoot!.ToFullString(), System.Text.Encoding.UTF8);
                            savedCount++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[错误] 写入文件 {res.FilePath} 失败: {ex.Message}");
                        }
                    }
                    Console.WriteLine($"[成功] 本轮迭代结束。已保存 {savedCount} 个文件的变更，共删除 {totalDeleted} 个类。");
                }
                else
                {
                    Console.WriteLine("[信息] 本轮未发现可重构的内容，迭代停止。");
                }
            } while (anyFileChangedInPass && passCount < 10);

            Console.WriteLine($"\n[完成] 类重构全流程结束。总共执行了 {passCount} 轮迭代。");
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

            var typeDecls = root.DescendantNodes()
                .Where(n => n is BaseTypeDeclarationSyntax || n is DelegateDeclarationSyntax)
                .ToList();
            var toDelete = new List<SyntaxNode>();

            // 并行分析文件中的所有类型
            if (typeDecls.Any())
            {
                var analysisTasks = typeDecls.Select(async typeDecl =>
                {
                    var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
                    if (typeSymbol == null) return (Decl: (SyntaxNode?)null, ShouldDelete: false);

                    // 1. 检查是否是程序的入口点所在的类 (包含 Main 方法)
                    if (typeSymbol is INamedTypeSymbol namedType && HasMainMethod(namedType, semanticModel.Compilation))
                        return (Decl: (SyntaxNode?)typeDecl, ShouldDelete: false);

                    // 2. 检查是否是静态类 (静态类不能删除)
                    if (typeSymbol is INamedTypeSymbol classType && classType.IsStatic)
                        return (Decl: (SyntaxNode?)typeDecl, ShouldDelete: false);

                    // 3. 检查全方案引用
                    var references = await SymbolFinder.FindReferencesAsync(typeSymbol, _solution);
                    bool hasExternalReferences = false;

                    foreach (var reference in references)
                    {
                        foreach (var location in reference.Locations)
                        {
                            // 检查引用是否在类型定义之外
                            if (!IsLocationInsideType(location, typeSymbol))
                            {
                                hasExternalReferences = true;
                                break;
                            }
                        }
                        if (hasExternalReferences) break;
                    }

                    return (Decl: (SyntaxNode?)typeDecl, ShouldDelete: !hasExternalReferences);
                });

                var analysisResults = await Task.WhenAll(analysisTasks);

                foreach (var res in analysisResults)
                {
                    if (res.Decl != null && res.ShouldDelete)
                    {
                        toDelete.Add(res.Decl);
                        Interlocked.Increment(ref result.DeletedCount);
                    }
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

        private bool IsLocationInsideType(ReferenceLocation location, ISymbol typeSymbol)
        {
            if (location.Document.Project.Solution != _solution) return false;

            foreach (var reference in typeSymbol.DeclaringSyntaxReferences)
            {
                if (reference.SyntaxTree == location.Location.SourceTree &&
                    reference.Span.Contains(location.Location.SourceSpan))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
