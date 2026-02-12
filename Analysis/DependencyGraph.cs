using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 表示依赖图中的一个符号节点。
    /// </summary>
    public class SymbolNode
    {
        public ISymbol Symbol { get; }
        public string FullName => Symbol.ToDisplayString();
        public HashSet<SymbolNode> Dependencies { get; }

        public SymbolNode(ISymbol symbol, IEqualityComparer<SymbolNode> nodeComparer)
        {
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
            Dependencies = new HashSet<SymbolNode>(nodeComparer);
        }

        public override int GetHashCode() => SymbolEqualityComparer.Default.GetHashCode(Symbol);
        public override bool Equals(object? obj) => obj is SymbolNode other && SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol);
    }

    public class SymbolNodeComparer : IEqualityComparer<SymbolNode>
    {
        private readonly IEqualityComparer<ISymbol> _symbolComparer;
        public SymbolNodeComparer(IEqualityComparer<ISymbol> symbolComparer) => _symbolComparer = symbolComparer;

        public bool Equals(SymbolNode? x, SymbolNode? y) => _symbolComparer.Equals(x?.Symbol, y?.Symbol);
        public int GetHashCode(SymbolNode obj) => _symbolComparer.GetHashCode(obj.Symbol);
    }

    /// <summary>
    /// 依赖图数据结构，包含核心算法实现（Tarjan, 拓扑排序, DFS）。
    /// </summary>
    public class DependencyGraph
    {
        private readonly Dictionary<ISymbol, SymbolNode> _nodes;
        private readonly SymbolNodeComparer _nodeComparer;
        private readonly IEqualityComparer<ISymbol> _symbolComparer;

        public IEqualityComparer<SymbolNode> NodeComparer => _nodeComparer;

        public DependencyGraph(IEqualityComparer<ISymbol>? symbolComparer = null)
        {
            _symbolComparer = symbolComparer ?? SymbolEqualityComparer.Default;
            _nodeComparer = new SymbolNodeComparer(_symbolComparer);
            _nodes = new Dictionary<ISymbol, SymbolNode>(_symbolComparer);
        }

        public SymbolNode GetOrAddNode(ISymbol symbol)
        {
            if (!_nodes.TryGetValue(symbol, out var node))
            {
                node = new SymbolNode(symbol, _nodeComparer);
                _nodes[symbol] = node;
            }
            return node;
        }

        public void AddDependency(ISymbol from, ISymbol to)
        {
            var fromNode = GetOrAddNode(from);
            var toNode = GetOrAddNode(to);
            fromNode.Dependencies.Add(toNode);
        }

        public IEnumerable<SymbolNode> AllNodes => _nodes.Values;

        /// <summary>
        /// 深度优先搜索 (DFS) 遍历。
        /// </summary>
        public IEnumerable<SymbolNode> DFS(SymbolNode startNode)
        {
            var visited = new HashSet<SymbolNode>(_nodeComparer);
            var stack = new Stack<SymbolNode>();
            stack.Push(startNode);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (visited.Add(current))
                {
                    yield return current;
                    foreach (var dep in current.Dependencies)
                    {
                        if (!visited.Contains(dep))
                            stack.Push(dep);
                    }
                }
            }
        }

        /// <summary>
        /// 拓扑排序。
        /// 如果存在环，则抛出异常（应先使用 Tarjan 处理 SCC）。
        /// </summary>
        public List<SymbolNode> TopologicalSort()
        {
            var result = new List<SymbolNode>();
            var visited = new HashSet<SymbolNode>(_nodeComparer);
            var tempVisited = new HashSet<SymbolNode>(_nodeComparer);

            foreach (var node in _nodes.Values)
            {
                if (!visited.Contains(node))
                    Visit(node);
            }

            void Visit(SymbolNode node)
            {
                if (tempVisited.Contains(node))
                    throw new InvalidOperationException("图中存在环，无法进行纯粹的拓扑排序。请先识别强连通组件 (SCC)。");
                
                if (!visited.Contains(node))
                {
                    tempVisited.Add(node);
                    foreach (var dep in node.Dependencies)
                        Visit(dep);
                    
                    tempVisited.Remove(node);
                    visited.Add(node);
                    result.Insert(0, node);
                }
            }

            return result;
        }

        /// <summary>
        /// 使用 Tarjan 算法查找强连通组件 (SCC)。
        /// 用于识别循环依赖。
        /// </summary>
        public List<List<SymbolNode>> FindSCCs()
        {
            int index = 0;
            var stack = new Stack<SymbolNode>();
            var indices = new Dictionary<SymbolNode, int>(_nodeComparer);
            var lowlink = new Dictionary<SymbolNode, int>(_nodeComparer);
            var onStack = new HashSet<SymbolNode>(_nodeComparer);
            var sccs = new List<List<SymbolNode>>();

            foreach (var node in _nodes.Values)
            {
                if (!indices.ContainsKey(node))
                    StrongConnect(node);
            }

            void StrongConnect(SymbolNode v)
            {
                indices[v] = lowlink[v] = index++;
                stack.Push(v);
                onStack.Add(v);

                foreach (var w in v.Dependencies)
                {
                    if (!indices.ContainsKey(w))
                    {
                        StrongConnect(w);
                        lowlink[v] = Math.Min(lowlink[v], lowlink[w]);
                    }
                    else if (onStack.Contains(w))
                    {
                        lowlink[v] = Math.Min(lowlink[v], indices[w]);
                    }
                }

                if (lowlink[v] == indices[v])
                {
                    var scc = new List<SymbolNode>();
                    SymbolNode w;
                    do
                    {
                        w = stack.Pop();
                        onStack.Remove(w);
                        scc.Add(w);
                    } while (w != v);
                    sccs.Add(scc);
                }
            }

            return sccs;
        }
    }
}
