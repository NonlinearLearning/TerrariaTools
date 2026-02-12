using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using TerrariaTools.Analysis;
using Microsoft.CodeAnalysis;
using Moq;

namespace TerrariaTools.UnitTests.Analysis
{
    public class DependencyAnalysisTests
    {
        // 简单的符号比较器，用于测试图算法，不依赖 Roslyn 的 SymbolEqualityComparer
        private class TestSymbolComparer : IEqualityComparer<ISymbol>
        {
            public bool Equals(ISymbol? x, ISymbol? y) => x?.Name == y?.Name;
            public int GetHashCode(ISymbol obj) => obj.Name?.GetHashCode() ?? 0;
        }

        private ISymbol CreateSymbol(string name)
        {
            var mock = new Mock<ISymbol>();
            mock.Setup(s => s.Name).Returns(name);
            mock.Setup(s => s.ToDisplayString(It.IsAny<SymbolDisplayFormat>())).Returns(name);
            return mock.Object;
        }

        [Fact]
        public void DependencyGraph_TarjanSCC_ShouldIdentifyCycles()
        {
            // Arrange
            var graph = new DependencyGraph(new TestSymbolComparer());
            var s1 = CreateSymbol("S1");
            var s2 = CreateSymbol("S2");
            var s3 = CreateSymbol("S3");
            var s4 = CreateSymbol("S4");

            // 构建环: S1 -> S2 -> S3 -> S1
            graph.AddDependency(s1, s2);
            graph.AddDependency(s2, s3);
            graph.AddDependency(s3, s1);
            // 外部节点: S3 -> S4
            graph.AddDependency(s3, s4);

            // Act
            var sccs = graph.FindSCCs();

            // Assert
            // 应该有 2 个 SCC: {S1, S2, S3} 和 {S4}
            Assert.Equal(2, sccs.Count);
            var cycleSCC = sccs.First(s => s.Count == 3);
            Assert.Contains(cycleSCC, n => n.Symbol.Name == "S1");
            Assert.Contains(cycleSCC, n => n.Symbol.Name == "S2");
            Assert.Contains(cycleSCC, n => n.Symbol.Name == "S3");
        }

        [Fact]
        public void DependencyGraph_TopologicalSort_ShouldSortDAG()
        {
            // Arrange
            var graph = new DependencyGraph(new TestSymbolComparer());
            var s1 = CreateSymbol("S1");
            var s2 = CreateSymbol("S2");
            var s3 = CreateSymbol("S3");

            // S1 -> S2, S1 -> S3, S2 -> S3
            graph.AddDependency(s1, s2);
            graph.AddDependency(s1, s3);
            graph.AddDependency(s2, s3);

            // Act
            var sorted = graph.TopologicalSort();

            // Assert
            Assert.Equal(3, sorted.Count);
            Assert.Equal("S1", sorted[0].Symbol.Name);
            Assert.Equal("S2", sorted[1].Symbol.Name);
            Assert.Equal("S3", sorted[2].Symbol.Name);
        }

        [Fact]
        public void DependencyGraph_DFS_ShouldVisitAllReachableNodes()
        {
            // Arrange
            var graph = new DependencyGraph(new TestSymbolComparer());
            var s1 = CreateSymbol("S1");
            var s2 = CreateSymbol("S2");
            var s3 = CreateSymbol("S3");

            graph.AddDependency(s1, s2);
            graph.AddDependency(s2, s3);

            // Act
            var startNode = graph.GetOrAddNode(s1);
            var visited = graph.DFS(startNode).ToList();

            // Assert
            Assert.Equal(3, visited.Count);
            Assert.Contains(visited, n => n.Symbol.Name == "S3");
        }
    }
}
