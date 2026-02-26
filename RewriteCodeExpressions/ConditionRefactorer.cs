using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using TerrariaTools.Services;

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

        public class ConditionRefactoringStats
        {
            public int TotalChangedFiles { get; set; }
        }

        /// <summary>
        /// 执行解决方案级别的条件重构。
        /// </summary>
        /// <param name="solutionPath">解决方案路径</param>
        /// <param name="loader">用于加载解决方案的加载器</param>
        /// <param name="progress">进度报告回调</param>
        public static async Task<ConditionRefactoringStats> ExecuteSolutionRefactoringAsync(string solutionPath, IWorkspaceLoader loader, IProgress<string>? progress = null)
        {
            var stats = new ConditionRefactoringStats();
            progress?.Report("\n[信息] 正在启动条件重构 (Terraria 特有条件)...");

            var solution = await loader.LoadSolutionAsync(solutionPath);
            if (solution == null) return stats;

            var refactorer = new ConditionRefactorer(solution);

            var allDocuments = solution.Projects
                .SelectMany(p => p.Documents)
                .Where(d => d.FilePath != null && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .ToList();

            progress?.Report($"[信息] 发现 {allDocuments.Count} 个待处理的 C# 文件。");

            var results = new ConcurrentBag<RefactorResult>();
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            await Parallel.ForEachAsync(allDocuments, parallelOptions, async (doc, ct) =>
            {
                try
                {
                    var result = await refactorer.ProcessFileAsync(doc.FilePath!);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    progress?.Report($"[错误] 处理文件 {doc.FilePath} 时出错: {ex.Message}");
                }
            });

            var changedResults = results.Where(r => r.AnyChanged && r.NewRoot != null).ToList();

            if (changedResults.Any())
            {
                progress?.Report($"[信息] 发现 {changedResults.Count} 个文件需要修改，正在保存...");
                int savedCount = 0;

                foreach (var res in changedResults)
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
                stats.TotalChangedFiles = savedCount;
                progress?.Report($"[成功] 条件重构完成。已修改 {savedCount} 个文件。");
            }
            else
            {
                progress?.Report("[信息] 未发现需要修改的条件表达式。");
            }

            return stats;
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
