/**
 * 该文件是程序的入口点。
 * 负责解析命令行参数、调用加载逻辑以获取解决方案、语义模型和语法树。
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.Diagnostics;

namespace TerrariaTools
{
    /// <summary>
    /// 程序主入口类
    /// </summary>
    class Program
    {
        private readonly Load _loader = new Load();

        static async Task Main(string[] args)
        {
            var program = new Program();

            // 执行类重构逻辑
            await program.ExecuteClassRefactoring();

            // 执行方法重构逻辑
            await program.ExecuteMethodRefactoring();
        }

        /// <summary>
        /// 执行类重构功能：循环遍历解决方案中的所有文件，删除未引用的类。
        /// </summary>
        async Task ExecuteClassRefactoring()
        {
            string solutionPath = @"D:\lodes\TR\Backup\New1.27\TR\TerrariaServer.sln";
            bool anyFileChangedInPass;
            int passCount = 0;

            do
            {
                passCount++;
                anyFileChangedInPass = false;
                Console.WriteLine($"\n[信息] 正在启动第 {passCount} 轮类重构迭代...");

                using var workspace = await _loader.LoadSolutionAsync(solutionPath);
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
                var results = new ConcurrentBag<ClassRefactorer.RefactorResult>();

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
                        currentSolution = currentSolution.WithDocumentSyntaxRoot(res.DocumentId!, res.NewRoot!);
                        savedCount++;
                    }

                    if (workspace.TryApplyChanges(currentSolution))
                    {
                        Console.WriteLine($"[成功] 已保存 {savedCount} 个文件的类变更。");
                    }
                    else
                    {
                        foreach (var res in results.Where(r => r.AnyChanged && r.FilePath != null && r.NewRoot != null))
                        {
                            File.WriteAllText(res.FilePath!, res.NewRoot!.ToFullString());
                        }
                    }
                }

                Console.WriteLine($"\n[第 {passCount} 轮结束] 本轮耗时: {DateTime.Now - startTime:mm\\:ss}, 总计删除类: {totalDeleted}");
            } while (anyFileChangedInPass);

            Console.WriteLine($"\n[完成] 类重构全流程结束。总共执行了 {passCount} 轮迭代。");
        }

        /// <summary>
        /// 执行方法重构功能：循环遍历解决方案中的所有文件，直到没有查找到未引用的函数为止。
        /// </summary>
        async Task ExecuteMethodRefactoring()
        {
            string solutionPath = @"D:\lodes\TR\Backup\New1.27\TR\TerrariaServer.sln";
            bool anyFileChangedInPass;
            int passCount = 0;

            do
            {
                passCount++;
                anyFileChangedInPass = false;
                Console.WriteLine($"\n[信息] 正在启动第 {passCount} 轮重构迭代...");

                // 每轮迭代都重新加载解决方案，以确保获取最新的语义模型
                using var workspace = await _loader.LoadSolutionAsync(solutionPath);
                if (workspace == null) break;

                var solution = workspace.CurrentSolution;
                var refactorer = new MethodRefactorer(solution);

                var allDocuments = solution.Projects
                    .SelectMany(p => p.Documents)
                    .Where(d => d.FilePath != null && d.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($"[信息] 发现 {allDocuments.Count} 个待处理的 C# 文件。");

                int totalDeleted = 0;
                int totalPrivatized = 0;
                int totalBodyCleared = 0;
                int processedCount = 0;

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                var startTime = DateTime.Now;
                var results = new ConcurrentBag<MethodRefactorer.RefactorResult>();

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
                            Interlocked.Add(ref totalPrivatized, result.PrivatizedCount);
                            Interlocked.Add(ref totalBodyCleared, result.BodyClearedCount);
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
                        Console.WriteLine($"[{currentCount}/{allDocuments.Count}] 正在分析... 速度: {speed:F1} 文件/秒, 待处理变更: {totalDeleted + totalPrivatized + totalBodyCleared}");
                    }
                });

                if (anyFileChangedInPass)
                {
                    Console.WriteLine($"[信息] 正在将本轮变更保存到磁盘...");
                    var currentSolution = solution;
                    int savedCount = 0;
                    foreach (var res in results.Where(r => r.AnyChanged && r.DocumentId != null && r.NewRoot != null))
                    {
                        currentSolution = currentSolution.WithDocumentSyntaxRoot(res.DocumentId!, res.NewRoot!);
                        savedCount++;
                    }

                    if (workspace.TryApplyChanges(currentSolution))
                    {
                        Console.WriteLine($"[成功] 已保存 {savedCount} 个文件的变更。");
                    }
                    else
                    {
                        Console.WriteLine($"[警告] 无法通过 Workspace 保存变更，尝试回退到手动写入。");
                        // 备选方案：如果 TryApplyChanges 失败，手动写入（虽然通常不会失败）
                        foreach (var res in results.Where(r => r.AnyChanged && r.FilePath != null && r.NewRoot != null))
                        {
                            File.WriteAllText(res.FilePath!, res.NewRoot!.ToFullString());
                        }
                    }
                }

                Console.WriteLine($"\n[第 {passCount} 轮结束] 本轮耗时: {DateTime.Now - startTime:mm\\:ss}, 总计: 删除 {totalDeleted}, 私有化 {totalPrivatized}, 清空体 {totalBodyCleared}");
            } while (anyFileChangedInPass);

            Console.WriteLine($"\n[完成] 方法重构全流程结束。总共执行了 {passCount} 轮迭代。");
        }

        /// <summary>
        /// 高性能代码处理过程。
        /// 融合方案：Dataflow Pipeline (方案3) + 服务解耦 (方案2)
        /// </summary>
        async Task CodeProcess()
        {
            // 保留硬编码路径用于测试
            string solutionPath = @"D:\lodes\TR\Backup\New1.27\TR\TerrariaServer.sln";
            string targetFilePath = @"D:\lodes\TR\Backup\New1.27\TR\Terraria.GameInput\PlayerInput.cs";

            Console.WriteLine($"[信息] 正在加载解决方案: {solutionPath}");
            using var workspace = await _loader.LoadSolutionAsync(solutionPath);
            if (workspace == null) return;

            var solution = workspace.CurrentSolution;

            // 1. 分析目标符号
            var targetModel = await _loader.GetFileSemanticModelAsync(workspace, targetFilePath);
            if (targetModel == null) return;

            var symbolsToFind = ExtractSymbols(targetModel);
            Console.WriteLine($"[信息] 准备查找 {symbolsToFind.Count} 个符号的引用...");

            // 2. 构建 Pipeline (方案3核心：Dataflow)

            // 阶段 A: 查找引用并按文档分组
            var docReferences = new ConcurrentDictionary<DocumentId, List<(ISymbol, SyntaxAnnotation)>>();

            var findRefsBlock = new ActionBlock<(ISymbol symbol, SyntaxAnnotation annotation)>(async item =>
            {
                var references = await SymbolFinder.FindReferencesAsync(item.symbol, solution);
                foreach (var refSymbol in references)
                {
                    foreach (var location in refSymbol.Locations)
                    {
                        if (location.Document != null && !string.Equals(location.Document.FilePath, targetFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            docReferences.AddOrUpdate(
                                location.Document.Id,
                                new List<(ISymbol, SyntaxAnnotation)> { item },
                                (id, list) => { lock (list) { list.Add(item); } return list; }
                            );
                        }
                    }
                }
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount });

            foreach (var sym in symbolsToFind) findRefsBlock.Post(sym);
            findRefsBlock.Complete();
            await findRefsBlock.Completion;

            // 阶段 B: 并行处理文档转换
            var modifiedDocs = new ConcurrentDictionary<string, string>();
            var globalTrace = new RewritingTraceContext();

            var transformBlock = new ActionBlock<KeyValuePair<DocumentId, List<(ISymbol, SyntaxAnnotation)>>>(async entry =>
            {
                var document = solution.GetDocument(entry.Key);
                if (document == null) return;

                var model = await document.GetSemanticModelAsync();
                if (model == null) return;

                var annotatedRoot = await _loader.FindSymbolsReferencesAsync(model, entry.Value);
                if (annotatedRoot != null)
                {
                    // 使用支持诊断追踪的 RemoveParts 方法
                    var processedRoot = ExpressionProcessor.RemoveParts(
                        annotatedRoot,
                        node => node.GetAnnotations("ReferenceType").Any(),
                        model,
                        globalTrace);

                    if (processedRoot != null)
                    {
                        modifiedDocs[document.FilePath!] = processedRoot.ToFullString();
                    }
                }
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Environment.ProcessorCount });

            foreach (var entry in docReferences) transformBlock.Post(entry);
            transformBlock.Complete();
            await transformBlock.Completion;

            // 3. 批量写回磁盘
            await WriteFilesAsync(modifiedDocs);

            // 输出诊断报告
            var diagnostics = globalTrace.GetDiagnostics();
            if (diagnostics.Count > 0)
            {
                Console.WriteLine($"\n生成了 {diagnostics.Count} 条重写诊断信息:");
                foreach (var diag in diagnostics)
                {
                    Console.WriteLine(diag.ToString());
                }
            }
        }

        private List<(ISymbol symbol, SyntaxAnnotation annotation)> ExtractSymbols(SemanticModel model)
        {
            var symbols = new List<(ISymbol, SyntaxAnnotation)>();
            var annotations = new Dictionary<string, SyntaxAnnotation> {
                { "Property", new SyntaxAnnotation("ReferenceType", "PropertyReference") },
                { "Field", new SyntaxAnnotation("ReferenceType", "FieldReference") },
                { "Method", new SyntaxAnnotation("ReferenceType", "MethodReference") }
            };

            symbols.AddRange(_loader.GetPropertiesFromSemanticModel(model).Select(s => (s as ISymbol, annotations["Property"])));
            symbols.AddRange(_loader.GetFieldsFromSemanticModel(model).Select(s => (s as ISymbol, annotations["Field"])));
            symbols.AddRange(_loader.GetMethodsFromSemanticModel(model).Select(s => (s as ISymbol, annotations["Method"])));

            return symbols;
        }

        private async Task WriteFilesAsync(ConcurrentDictionary<string, string> files)
        {
            Console.WriteLine($"[信息] 正在写回 {files.Count} 个文件...");
            var tasks = files.Select(kvp => Task.Run(() => {
                try {
                    System.IO.File.WriteAllText(kvp.Key, kvp.Value, System.Text.Encoding.UTF8);
                } catch (Exception ex) {
                    Console.WriteLine($"[错误] {kvp.Key}: {ex.Message}");
                }
            }));
            await Task.WhenAll(tasks);
            Console.WriteLine("[完成] 处理结束。");
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