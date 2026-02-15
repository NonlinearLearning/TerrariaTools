using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;

namespace TerrariaTools.DynamicAnalysis
{
#pragma warning disable CS8602, CS8603, CS8714
    public class StaticCallChainAnalyzer
    {
        private readonly string _solutionPath;
        private readonly Load _loader;
        private readonly SymbolDisplayFormat _displayFormat;
        private readonly ConcurrentDictionary<string, IMethodSymbol> _methodSymbolCache = new ConcurrentDictionary<string, IMethodSymbol>();
        private readonly ConcurrentDictionary<string, MethodDeclarationSyntax> _methodSyntaxCache = new ConcurrentDictionary<string, MethodDeclarationSyntax>();
        private readonly ConcurrentDictionary<string, SemanticModel> _semanticModelCache = new ConcurrentDictionary<string, SemanticModel>();
        private readonly ConcurrentHashSet<string> _allFoundFunctions = new ConcurrentHashSet<string>();

        public StaticCallChainAnalyzer(string solutionPath, Load loader)
        {
            // 设置控制台编码为 UTF-8 以支持中文显示
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            _solutionPath = solutionPath;
            _loader = loader;
            _displayFormat = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted
            );
        }

        public async Task ExecuteAnalysisAsync(string logPath)
        {
            Console.WriteLine($"[信息] 正在加载解决方案: {_solutionPath}");
            using var workspace = await _loader.LoadSolutionAsync(_solutionPath);
            if (workspace == null) return;
            await AnalyzeSolutionAsync(workspace.CurrentSolution, logPath);
        }

        public async Task AnalyzeSolutionAsync(Solution solution, string logPath)
        {
            // 1. 预扫描整个项目，建立 FullName -> Symbol 的索引
            Console.WriteLine("[信息] 正在扫描项目建立语义索引...");
            await BuildMethodIndexAsync(solution);

            // 2. 读取日志文件中的初始函数
            var seedFunctions = ReadSeedFunctions(logPath);
            Console.WriteLine($"[信息] 从日志中读取到 {seedFunctions.Count} 个起始函数。");

            // 3. 递归分析调用链
            Console.WriteLine("[信息] 开始递归分析静态调用链 (并行模式)...");

            // 使用并行处理初始种子，提高吞吐量
            Parallel.ForEach(seedFunctions, f =>
            {
                AnalyzeFunctionRecursive(f);
            });

            // 4. 保存结果
            string outputPath = Path.Combine(Environment.CurrentDirectory, "analyzed_functions.txt");
            File.WriteAllLines(outputPath, _allFoundFunctions.OrderBy(f => f));
            Console.WriteLine($"[完成] 分析结束。共找到 {_allFoundFunctions.Count} 个唯一函数。");
            Console.WriteLine($"[信息] 结果已保存至: {outputPath}");
        }

        private async Task BuildMethodIndexAsync(Solution solution)
        {
            var projects = solution.Projects.ToList();
            await Task.WhenAll(projects.Select(async project =>
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) return;

                foreach (var document in project.Documents)
                {
                    if (document.FilePath == null || !document.FilePath.EndsWith(".cs")) continue;

                    var root = await document.GetSyntaxRootAsync();
                    var model = compilation.GetSemanticModel(await document.GetSyntaxTreeAsync());
                    if (root == null || model == null) continue;

                    // 缓存语义模型，以文件路径为键
                    _semanticModelCache[document.FilePath] = model;

                    // 扫描方法
                    var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                    foreach (var method in methods)
                    {
                        var symbol = model.GetDeclaredSymbol(method);
                        if (symbol != null)
                        {
                            string fullName = symbol.ToDisplayString(_displayFormat);
                            _methodSymbolCache[fullName] = symbol;
                            _methodSyntaxCache[fullName] = method;
                        }
                    }

                    // 扫描属性访问器 (get/set)
                    var accessors = root.DescendantNodes().OfType<AccessorDeclarationSyntax>();
                    foreach (var accessor in accessors)
                    {
                        var symbol = model.GetDeclaredSymbol(accessor);
                        if (symbol != null)
                        {
                            string fullName = symbol.ToDisplayString(_displayFormat);
                            _methodSymbolCache[fullName] = symbol;
                            // 注意：这里我们不缓存 AccessorDeclarationSyntax 到 _methodSyntaxCache，
                            // 因为目前 AnalyzeFunctionRecursive 主要是基于 MethodDeclarationSyntax。
                            // 如果需要深入分析属性内部调用，可以扩展。
                        }
                    }
                }
            }));
        }

        private HashSet<string> ReadSeedFunctions(string logPath)
        {
            var seeds = new HashSet<string>();
            if (!File.Exists(logPath)) return seeds;

            try
            {
                var lines = File.ReadAllLines(logPath);
                foreach (var line in lines)
                {
                    if (line.Contains("[ENTER]"))
                    {
                        var parts = line.Split(new[] { "[ENTER] " }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 1)
                        {
                            seeds.Add(parts[1].Trim());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 读取日志失败: {ex.Message}");
            }
            return seeds;
        }

        private void AnalyzeFunctionRecursive(string fullName)
        {
            if (string.IsNullOrEmpty(fullName) || !_allFoundFunctions.Add(fullName))
            {
                return;
            }

            if (!_methodSyntaxCache.TryGetValue(fullName, out var methodSyntax))
            {
                return;
            }

            // 获取该方法所在文档的语义模型
            var model = _semanticModelCache[methodSyntax.SyntaxTree.FilePath];
            if (model == null) return;

            // 查找方法体中调用的所有函数、属性、构造函数
            var nodes = methodSyntax.DescendantNodes();

            var calledFunctions = new HashSet<string>();

            foreach (var node in nodes)
            {
                if (node is ExpressionSyntax expression)
                {
                    var symbol = model.GetSymbolInfo(expression).Symbol;
                    if (symbol is IMethodSymbol methodSymbol)
                    {
                        calledFunctions.Add(methodSymbol.ToDisplayString(_displayFormat));
                    }
                    else if (symbol is IPropertySymbol propertySymbol)
                    {
                        if (propertySymbol.GetMethod != null)
                            calledFunctions.Add(propertySymbol.GetMethod.ToDisplayString(_displayFormat));
                        if (propertySymbol.SetMethod != null)
                            calledFunctions.Add(propertySymbol.SetMethod.ToDisplayString(_displayFormat));
                    }
                }
            }

            // 递归分析找到的每一个函数
            foreach (var calledFunc in calledFunctions)
            {
                AnalyzeFunctionRecursive(calledFunc);
            }
        }
    }

    /// <summary>
    /// 简单的线程安全 HashSet 包装器
    /// </summary>
    public class ConcurrentHashSet<T> : IEnumerable<T> where T : notnull
    {
        private readonly ConcurrentDictionary<T, byte> _dict = new ConcurrentDictionary<T, byte>();

        public bool Add(T item) => _dict.TryAdd(item, 0);
        public int Count => _dict.Count;

        public IEnumerator<T> GetEnumerator() => _dict.Keys.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
