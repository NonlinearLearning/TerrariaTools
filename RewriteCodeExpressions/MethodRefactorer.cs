using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using TerrariaTools.Analysis;
using TerrariaTools.Services;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 提供方法重构功能，包括删除未引用方法和将仅内部引用的公开方法改为 private 方法。
    /// 采用依赖图索引 (Dependency Graph Indexing) 算法，极大提升大规模解决方案的重构速度。
    /// </summary>
    public class MethodRefactorer
    {
        public class MethodRefactoringStats
        {
            public int TotalDeletedMethods { get; set; }
            public int TotalPrivatizedMethods { get; set; }
            public int TotalBodyClearedMethods { get; set; }
        }

        /// <summary>
        /// 执行解决方案级别的方法重构。
        /// </summary>
        public static async Task<MethodRefactoringStats> ExecuteSolutionRefactoringAsync(string solutionPath, IWorkspaceLoader loader, IProgress<string>? progress = null, bool aggressive = false, bool enableRatioAnalysis = true)
        {
            var stats = new MethodRefactoringStats();
            var startTime = DateTime.Now;

            progress?.Report($"[初始化] 正在加载解决方案: {solutionPath}");
            var solution = await loader.LoadSolutionAsync(solutionPath);
            if (solution == null)
            {
                progress?.Report("[错误] 无法加载解决方案。");
                return stats;
            }

            // Phase 1: 构建依赖图 (最耗时步骤，一次性完成)
            progress?.Report($"[分析] 正在构建全项目依赖图 (Dependency Graph)...");
            var builder = new CallGraphBuilder(solution);
            await builder.BuildAsync(progress);

            // Phase 2: 内存中计算可重构方法 (毫秒级)
            progress?.Report($"[分析] 正在计算可移除/私有化的方法...");
            var methodActions = builder.AnalyzeMethods(aggressive: aggressive, enableRatioAnalysis: enableRatioAnalysis);

            if (methodActions.Count == 0)
            {
                progress?.Report("[完成] 未发现可重构的方法。");
                return stats;
            }

            // 统计预期变更
            int expectedDelete = methodActions.Values.Count(a => a == CallGraphBuilder.GraphMethodAction.Delete);
            int expectedPrivatize = methodActions.Values.Count(a => a == CallGraphBuilder.GraphMethodAction.Privatize);
            int expectedClearBody = methodActions.Values.Count(a => a == CallGraphBuilder.GraphMethodAction.ClearBody);

            progress?.Report($"[分析完成] 发现 {methodActions.Count} 个待处理方法:");
            progress?.Report($"  - 待删除: {expectedDelete}");
            progress?.Report($"  - 待私有化: {expectedPrivatize}");
            progress?.Report($"  - 待清空体: {expectedClearBody}");

            // Phase 3: 按文档分组变更
            // 我们需要将 IMethodSymbol 映射回具体的 SyntaxNode 位置
            var actionsByDocument = new ConcurrentDictionary<DocumentId, List<(TextSpan Span, CallGraphBuilder.GraphMethodAction Action)>>();

            await Parallel.ForEachAsync(methodActions, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (kvp, ct) =>
            {
                var method = kvp.Key;
                var action = kvp.Value;

                foreach (var syntaxRef in method.DeclaringSyntaxReferences)
                {
                    var syntaxTree = syntaxRef.SyntaxTree;
                    var doc = solution.GetDocument(syntaxTree);

                    if (doc != null)
                    {
                        actionsByDocument.AddOrUpdate(doc.Id,
                            new List<(TextSpan, CallGraphBuilder.GraphMethodAction)> { (syntaxRef.Span, action) },
                            (key, list) => { lock (list) { list.Add((syntaxRef.Span, action)); } return list; });
                    }
                }
            });

            // Phase 4: 并行应用变更并写入磁盘
            progress?.Report($"[执行] 正在将变更应用到 {actionsByDocument.Count} 个文件...");

            int processedDocs = 0;
            int totalDocs = actionsByDocument.Count;
            int totalSaved = 0;

            await Parallel.ForEachAsync(actionsByDocument, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (kvp, ct) =>
            {
                var docId = kvp.Key;
                var actions = kvp.Value;

                var doc = solution.GetDocument(docId);
                if (doc == null) return;

                var root = await doc.GetSyntaxRootAsync(ct);
                if (root == null) return;

                var rewriter = new BatchMethodRewriter(actions);
                var newRoot = rewriter.Visit(root);

                if (newRoot != root)
                {
                    try
                    {
                        await loader.SaveDocumentAsync(doc.FilePath!, newRoot.ToFullString());
                        Interlocked.Increment(ref totalSaved);
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"[错误] 保存文件 {doc.FilePath} 失败: {ex.Message}");
                    }
                }

                var current = Interlocked.Increment(ref processedDocs);
                if (current % 50 == 0)
                {
                    progress?.Report($"[进度] 已处理 {current}/{totalDocs} 个文件...");
                }
            });

            stats.TotalDeletedMethods = expectedDelete;
            stats.TotalPrivatizedMethods = expectedPrivatize;
            stats.TotalBodyClearedMethods = expectedClearBody;

            var elapsed = DateTime.Now - startTime;
            progress?.Report($"\n[完成] 重构任务结束。耗时: {elapsed.TotalSeconds:F2} 秒。");
            progress?.Report($"  - 已修改文件数: {totalSaved}");

            return stats;
        }

        private class BatchMethodRewriter : CSharpSyntaxRewriter
        {
            private readonly HashSet<TextSpan> _deleteSpans;
            private readonly HashSet<TextSpan> _privatizeSpans;
            private readonly HashSet<TextSpan> _clearBodySpans;
            private readonly HashSet<TextSpan> _decoupleSpans;

            public BatchMethodRewriter(List<(TextSpan Span, CallGraphBuilder.GraphMethodAction Action)> actions)
            {
                _deleteSpans = new HashSet<TextSpan>(actions.Where(a => a.Action == CallGraphBuilder.GraphMethodAction.Delete).Select(a => a.Span));
                _privatizeSpans = new HashSet<TextSpan>(actions.Where(a => a.Action == CallGraphBuilder.GraphMethodAction.Privatize).Select(a => a.Span));
                _clearBodySpans = new HashSet<TextSpan>(actions.Where(a => a.Action == CallGraphBuilder.GraphMethodAction.ClearBody).Select(a => a.Span));
                _decoupleSpans = new HashSet<TextSpan>(actions.Where(a => a.Action == CallGraphBuilder.GraphMethodAction.Decouple).Select(a => a.Span));
            }

            public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                if (_deleteSpans.Contains(node.Span))
                {
                    // 返回 null 以删除节点
                    return null;
                }

                if (_privatizeSpans.Contains(node.Span))
                {
                    // 修改修饰符为 private
                    var newModifiers = SyntaxFactory.TokenList(
                        node.Modifiers.Select(m =>
                            m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword)
                            ? SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTriviaFrom(m)
                            : m)
                        .Where(m => !m.IsKind(SyntaxKind.VirtualKeyword)) // 私有方法不能是 virtual
                    );

                    // 如果原本没有访问修饰符（默认 private），则添加 private
                    if (!newModifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)))
                    {
                        newModifiers = newModifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTrailingTrivia(SyntaxFactory.Space));
                    }

                    node = node.WithModifiers(newModifiers);
                }

                if (_clearBodySpans.Contains(node.Span))
                {
                    // 清空方法体，只保留 out 参数赋值和 return default
                    var returnType = node.ReturnType;
                    bool isVoid = returnType is PredefinedTypeSyntax pre && pre.Keyword.IsKind(SyntaxKind.VoidKeyword);
                    bool isAsync = node.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));

                    var statements = new List<StatementSyntax>();

                    // 1. 处理 out 参数 (必须赋值)
                    foreach (var param in node.ParameterList.Parameters)
                    {
                        if (param.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword)))
                        {
                            statements.Add(SyntaxFactory.ExpressionStatement(
                                SyntaxFactory.AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    SyntaxFactory.IdentifierName(param.Identifier).WithTrailingTrivia(SyntaxFactory.Space),
                                    SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)
                                ).WithOperatorToken(SyntaxFactory.Token(SyntaxKind.EqualsToken).WithTrailingTrivia(SyntaxFactory.Space))
                            ));
                        }
                    }

                    // 2. 处理返回值
                    bool needsReturn = !isVoid;
                    if (isAsync)
                    {
                        if (isVoid) needsReturn = false;
                        else if (returnType is GenericNameSyntax) needsReturn = true; // Task<T>
                        else needsReturn = false; // Task
                    }

                    if (needsReturn)
                    {
                        statements.Add(SyntaxFactory.ReturnStatement(
                            SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)
                        ).WithReturnKeyword(SyntaxFactory.Token(SyntaxKind.ReturnKeyword).WithTrailingTrivia(SyntaxFactory.Space)));
                    }

                    var newBody = SyntaxFactory.Block(statements);
                    node = node.WithBody(newBody).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
                }

                if (_decoupleSpans.Contains(node.Span))
                {
                    // 解耦：移除 override, virtual, abstract 关键字
                    var newModifiers = SyntaxFactory.TokenList(
                        node.Modifiers.Where(m =>
                            !m.IsKind(SyntaxKind.OverrideKeyword) &&
                            !m.IsKind(SyntaxKind.VirtualKeyword) &&
                            !m.IsKind(SyntaxKind.AbstractKeyword))
                    );
                    node = node.WithModifiers(newModifiers);
                }

                return base.VisitMethodDeclaration(node);
            }
        }
    }
}
