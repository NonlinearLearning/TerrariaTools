using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TerrariaTools.RewriteCodeExpressions
{
    /// <summary>
    /// 提供条件表达式重构功能。专门用于移除特定的 Terraria 条件（如 netMode == 1）。
    /// </summary>
    public class ConditionRefactorer
    {
        private readonly Solution _solution;

        public ConditionRefactorer(Solution solution)
        {
            _solution = solution;
        }

        /// <summary>
        /// 执行解决方案级别的条件重构。
        /// </summary>
        public static async Task ExecuteSolutionRefactoringAsync(string solutionPath, Load loader)
        {
            Console.WriteLine($"\n[信息] 正在启动条件表达式重构 (移除 netMode == 1, netMode != 2, myPlayer == whoAmI)...");

            using var workspace = await loader.LoadSolutionAsync(solutionPath);
            if (workspace == null) return;

            var solution = workspace.CurrentSolution;
            var refactorer = new ConditionRefactorer(solution);

            var allDocuments = solution.Projects
                .SelectMany(p => p.Documents)
                .Where(d => d.FilePath != null && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Console.WriteLine($"[信息] 发现 {allDocuments.Count} 个待处理的 C# 文件。");

            int processedCount = 0;
            int changedCount = 0;
            int totalProcessed = allDocuments.Count;

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
            var results = new ConcurrentBag<RefactorResult>();
            var startTime = DateTime.Now;

            await Parallel.ForEachAsync(allDocuments, parallelOptions, async (doc, ct) =>
            {
                var result = await refactorer.ProcessFileAsync(doc.FilePath!);
                results.Add(result);

                if (result.AnyChanged)
                {
                    Interlocked.Increment(ref changedCount);
                }

                var currentCount = Interlocked.Increment(ref processedCount);
                if (currentCount % 100 == 0 || currentCount == totalProcessed)
                {
                    var elapsed = DateTime.Now - startTime;
                    var speed = currentCount / elapsed.TotalSeconds;
                    Console.WriteLine($"[{currentCount}/{totalProcessed}] 正在处理... 速度: {speed:F1} 文件/秒, 已变更: {changedCount}");
                }
            });

            if (changedCount > 0)
            {
                Console.WriteLine($"[信息] 正在将变更保存到磁盘...");
                int savedCount = 0;
                foreach (var res in results.Where(r => r.AnyChanged && r.NewRoot != null))
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
                Console.WriteLine($"[成功] 条件重构结束。已更新 {savedCount} 个文件。");
            }
            else
            {
                Console.WriteLine("[信息] 未发现需要重构的代码。");
            }
        }

        public class RefactorResult
        {
            public bool AnyChanged { get; set; }
            public SyntaxNode? NewRoot { get; set; }
            public string? FilePath { get; set; }
        }

        public async Task<RefactorResult> ProcessFileAsync(string filePath)
        {
            var result = new RefactorResult { FilePath = filePath };

            var document = _solution.Projects.SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == filePath || d.Name == filePath);

            if (document == null) return result;

            var root = await document.GetSyntaxRootAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            if (root == null || semanticModel == null) return result;

            var conditions = new List<RewriteCondition>
            {
                new RewriteCondition { SymbolName = "netMode", Operator = Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression, Value = "1", IsValueLiteral = true },
                new RewriteCondition { SymbolName = "netMode", Operator = Microsoft.CodeAnalysis.CSharp.SyntaxKind.NotEqualsExpression, Value = "2", IsValueLiteral = true },
                new RewriteCondition { SymbolName = "myPlayer", Operator = Microsoft.CodeAnalysis.CSharp.SyntaxKind.EqualsExpression, Value = "whoAmI", IsValueLiteral = false }
            };

            var newRoot = ExpressionProcessor.RemoveTerrariaConditions(root, semanticModel, conditions);

            if (newRoot != null && newRoot != root)
            {
                result.NewRoot = newRoot;
                result.AnyChanged = true;
            }

            return result;
        }
    }
}
