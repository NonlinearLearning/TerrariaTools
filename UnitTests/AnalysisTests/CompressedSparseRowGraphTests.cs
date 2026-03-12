using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.Analysis;
using Xunit;

namespace TerrariaTools.UnitTests.AnalysisTests
{
    public class CompressedSparseRowGraphTests
    {
        [Fact]
        public void ComputeReachability_ShouldFindAllReachableNodes()
        {
            // 构造一个简单的图:
            // 0 -> 1 -> 2
            // 0 -> 3
            // 4 (孤立)

            int nodeCount = 5;
            var adjacencyList = new List<int>[nodeCount];
            for (int i = 0; i < nodeCount; i++) adjacencyList[i] = new List<int>();

            adjacencyList[0].Add(1);
            adjacencyList[0].Add(3);
            adjacencyList[1].Add(2);

            var csr = new CompressedSparseRowGraph(nodeCount, adjacencyList);

            // Test 1: 从 0 出发
            var reachable = csr.ComputeReachability([0]);
            Assert.True(reachable[0]);
            Assert.True(reachable[1]);
            Assert.True(reachable[2]);
            Assert.True(reachable[3]);
            Assert.False(reachable[4]);

            // Test 2: 从 1 出发
            reachable = csr.ComputeReachability([1]);
            Assert.False(reachable[0]);
            Assert.True(reachable[1]);
            Assert.True(reachable[2]);
            Assert.False(reachable[3]);
            Assert.False(reachable[4]);
        }

        [Fact]
        public void GetNeighbors_ShouldReturnCorrectNeighbors()
        {
            int nodeCount = 3;
            var adjacencyList = new List<int>[nodeCount];
            for (int i = 0; i < nodeCount; i++) adjacencyList[i] = new List<int>();

            adjacencyList[0].Add(1);
            adjacencyList[0].Add(2);

            var csr = new CompressedSparseRowGraph(nodeCount, adjacencyList);

            var neighbors = csr.GetNeighbors(0);
            Assert.Equal(2, neighbors.Count());
            Assert.Contains(1, neighbors);
            Assert.Contains(2, neighbors);

            neighbors = csr.GetNeighbors(1);
            Assert.Empty(neighbors);
        }

        [Fact]
        public void LargeScale_Reachability_PerformanceTest()
        {
            // 简单的压力测试，验证 CSR 处理 10万节点的能力
            int nodeCount = 100000;
            var adjacencyList = new List<int>[nodeCount];
            for (int i = 0; i < nodeCount; i++) adjacencyList[i] = new List<int>();

            // 构建一条长链: 0 -> 1 -> 2 ... -> 99999
            for (int i = 0; i < nodeCount - 1; i++)
            {
                adjacencyList[i].Add(i + 1);
            }

            var csr = new CompressedSparseRowGraph(nodeCount, adjacencyList);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var reachable = csr.ComputeReachability([0]);
            sw.Stop();

            // 确保最后一个节点可达
            Assert.True(reachable[nodeCount - 1]);

            // 10万节点的简单遍历应该在 50ms 内完成 (实际通常 < 10ms)
            Assert.True(sw.ElapsedMilliseconds < 200, $"Traversal took too long: {sw.ElapsedMilliseconds}ms");
        }
    }
}
