using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerrariaTools.Analysis.Dome
{
    /// <summary>
    /// 引用分析器，用于跨文件/跨项目分析符号的引用情况。
    /// 封装了 SymbolFinder 的核心逻辑，提供简单的引用计数和判定接口。
    /// </summary>
    public static class ReferenceAnalyzer
    {
        /// <summary>
        /// 异步查找指定符号的所有引用。
        /// </summary>
        /// <param name="symbol">要查找的符号。</param>
        /// <param name="solution">包含符号定义的解决方案。</param>
        /// <returns>引用的位置列表。</returns>
        public static async Task<IEnumerable<ReferenceLocation>> FindReferencesAsync(ISymbol symbol, Solution solution)
        {
            if (symbol == null || solution == null) return Enumerable.Empty<ReferenceLocation>();

            var references = await SymbolFinder.FindReferencesAsync(symbol, solution);
            return references.SelectMany(r => r.Locations);
        }

        /// <summary>
        /// 检查指定符号是否被引用。
        /// </summary>
        /// <param name="symbol">要检查的符号。</param>
        /// <param name="solution">包含符号定义的解决方案。</param>
        /// <param name="documents">可选：限制搜索范围为这些文档。</param>
        /// <returns>如果存在至少一个外部引用（非定义处），则返回 true。</returns>
        public static async Task<bool> HasReferencesAsync(ISymbol symbol, Solution solution, IReadOnlyList<Document> documents = null)
        {
            if (symbol == null || solution == null) return false;

            // 如果指定了文档范围，则仅搜索这些文档
            if (documents != null)
            {
                var references = await SymbolFinder.FindReferencesAsync(symbol, solution, documents.ToImmutableHashSet());
                return references.Any(r => r.Locations.Any());
            }

            // 否则全量搜索，并尝试尽早停止（Roslyn 本身不直接支持提前停止，但获取所有引用后判断 Any 已经是标准做法）
            var allReferences = await SymbolFinder.FindReferencesAsync(symbol, solution);
            return allReferences.Any(r => r.Locations.Any());
        }

        /// <summary>
        /// 获取符号的引用计数。
        /// </summary>
        public static async Task<int> GetReferenceCountAsync(ISymbol symbol, Solution solution)
        {
            var locations = await FindReferencesAsync(symbol, solution);
            return locations.Count();
        }
    }
}
