using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 基于 Roslyn 的递归依赖分析引擎。
    /// 实现了“重写思路.txt”中的静态分析和递归依赖图构建。
    /// </summary>
    public class CodeDependencyAnalyzer
    {
        private readonly Solution _solution;
        private readonly DependencyGraph _graph = new DependencyGraph();
        private readonly HashSet<ISymbol> _processedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        public CodeDependencyAnalyzer(Solution solution)
        {
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
        }

        public DependencyGraph Graph => _graph;

        /// <summary>
        /// 从给定的种子符号开始递归分析依赖。
        /// </summary>
        public async Task AnalyzeRecursiveAsync(ISymbol seedSymbol)
        {
            if (seedSymbol == null || _processedSymbols.Contains(seedSymbol))
                return;

            _processedSymbols.Add(seedSymbol);
            var node = _graph.GetOrAddNode(seedSymbol);

            // 1. 获取符号的定义位置
            var reference = seedSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (reference == null) return; // 外部库符号暂不深入分析

            var syntaxNode = await reference.GetSyntaxAsync();
            var semanticModel = await _solution.GetDocument(syntaxNode.SyntaxTree)!.GetSemanticModelAsync();

            if (semanticModel == null) return;

            // 2. 遍历语法树查找所有引用的符号
            var descendantNodes = syntaxNode.DescendantNodes();
            foreach (var descendant in descendantNodes)
            {
                ISymbol? referencedSymbol = null;

                if (descendant is IdentifierNameSyntax identifier)
                {
                    referencedSymbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                }
                else if (descendant is MemberAccessExpressionSyntax memberAccess)
                {
                    referencedSymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
                }
                else if (descendant is ObjectCreationExpressionSyntax objectCreation)
                {
                    referencedSymbol = semanticModel.GetSymbolInfo(objectCreation).Symbol;
                }

                if (referencedSymbol != null && IsInterestingSymbol(referencedSymbol))
                {
                    // 排除自身引用
                    if (!SymbolEqualityComparer.Default.Equals(referencedSymbol, seedSymbol))
                    {
                        _graph.AddDependency(seedSymbol, referencedSymbol);
                        // 递归分析
                        await AnalyzeRecursiveAsync(referencedSymbol);
                    }
                }
            }
        }

        /// <summary>
        /// 过滤掉不感兴趣的符号（如命名空间、内置基本类型等）。
        /// </summary>
        private bool IsInterestingSymbol(ISymbol symbol)
        {
            if (symbol is INamespaceSymbol) return false;
            
            // 仅关注当前解决方案中定义的符号
            return symbol.DeclaringSyntaxReferences.Any();
        }

        /// <summary>
        /// 执行后向切片 (Backward Slicing) 的简化版本：
        /// 查找所有直接或间接依赖于给定目标的符号。
        /// </summary>
        public IEnumerable<SymbolNode> GetBackwardSlice(ISymbol targetSymbol)
        {
            var targetNode = _graph.GetOrAddNode(targetSymbol);
            var result = new HashSet<SymbolNode>(_graph.NodeComparer);
            
            // 在图中寻找所有能到达 targetNode 的节点
            foreach (var node in _graph.AllNodes)
            {
                if (CanReach(node, targetNode, new HashSet<SymbolNode>(_graph.NodeComparer)))
                {
                    result.Add(node);
                }
            }
            return result;
        }

        private bool CanReach(SymbolNode current, SymbolNode target, HashSet<SymbolNode> visited)
        {
            if (current == target) return true;
            if (!visited.Add(current)) return false;

            foreach (var dep in current.Dependencies)
            {
                if (CanReach(dep, target, visited)) return true;
            }
            return false;
        }
    }
}
