using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;

using TerrariaTools.Services;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 提供基于名称匹配的方法重构功能。支持删除未引用的方法，或清空特定继承关系下的方法体。
    /// </summary>
    public class NameBasedMethodRefactorer
    {
        private readonly Solution _solution;
        private readonly string _namePattern;
        private readonly ConcurrentDictionary<string, byte> _processedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        public NameBasedMethodRefactorer(Solution solution, string namePattern)
        {            _solution = solution;
            _namePattern = namePattern;
        }

        /// <summary>
        /// 执行解决方案级别的方法重构。
        /// </summary>
        /// <param name="solutionPath">解决方案路径</param>
        /// <param name="loader">用于加载解决方案的加载器</param>
        /// <param name="namePattern">方法名称包含的模式字符串</param>
        /// <param name="progress">进度报告回调</param>
        public static async Task<MethodRefactorer.MethodRefactoringStats> ExecuteSolutionRefactoringAsync(string solutionPath, IWorkspaceLoader loader, string namePattern, IProgress<string>? progress = null)
        {
            var stats = new MethodRefactorer.MethodRefactoringStats();
            bool anyFileChangedInPass;
            int passCount = 0;

            do
            {
                passCount++;
                anyFileChangedInPass = false;
                progress?.Report($"\n[信息] 正在启动第 {passCount} 轮基于名称的清理 ({namePattern})...");

                var solution = await loader.LoadSolutionAsync(solutionPath);
                if (solution == null) break;

                var refactorer = new NameBasedMethodRefactorer(solution, namePattern);

                var allDocuments = solution.Projects
                    .SelectMany(p => p.Documents)
                    .Where(d => d.FilePath != null && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                progress?.Report($"[信息] 发现 {allDocuments.Count} 个待处理的 C# 文件。");

                int totalDeletedInPass = 0;
                int totalBodyClearedInPass = 0;
                int processedCount = 0;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                var results = new ConcurrentBag<RefactorResult>();
                var startTime = DateTime.Now;

                await Parallel.ForEachAsync(allDocuments, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var result = await refactorer.ProcessFileAsync(doc.FilePath!);
                        results.Add(result);

                        if (result.AnyChanged)
                        {
                            anyFileChangedInPass = true;
                            Interlocked.Add(ref totalDeletedInPass, result.DeletedCount);
                            Interlocked.Add(ref totalBodyClearedInPass, result.BodyClearedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"[错误] 处理文件 {doc.FilePath} 时出错: {ex.Message}");
                    }

                    var currentCount = Interlocked.Increment(ref processedCount);
                    if (currentCount % 100 == 0 || currentCount == allDocuments.Count)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var speed = currentCount / elapsed.TotalSeconds;
                        progress?.Report($"[{currentCount}/{allDocuments.Count}] 正在重构方法... 速度: {speed:F1} 文件/秒, 变更数: {totalDeletedInPass + totalBodyClearedInPass}");
                    }
                });

                if (anyFileChangedInPass)
                {
                    progress?.Report($"[信息] 正在将本轮变更保存到磁盘...");
                    int savedCount = 0;
                    foreach (var res in results.Where(r => r.AnyChanged && r.NewRoot != null))
                    {
                        try
                        {
                            await loader.SaveDocumentAsync(res.FilePath!, res.NewRoot!.ToFullString());
                            savedCount++;
                        }
                        catch (Exception ex)
                        {
                            progress?.Report($"[错误] 写入文件 {res.FilePath} 失败: {ex.Message}");
                        }
                    }

                    stats.TotalDeletedMethods += totalDeletedInPass;
                    stats.TotalBodyClearedMethods += totalBodyClearedInPass;

                    progress?.Report($"[成功] 本轮迭代结束。已保存 {savedCount} 个文件的变更。");
                    progress?.Report($"  - 删除: {totalDeletedInPass}");
                    progress?.Report($"  - 清空方法体: {totalBodyClearedInPass}");
                }
                else
                {
                    progress?.Report("[信息] 本轮未发现可重构的内容，迭代停止。");
                }
            } while (anyFileChangedInPass && passCount < 10);

            progress?.Report($"\n[完成] 名称清理全流程结束。总共执行了 {passCount} 轮迭代。");
            return stats;
        }

        public class RefactorResult
        {
            public bool AnyChanged => DeletedCount > 0 || BodyClearedCount > 0;
            public int DeletedCount;
            public int BodyClearedCount;
            public SyntaxNode? NewRoot { get; set; }
            public string? FilePath { get; set; }
        }

        private static readonly SyntaxAnnotation DeleteAnnotation = new SyntaxAnnotation("Delete");
        private static readonly SyntaxAnnotation ClearBodyAnnotation = new SyntaxAnnotation("ClearBody");

        public async Task<RefactorResult> ProcessFileAsync(string filePath, bool useFileLock = true)
        {
            var result = new RefactorResult { FilePath = filePath };
            if (useFileLock && !_processedFiles.TryAdd(filePath, 0)) return result;

            var document = _solution.Projects.SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == filePath || d.Name == filePath);

            if (document == null) return result;

            var root = await document.GetSyntaxRootAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (root == null || semanticModel == null) return result;

            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            var toDelete = new HashSet<MethodDeclarationSyntax>();
            var toClearBody = new HashSet<MethodDeclarationSyntax>();

            // 并行分析文件中的所有方法
            var matchingMethods = methods
                .Where(method => System.Text.RegularExpressions.Regex.IsMatch(method.Identifier.Text, _namePattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .ToList();

            if (matchingMethods.Any())
            {
                var analysisTasks = matchingMethods.Select(async method =>
                {
                    var methodSymbol = semanticModel.GetDeclaredSymbol(method);
                    if (methodSymbol == null) return (Method: (MethodDeclarationSyntax?)null, ShouldDelete: false, ShouldClearBody: false);

                    var (shouldDelete, shouldClearBody) = await AnalyzeMethodAsync(method, methodSymbol, document);
                    return (Method: (MethodDeclarationSyntax?)method, ShouldDelete: shouldDelete, ShouldClearBody: shouldClearBody);
                });

                var analysisResults = await Task.WhenAll(analysisTasks);

                foreach (var res in analysisResults)
                {
                    if (res.Method == null) continue;
                    if (res.ShouldDelete) toDelete.Add(res.Method);
                    if (res.ShouldClearBody) toClearBody.Add(res.Method);
                }
            }

            // 标注节点
            if (!toDelete.Any() && !toClearBody.Any()) return result;

            var annotatedRoot = root.ReplaceNodes(
                toDelete.Concat(toClearBody),
                (oldNode, newNode) =>
                {
                    if (toDelete.Any(m => m.IsEquivalentTo(oldNode)))
                        return newNode.WithAdditionalAnnotations(DeleteAnnotation);

                    if (toClearBody.Any(m => m.IsEquivalentTo(oldNode)))
                    {
                        var methodSymbol = semanticModel.GetDeclaredSymbol(oldNode);
                        if (methodSymbol != null)
                        {
                            var statements = new List<StatementSyntax>();

                            // 1. 处理 out 参数
                            foreach (var param in methodSymbol.Parameters.Where(p => p.RefKind == RefKind.Out))
                            {
                                var defaultValue = ExpressionSimplifier.CreatePlaceholder(param.Type);
                                statements.Add(SyntaxFactory.ExpressionStatement(
                                    SyntaxFactory.AssignmentExpression(
                                        SyntaxKind.SimpleAssignmentExpression,
                                        SyntaxFactory.IdentifierName(param.Name),
                                        defaultValue)));
                            }

                            // 2. 处理返回值
                            if (!methodSymbol.ReturnsVoid)
                            {
                                var returnExpression = ExpressionSimplifier.CreatePlaceholder(methodSymbol.ReturnType);
                                statements.Add(SyntaxFactory.ReturnStatement(returnExpression));
                            }

                            newNode = ((MethodDeclarationSyntax)newNode)
                                .WithBody(SyntaxFactory.Block(statements))
                                .WithExpressionBody(null)
                                .WithSemicolonToken(default);
                        }
                        return newNode.WithAdditionalAnnotations(ClearBodyAnnotation);
                    }

                    return newNode;
                });

            var bodyClearedRoot = annotatedRoot;

            // 3. 移除被标记为 Delete 的节点，并进行传播处理
            var finalRoot = ExpressionProcessor.RemoveAnnotatedParts(bodyClearedRoot, DeleteAnnotation, semanticModel);

            if (finalRoot == null) return result;

            if (finalRoot != root)
            {
                result.NewRoot = finalRoot;
                result.DeletedCount = toDelete.Count;
                result.BodyClearedCount = toClearBody.Count;
            }

            return result;
        }

        private async Task<(bool shouldDelete, bool shouldClearBody)> AnalyzeMethodAsync(MethodDeclarationSyntax methodDecl, IMethodSymbol methodSymbol, Document document)
        {
            // 抽象方法不处理其删除逻辑（保留定义）
            if (methodSymbol.IsAbstract) return (false, false);

            // 检查是否是程序的入口点 (Main 方法)
            if (methodSymbol.Name == "Main" && methodSymbol.IsStatic)
            {
                return (false, false);
            }

            // 检查是否是接口实现
            bool isInterfaceImpl = IsInterfaceImplementation(methodSymbol);

            // 检查继承体系关联
            var containingType = methodSymbol.ContainingType;
            bool hasInheritanceAssociation = false;

            if (containingType != null)
            {
                bool isTypeWithInheritance =
                    containingType.TypeKind == TypeKind.Class ||
                    containingType.TypeKind == TypeKind.Struct ||
                    containingType.IsRecord;

                hasInheritanceAssociation =
                    methodSymbol.IsOverride ||
                    methodSymbol.IsVirtual ||
                    isInterfaceImpl ||
                    (isTypeWithInheritance && (
                        (containingType.TypeKind == TypeKind.Class && !containingType.IsSealed) ||
                        (containingType.BaseType != null &&
                         containingType.BaseType.SpecialType == SpecialType.None &&
                         containingType.BaseType.TypeKind == TypeKind.Class) ||
                        containingType.AllInterfaces.Any()
                    ));
            }

            // 使用 SymbolFinder 在全解决方案范围内查找引用
            var references = await SymbolFinder.FindReferencesAsync(methodSymbol, _solution);
            bool hasExternalReferences = false;

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    var doc = location.Document;
                    if (doc == null) continue;

                    var docModel = await doc.GetSemanticModelAsync();
                    if (docModel == null) continue;

                    var root = await doc.GetSyntaxRootAsync();
                    var node = root?.FindNode(location.Location.SourceSpan);
                    if (node == null) continue;

                    // 1. 比较是否是当前方法本身的定义位置
                    if (doc.Id == document.Id)
                    {
                        var declaredSymbol = docModel.GetDeclaredSymbol(node);
                        if (SymbolEqualityComparer.Default.Equals(declaredSymbol, methodSymbol)) continue;
                    }

                    // 2. 检查引用是否在当前类内部（处理 partial 类跨文件的情况）
                    if (containingType != null)
                    {
                        var enclosingSymbol = docModel.GetEnclosingSymbol(location.Location.SourceSpan.Start);
                        if (IsSymbolInsideClass(enclosingSymbol, containingType))
                        {
                            // 内部引用，继续检查其他引用
                            continue;
                        }
                    }

                    // 如果引用在类外部，则视为外部引用
                    hasExternalReferences = true;
                    break;
                }
                if (hasExternalReferences) break;
            }

            // 逻辑：
            // 1. 如果有继承体系关联，不能删除，只能清空方法体
            if (hasInheritanceAssociation)
            {
                return (false, true);
            }

            // 2. 如果有外部引用，不能删除，只能清空方法体
            if (hasExternalReferences)
            {
                return (false, true);
            }

            // 3. 否则可以安全删除
            return (true, false);
        }

        private bool IsSymbolInsideClass(ISymbol? symbol, INamedTypeSymbol classSymbol)
        {
            while (symbol != null)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol, classSymbol))
                    return true;
                symbol = symbol.ContainingSymbol;
            }
            return false;
        }

        private bool IsInterfaceImplementation(IMethodSymbol methodSymbol)
        {
            var containingType = methodSymbol.ContainingType;
            if (containingType == null) return false;

            foreach (var iface in containingType.AllInterfaces)
            {
                foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(member), methodSymbol))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

    }
}
