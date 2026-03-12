using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 影子类生成器。
    /// 升级版：统一使用 AdvancedCodeAnalyzer 门面 (方案 A)，实现并行增量生成 (方案 D)。
    /// </summary>
    public class ShadowClassGenerator
    {
        private readonly Solution _solution;
        private readonly AdvancedCodeAnalyzer _analyzer;

        public ShadowClassGenerator(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
            _analyzer = new AdvancedCodeAnalyzer(solution);
        }

        /// <summary>
        /// 生成精简版项目源码。
        /// </summary>
        /// <param name="seedSymbol">分析起点符号</param>
        /// <param name="dynamicLogs">可选的动态运行日志（用于混合分析）</param>
        public async Task<Dictionary<string, string>> GenerateShadowSourceAsync(ISymbol seedSymbol, IEnumerable<string>? dynamicLogs = null)
        {
            // 1. 执行统一分析 (方案 A + E: 门面模式驱动)
            // 内部已集成了静态分析、动态日志加载、冲突域分析
            var analysisResult = await _analyzer.PerformFullAnalysisAsync(seedSymbol, dynamicLogs);
            var dependencyGraph = analysisResult.Graph;

            // 2. 获取必要符号集 (除了图中可达的，还有一些特殊规则需要的符号)
            var necessarySymbols = await CollectSpecialNecessarySymbolsAsync();

            var result = new Dictionary<string, string>();

            // 3. 并行增量生成 (方案 D)
            // 识别受影响的文档
            var affectedDocuments = new HashSet<DocumentId>();
            foreach (var node in dependencyGraph.AllNodes)
            {
                if (node.IsStaticallyReached || node.IsDynamicallyReached)
                {
                    foreach (var location in node.Symbol.Locations.Where(l => l.IsInSource))
                    {
                        var doc = _solution.GetDocument(location.SourceTree);
                        if (doc != null) affectedDocuments.Add(doc.Id);
                    }
                }
            }

            // 并行重写每个文档
            var tasks = affectedDocuments.Select(async docId =>
            {
                var doc = _solution.GetDocument(docId)!;
                var semanticModel = await doc.GetSemanticModelAsync();
                var root = await doc.GetSyntaxRootAsync();

                if (semanticModel == null || root == null) return;

                // 方案 A/C：使用升级后的 Rewriter
                var rewriter = new MemberSlicingRewriter(
                    semanticModel, 
                    dependencyGraph, 
                    necessarySymbols, 
                    analysisResult.ConflictIdentifiers);

                var newRoot = rewriter.Visit(root);

                if (newRoot != null)
                {
                    // 格式化代码
                    var formattedNode = Microsoft.CodeAnalysis.Formatting.Formatter.Format(newRoot, _solution.Workspace);
                    
                    lock (result)
                    {
                        result[doc.FilePath ?? doc.Name] = formattedNode.ToFullString();
                    }
                }
            });

            await Task.WhenAll(tasks);

            return result;
        }

        private async Task<HashSet<ISymbol>> CollectSpecialNecessarySymbolsAsync()
        {
            var necessary = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            
            foreach (var project in _solution.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                // 强制保留程序集特性
                necessary.Add(compilation.Assembly);

                // 强制保留所有带有初始化器的静态字段（自发运行逻辑）
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var root = await tree.GetRootAsync();
                    var model = compilation.GetSemanticModel(tree);

                    var staticFields = root.DescendantNodes()
                        .OfType<FieldDeclarationSyntax>()
                        .Where(f => f.Modifiers.Any(SyntaxKind.StaticKeyword))
                        .SelectMany(f => f.Declaration.Variables)
                        .Where(v => v.Initializer != null);

                    foreach (var fieldVar in staticFields)
                    {
                        var symbol = model.GetDeclaredSymbol(fieldVar);
                        if (symbol != null) necessary.Add(symbol);
                    }
                }
            }

            return necessary;
        }
    }
}
