using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

using TerrariaTools;

namespace Example
{
    class RewriteCodeExpressionsExample
    {
        /// <summary>
        /// 程序主入口方法
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>异步任务</returns>
        public static async Task Run(string[] args)
        {
            var program = new RewriteCodeExpressionsExample();
            await program.CodeProcess();
        }

        /// <summary>
        /// 高性能代码处理过程。
        /// 组合方案：并行文档处理 + 基于符号的批量查找 + 内存缓存批量写回。
        /// </summary>
        /// <returns>异步任务</returns>
        async Task CodeProcess()
        {
            // 1. 初始化与加载解决方案
            string solutionPath = @"D:\lodes\TR\Backup\New1.27\TR\TerrariaServer.sln";
            using var loader = new TerrariaTools.Services.WorkspaceLoader();

            Console.WriteLine($"[信息] 正在加载解决方案: {solutionPath}");
            var solution = await loader.LoadSolutionAsync(solutionPath);
            if (solution == null)
            {
                Console.WriteLine("[错误] 加载解决方案失败。");
                return;
            }

            string targetFilePath = @"D:\lodes\TR\Backup\New1.27\TR\Terraria.GameInput\PlayerInput.cs";

            // 2. 获取目标文件的语义模型和符号
            Console.WriteLine($"[信息] 正在分析目标文件: {targetFilePath}");
            var targetModel = await loader.GetFileSemanticModelAsync(targetFilePath);
            if (targetModel == null)
            {
                Console.WriteLine("[错误] 未能加载目标文件的语义模型。");
                return;
            }

            // 提取所有需要查找引用的符号
            var symbolsToFind = new List<(ISymbol symbol, SyntaxAnnotation annotation)>();
            var propAnnotation = new SyntaxAnnotation("ReferenceType", "PropertyReference");
            var fieldAnnotation = new SyntaxAnnotation("ReferenceType", "FieldReference");
            var methodAnnotation = new SyntaxAnnotation("ReferenceType", "MethodReference");

            foreach (ISymbol prop in loader.GetPropertiesFromSemanticModel(targetModel)) symbolsToFind.Add((prop, propAnnotation));
            foreach (ISymbol field in loader.GetFieldsFromSemanticModel(targetModel)) symbolsToFind.Add((field, fieldAnnotation));
            foreach (ISymbol method in loader.GetMethodsFromSemanticModel(targetModel)) symbolsToFind.Add((method, methodAnnotation));

            Console.WriteLine($"[信息] 准备查找 {symbolsToFind.Count} 个符号的引用...");

            // 3. 批量查找引用并按文档分组 (方案二核心)
            // 使用 ConcurrentDictionary 存储每个文档需要处理的符号标记
            var documentsToProcess = new ConcurrentDictionary<DocumentId, List<(ISymbol symbol, SyntaxAnnotation annotation)>>();

            await Task.WhenAll(symbolsToFind.Select(async item =>
            {
                var references = await SymbolFinder.FindReferencesAsync(item.symbol, solution);
                foreach (var refSymbol in references)
                {
                    foreach (var location in refSymbol.Locations)
                    {
                        if (location.Document != null && !string.Equals(location.Document.FilePath, targetFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            documentsToProcess.AddOrUpdate(
                                location.Document.Id,
                                new List<(ISymbol, SyntaxAnnotation)> { item },
                                (id, list) => { lock (list) { list.Add(item); } return list; }
                            );
                        }
                    }
                }
            }));

            Console.WriteLine($"[信息] 查找到 {documentsToProcess.Count} 个文件包含引用。");

            // 4. 并行处理文档并缓存结果 (方案一核心)
            var modifiedDocs = new ConcurrentDictionary<string, string>();
            int processedCount = 0;

            await Task.WhenAll(documentsToProcess.Select(async entry =>
            {
                var docId = entry.Key;
                var annotations = entry.Value;
                var document = solution.GetDocument(docId);
                if (document == null) return;

                var model = await document.GetSemanticModelAsync();
                if (model == null) return;

                // 标记引用节点
                var annotatedRoot = await loader.FindSymbolsReferencesAsync(model, annotations);
                if (annotatedRoot != null)
                {
                    // 简化处理
                    // 使用 "ReferenceType" Kind 一次性处理所有类型的引用（属性、字段、方法）
                    // 这样可以在单次遍历中完成所有移除逻辑，且语义模型在处理过程中始终有效
                    var processedRoot = TerrariaTools.RewriteCodeExpressions.ExpressionProcessor.ProcessAnnotatedNodes(annotatedRoot, "ReferenceType", model);

                    // 存入内存缓存 (方案三核心)
                    modifiedDocs[document.FilePath!] = processedRoot.ToFullString();
                    System.Threading.Interlocked.Increment(ref processedCount);
                }
            }));

            // 5. 批量写回磁盘 (方案三核心)
            Console.WriteLine($"[信息] 正在将 {modifiedDocs.Count} 个修改后的文件写回磁盘...");
            foreach (var kvp in modifiedDocs)
            {
                try
                {
                    await Task.Run(() => System.IO.File.WriteAllText(kvp.Key, kvp.Value, System.Text.Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] 写入文件失败 {kvp.Key}: {ex.Message}");
                }
            }

            Console.WriteLine($"\n[完成] 高性能处理结束。总计修改了 {processedCount} 个文件。");
        }
    }
}
