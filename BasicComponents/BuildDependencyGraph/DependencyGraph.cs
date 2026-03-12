using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using QuikGraph;
using QuikGraph.Algorithms;
using QuikGraph.Algorithms.Search;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 表示依赖图中的符号节点
    /// </summary>
    public class SymbolNode
    {
        /// <summary>
        /// 关联的符号对象
        /// </summary>
        public ISymbol Symbol { get; }

        /// <summary>
        /// 符号的全名
        /// </summary>
        public string FullName => Symbol.ToDisplayString();

        /// <summary>
        /// 获取或设置该节点是否通过动态分析可达
        /// </summary>
        public bool IsDynamicallyReached { get; set; }

        /// <summary>
        /// 获取或设置该节点是否通过静态分析可达
        /// </summary>
        public bool IsStaticallyReached { get; set; }

        /// <summary>
        /// 节点的直接依赖关系集合
        /// </summary>
        public Dictionary<SymbolNode, bool> Dependencies { get; } = new Dictionary<SymbolNode, bool>();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="symbol">关联的符号</param>
        public SymbolNode(ISymbol symbol)
        {
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        /// <summary>
        /// 获取当前节点状态（返回自身）
        /// </summary>
        public SymbolNode Status => this;

        /// <summary>
        /// 标记节点为静态可达
        /// </summary>
        public void MarkStaticallyReached() => IsStaticallyReached = true;

        /// <summary>
        /// 获取哈希值，基于符号的等价比较器
        /// </summary>
        /// <returns>哈希值</returns>
        public override int GetHashCode() => SymbolEqualityComparer.Default.GetHashCode(Symbol);

        /// <summary>
        /// 判断两个节点是否相等
        /// </summary>
        /// <param name="obj">比较对象</param>
        /// <returns>是否相等</returns>
        public override bool Equals(object? obj)
            => obj is SymbolNode other && SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol);

        /// <summary>
        /// 返回符号的全名
        /// </summary>
        /// <returns>全名字符串</returns>
        public override string ToString() => FullName;
    }

    /// <summary>
    /// 基于邻接表实现的动态有向图
    /// 封装了代码符号之间的依赖关系图。
    /// 使用 QuikGraph 库提供图的存储、遍历和算法支持。
    /// </summary>
    public class DependencyGraph
    {
        /// <summary>
        /// 内部使用的双向图
        /// </summary>
        private readonly BidirectionalGraph<SymbolNode, Edge<SymbolNode>> _graph;

        /// <summary>
        /// 符号到节点的映射字典
        /// </summary>
        private readonly Dictionary<ISymbol, SymbolNode> _symbolToNode;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="comparer">符号等价比较器</param>
        public DependencyGraph(IEqualityComparer<ISymbol>? comparer = null)
        {
            _graph = new BidirectionalGraph<SymbolNode, Edge<SymbolNode>>();
            _symbolToNode = new Dictionary<ISymbol, SymbolNode>(comparer ?? SymbolEqualityComparer.Default);
        }

        /// <summary>
        /// 获取或添加符号对应的节点
        /// </summary>
        /// <param name="symbol">符号</param>
        /// <returns>依赖图节点</returns>
        public SymbolNode GetOrAddNode(ISymbol symbol)
        {
            if (_symbolToNode.TryGetValue(symbol, out var node))
            {
                return node;
            }

            node = new SymbolNode(symbol);
            _symbolToNode[symbol] = node;
            _graph.AddVertex(node);
            return node;
        }

        /// <summary>
        /// 获取符号对应的节点，如果不存在则返回 null
        /// </summary>
        /// <param name="symbol">符号</param>
        /// <returns>依赖图节点或 null</returns>
        public SymbolNode? GetNode(ISymbol symbol)
        {
            _symbolToNode.TryGetValue(symbol, out var node);
            return node;
        }

        /// <summary>
        /// 获取符号的节点状态，如果不存在则返回一个新节点
        /// </summary>
        /// <param name="symbol">符号</param>
        /// <returns>依赖图节点</returns>
        public SymbolNode GetNodeStatus(ISymbol symbol)
        {
            if (_symbolToNode.TryGetValue(symbol, out var node))
            {
                return node;
            }

            return new SymbolNode(symbol);
        }

        /// <summary>
        /// 添加符号间的依赖关系（边）
        /// </summary>
        /// <param name="from">来源符号</param>
        /// <param name="to">目标符号</param>
        public void AddDependency(ISymbol from, ISymbol to)
        {
            var fromNode = GetOrAddNode(from);
            var toNode = GetOrAddNode(to);
            _graph.AddEdge(new Edge<SymbolNode>(fromNode, toNode));
            fromNode.Dependencies[toNode] = true;
        }

        /// <summary>
        /// 获取图中所有节点
        /// </summary>
        public IEnumerable<SymbolNode> AllNodes => _graph.Vertices;

        /// <summary>
        /// 获取图中所有节点
        /// </summary>
        /// <returns>节点集合</returns>
        public IEnumerable<SymbolNode> GetAllNodes() => AllNodes;

        /// <summary>
        /// 获取底层图对象
        /// </summary>
        public BidirectionalGraph<SymbolNode, Edge<SymbolNode>> UnderlyingGraph => _graph;

        /// <summary>
        /// 深度优先遍历 (DFS)
        /// </summary>
        /// <param name="startNode">起始节点</param>
        /// <param name="reverse">是否进行反向遍历</param>
        /// <returns>遍历到的节点集合</returns>
        public IEnumerable<SymbolNode> DFS(SymbolNode startNode, bool reverse = false)
        {
            if (reverse)
            {
                // 构建临时反向图，然后复用 QuikGraph DFS
                var reversedGraph = new AdjacencyGraph<SymbolNode, Edge<SymbolNode>>();
                reversedGraph.AddVertexRange(_graph.Vertices);
                foreach (var edge in _graph.Edges)
                {
                    reversedGraph.AddEdge(new Edge<SymbolNode>(edge.Target, edge.Source));
                }

                foreach (var node in RunDepthFirst(reversedGraph, startNode))
                {
                    yield return node;
                }
                yield break;
            }

            foreach (var node in RunDepthFirst(_graph, startNode))
            {
                yield return node;
            }
        }

        /// <summary>
        /// 执行深度优先搜索算法
        /// </summary>
        /// <param name="graph">图对象</param>
        /// <param name="startNode">起始节点</param>
        /// <returns>搜索结果节点列表</returns>
        private static IEnumerable<SymbolNode> RunDepthFirst(IVertexListGraph<SymbolNode, Edge<SymbolNode>> graph, SymbolNode startNode)
        {
            var dfs = new DepthFirstSearchAlgorithm<SymbolNode, Edge<SymbolNode>>(graph);
            var result = new List<SymbolNode>();
            dfs.DiscoverVertex += result.Add;
            dfs.Compute(startNode);
            foreach (var node in result)
            {
                yield return node;
            }
        }

        /// <summary>
        /// 拓扑排序
        /// </summary>
        /// <returns>排序后的节点列表</returns>
        /// <exception cref="InvalidOperationException">当图中包含环路时抛出</exception>
        public List<SymbolNode> TopologicalSort()
        {
            try
            {
                return _graph.TopologicalSort().ToList();
            }
            catch (NonAcyclicGraphException)
            {
                throw new InvalidOperationException("图中包含环路，无法进行拓扑排序。");
            }
        }

        /// <summary>
        /// 寻找强连通分量 (SCCs)
        /// </summary>
        /// <returns>强连通分量列表，每个分量是一个节点列表</returns>
        public List<List<SymbolNode>> FindSCCs()
        {
            var components = new Dictionary<SymbolNode, int>();
            var count = _graph.StronglyConnectedComponents(components);

            var sccs = new List<List<SymbolNode>>(count);
            for (var i = 0; i < count; i++)
            {
                sccs.Add(new List<SymbolNode>());
            }

            foreach (var kvp in components)
            {
                sccs[kvp.Value].Add(kvp.Key);
            }

            return sccs;
        }
    }
}
