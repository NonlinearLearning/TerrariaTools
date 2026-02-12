using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 影子类生成器。
    /// 负责协调依赖分析和成员切片，生成最终的“精简版”源码。
    /// </summary>
    public class ShadowClassGenerator
    {
        private readonly Solution _solution;
        private readonly CodeDependencyAnalyzer _analyzer;

        public ShadowClassGenerator(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
            _analyzer = new CodeDependencyAnalyzer(solution);
        }

        /// <summary>
        /// 生成针对特定入口点（如 GetData 方法）的精简版项目源码。
        /// </summary>
        /// <param name="seedSymbol">分析起点符号</param>
        /// <returns>返回包含精简代码的文档集合（路径 -> 内容）</returns>
        public async Task<Dictionary<string, string>> GenerateShadowSourceAsync(ISymbol seedSymbol)
        {
            // 1. 执行递归依赖分析，获取所有必要的符号
            await _analyzer.AnalyzeRecursiveAsync(seedSymbol);
            var necessarySymbols = _analyzer.Graph.AllNodes.Select(n => n.Symbol).ToHashSet(SymbolEqualityComparer.Default);
            
            // 种子本身也是必要的
            necessarySymbols.Add(seedSymbol);

            var result = new Dictionary<string, string>();

            // 2. 识别受影响的文档（包含必要符号的文档）
            var affectedDocuments = new HashSet<DocumentId>();
            foreach (var symbol in necessarySymbols)
            {
                foreach (var location in symbol.Locations)
                {
                    if (location.IsInSource)
                    {
                        var doc = _solution.GetDocument(location.SourceTree);
                        if (doc != null) affectedDocuments.Add(doc.Id);
                    }
                }
            }

            // 3. 对每个受影响的文档执行成员级切片
            foreach (var docId in affectedDocuments)
            {
                var doc = _solution.GetDocument(docId)!;
                var semanticModel = await doc.GetSemanticModelAsync();
                var root = await doc.GetSyntaxRootAsync();

                if (semanticModel == null || root == null) continue;

                // 执行重写
                var rewriter = new MemberSlicingRewriter(semanticModel, necessarySymbols);
                var newRoot = rewriter.Visit(root);

                if (newRoot != null)
                {
                    // 格式化代码
                    var formattedNode = Microsoft.CodeAnalysis.Formatting.Formatter.Format(newRoot, _solution.Workspace);
                    result[doc.FilePath ?? doc.Name] = formattedNode.ToFullString();
                }
            }

            return result;
        }
    }
}
