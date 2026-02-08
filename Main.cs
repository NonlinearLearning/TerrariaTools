/**
 * 该文件是程序的入口点。
 * 负责解析命令行参数、调用加载逻辑以获取解决方案、语义模型和语法树。
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerrariaTools
{
    /// <summary>
    /// 程序主入口类
    /// </summary>
    class Program
    {
        /// <summary>
        /// 程序主入口方法
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>异步任务</returns>
        static async Task Main(string[] args)
        {
            var program = new Program();
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
            var loader = new Load();

            Console.WriteLine($"[信息] 正在加载解决方案: {solutionPath}");
            using var workspace = await loader.LoadSolutionAsync(solutionPath);
            if (workspace == null)
            {
                Console.WriteLine("[错误] 加载解决方案失败。");
                return;
            }

            var solution = workspace.CurrentSolution;
            string targetFilePath = @"D:\lodes\TR\Backup\New1.27\TR\Terraria.GameInput\PlayerInput.cs";

            // 2. 获取目标文件的语义模型和符号
            Console.WriteLine($"[信息] 正在分析目标文件: {targetFilePath}");
            var targetModel = await loader.GetFileSemanticModelAsync(workspace, targetFilePath);
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

            foreach (var prop in loader.GetPropertiesFromSemanticModel(targetModel)) symbolsToFind.Add((prop, propAnnotation));
            foreach (var field in loader.GetFieldsFromSemanticModel(targetModel)) symbolsToFind.Add((field, fieldAnnotation));
            foreach (var method in loader.GetMethodsFromSemanticModel(targetModel)) symbolsToFind.Add((method, methodAnnotation));

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

        async Task CodeProcess_Old()
        {
                        // 硬编码解决方案路径
            string solutionPath = @"D:\lodes\TR\Backup\New1.27\TR\TerrariaServer.sln";
            // 实例化加载工具类
            var loader = new Load();

            Console.WriteLine($"[信息] 正在加载解决方案: {solutionPath}");
            // 调用现有函数异步加载解决方案并获取工作区
            using var workspace = await loader.LoadSolutionAsync(solutionPath);
            if (workspace == null)
            {
                Console.WriteLine("[错误] 加载解决方案失败，请检查路径或环境配置。");
                return;
            }

            Console.WriteLine("[信息] 解决方案加载成功，正在加载语义模型...");
            // 加载解决方案中所有项目的语义模型
            var semanticModels = await loader.LoadSolutionSemanticModelsAsync(workspace);
            Console.WriteLine($"[信息] 已成功加载 {semanticModels.Count} 个项目的语义模型。");

            Console.WriteLine("[信息] 正在加载所有语法树...");
            // 加载工作区中所有文档的语法树
            var syntaxTrees = await loader.LoadAllSyntaxTreesAsync(workspace);
            Console.WriteLine($"[信息] 已成功加载 {syntaxTrees.Count} 个语法树。");

            Console.WriteLine("[完成] 基础数据加载完毕。");

            // 指定要加载的文件路径
            string targetFilePath = @"D:\lodes\TR\Backup\New1.27\TR\Terraria.GameInput\PlayerInput.cs";
            Console.WriteLine($"\n[信息] 正在加载指定文件: {targetFilePath}");

            // 加载指定文件的语法树
            var cloudSyntaxTree = await loader.LoadFileSyntaxTreeAsync(workspace, targetFilePath);
            if (cloudSyntaxTree != null)
            {
                Console.WriteLine("[成功] 已加载 Cloud.cs 的语法树。");
            }
            else
            {
                Console.WriteLine("[警告] 未能加载 Cloud.cs 的语法树，请检查文件路径是否正确或是否存在于解决方案中。");
            }

            // 加载指定文件的语义模型
            var cloudSemanticModel = await loader.GetFileSemanticModelAsync(workspace, targetFilePath);
            if (cloudSemanticModel != null)
            {
                Console.WriteLine("[成功] 已加载 Cloud.cs 的语义模型。");

                // 获取并打印属性符号
                var properties = loader.GetPropertiesFromSemanticModel(cloudSemanticModel);
                Console.WriteLine("\n[属性列表]:");
                foreach (var prop in properties)
                {
                    Console.WriteLine($"  - {prop.ToDisplayString()}");
                }

                // 获取并打印所有字段
                var fields = loader.GetFieldsFromSemanticModel(cloudSemanticModel);
                Console.WriteLine("\n[字段列表]:");
                foreach (var field in fields)
                {
                    Console.WriteLine($"  - {field.ToDisplayString()}");
                }

                // 获取并打印所有方法
                var methods = loader.GetMethodsFromSemanticModel(cloudSemanticModel);
                Console.WriteLine("\n[方法列表]:");
                foreach (var method in methods)
                {
                    Console.WriteLine($"  - {method.ToDisplayString()}");
                }

                // 获取并打印所有具名类型
                var namedTypes = loader.GetNamedTypesFromSemanticModel(cloudSemanticModel);
                Console.WriteLine("\n[具名类型列表]:");
                foreach (var type in namedTypes)
                {
                    Console.WriteLine($"  - {type.ToDisplayString()} ({type.TypeKind})");
                }

                Console.WriteLine("\n[信息] 正在查找 Cloud.cs 中定义的符号在全解决方案中的引用并进行简化处理...");

                // 准备所有要查找的符号及其对应的标记
                var symbolAnnotations = new System.Collections.Generic.List<(Microsoft.CodeAnalysis.ISymbol, Microsoft.CodeAnalysis.SyntaxAnnotation)>();
                var propAnnotation = new Microsoft.CodeAnalysis.SyntaxAnnotation("ReferenceType", "PropertyReference");
                var fieldAnnotation = new Microsoft.CodeAnalysis.SyntaxAnnotation("ReferenceType", "FieldReference");
                var methodAnnotation = new Microsoft.CodeAnalysis.SyntaxAnnotation("ReferenceType", "MethodReference");

                foreach (var prop in properties) symbolAnnotations.Add((prop, propAnnotation));
                foreach (var field in fields) symbolAnnotations.Add((field, fieldAnnotation));
                foreach (var method in methods) symbolAnnotations.Add((method, methodAnnotation));

                int modifiedFilesCount = 0;

                // 遍历所有项目的语义模型查找引用
                foreach (var projectModels in semanticModels)
                {
                    var project = projectModels.Key;
                    var models = projectModels.Value;

                    foreach (var model in models)
                    {
                        string docPath = model.SyntaxTree.FilePath;

                        // 跳过 Cloud.cs 文件本身的搜索
                        if (string.Equals(docPath, targetFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // 查找并标记所有符号引用
                        var annotatedRoot = await loader.FindSymbolsReferencesAsync(model, symbolAnnotations);

                        if (annotatedRoot != null)
                        {
                            // 检查是否有任何标记被添加
                            bool hasAnnotations = annotatedRoot.GetAnnotatedNodes(propAnnotation).Any() ||
                                                annotatedRoot.GetAnnotatedNodes(fieldAnnotation).Any() ||
                                                annotatedRoot.GetAnnotatedNodes(methodAnnotation).Any();

                            if (hasAnnotations)
                            {
                                Console.WriteLine($"[处理] 正在处理并简化文件: {docPath}");

                                // 对标记的节点进行 ProcessAnnotatedNodes 处理
                                var processedRoot = TerrariaTools.RewriteCodeExpressions.ExpressionProcessor.ProcessAnnotatedNodes(annotatedRoot, propAnnotation);
                                processedRoot = TerrariaTools.RewriteCodeExpressions.ExpressionProcessor.ProcessAnnotatedNodes(processedRoot, fieldAnnotation);
                                processedRoot = TerrariaTools.RewriteCodeExpressions.ExpressionProcessor.ProcessAnnotatedNodes(processedRoot, methodAnnotation);

                                // 将结果写回文件
                                try
                                {
                                    System.IO.File.WriteAllText(docPath, processedRoot.ToFullString(), System.Text.Encoding.UTF8);
                                    Console.WriteLine($"[成功] 已将简化后的代码写回: {docPath}");
                                    modifiedFilesCount++;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[错误] 写入文件失败 {docPath}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                Console.WriteLine($"\n[完成] 总计处理并修改了 {modifiedFilesCount} 个文件。");
            }
            else
            {
                Console.WriteLine("[警告] 未能加载 Cloud.cs 的语义模型。");
            }
        }
    }
}