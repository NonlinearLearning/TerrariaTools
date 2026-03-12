using System;
using System.Collections.Generic;
using System.Linq;
using QuikGraph;
using QuikGraph.Algorithms.Search;

namespace TerrariaTools.Analysis
{
    /// <summary>
    /// 压缩稀疏行 (CSR) 图
    /// 一种高效的图存储格式，特别适合静态分析中的大规模稀疏图。
    /// 提供快速的邻接点查询和内存优化的存储。
    /// </summary>
    public class CompressedSparseRowGraph
    {
        /// <summary>
        /// 节点数量
        /// </summary>
        private readonly int _nodeCount;

        /// <summary>
        /// 内部使用的邻接图
        /// </summary>
        private readonly AdjacencyGraph<int, Edge<int>> _graph;

        /// <summary>
        /// 构造函数，基于邻接列表初始化图
        /// </summary>
        /// <param name="nodeCount">节点总数</param>
        /// <param name="adjacencyList">邻接列表</param>
        public CompressedSparseRowGraph(int nodeCount, IEnumerable<int>[] adjacencyList)
        {
            _nodeCount = nodeCount;
            _graph = new AdjacencyGraph<int, Edge<int>>(allowParallelEdges: false);

            _graph.AddVertexRange(Enumerable.Range(0, nodeCount));

            for (var i = 0; i < nodeCount; i++)
            {
                foreach (var neighbor in adjacencyList[i])
                {
                    if (neighbor >= 0 && neighbor < nodeCount)
                    {
                        _graph.AddEdge(new Edge<int>(i, neighbor));
                    }
                }
            }
        }

        /// <summary>
        /// 使用 QuikGraph BFS 计算一个或多个起始节点的可达性
        /// </summary>
        /// <param name="startNodes">起始节点集合</param>
        /// <returns>布尔数组，表示每个节点是否可达</returns>
        public bool[] ComputeReachability(IEnumerable<int> startNodes)
        {
            var reachable = new bool[_nodeCount];
            var distinctStarts = startNodes
                .Where(s => s >= 0 && s < _nodeCount)
                .Distinct()
                .ToList();

            foreach (var start in distinctStarts)
            {
                if (reachable[start])
                {
                    continue;
                }

                var bfs = new BreadthFirstSearchAlgorithm<int, Edge<int>>(_graph);
                bfs.DiscoverVertex += v => reachable[v] = true;
                bfs.Compute(start);
            }

            return reachable;
        }

        /// <summary>
        /// 获取指定节点的邻接节点
        /// </summary>
        /// <param name="node">节点索引</param>
        /// <returns>邻接节点索引迭代器</returns>
        public IEnumerable<int> GetNeighbors(int node)
        {
            if (node < 0 || node >= _nodeCount)
            {
                yield break;
            }

            if (_graph.TryGetOutEdges(node, out var edges))
            {
                foreach (var edge in edges)
                {
                    yield return edge.Target;
                }
            }
        }
    }
}
