using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using TerrariaTools.Analysis;
using TerrariaTools.UnitTests.Infrastructure;
using TerrariaTools.UnitTests.Scenarios;
using Xunit;

namespace TerrariaTools.UnitTests.StaticAnalysis
{
    public class DependencyGraphTests : RoslynTestBase
    {
        [Fact]
        public async Task AnalyzeRecursiveAsync_ShouldDetectDependencies()
        {
            // Arrange
            var source = SharedScenarios.AnalysisScenarios.DependencyCycle;
            var (workspace, solution, project) = await CreateSolutionAsync(("TestFile.cs", source));
            var compilation = await project.GetCompilationAsync();
            var classASymbol = compilation.GetTypeByMetadataName("TestNamespace.ClassA");
            
            var analyzer = new CodeDependencyAnalyzer(solution);

            // Act
            await analyzer.AnalyzeRecursiveAsync(classASymbol);

            // Assert
            var graph = analyzer.Graph;
            var nodes = graph.GetAllNodes().ToList();

            Assert.Contains(nodes, n => n.Symbol.Name == "ClassA");
            Assert.Contains(nodes, n => n.Symbol.Name == "ClassB");
            Assert.Contains(nodes, n => n.Symbol.Name == "ClassC");

            var nodeA = nodes.First(n => n.Symbol.Name == "ClassA");
            var nodeB = nodes.First(n => n.Symbol.Name == "ClassB");
            var nodeC = nodes.First(n => n.Symbol.Name == "ClassC");

            // Verify edges via QuikGraph
            Assert.True(graph.UnderlyingGraph.ContainsEdge(nodeA, nodeB));
            Assert.True(graph.UnderlyingGraph.ContainsEdge(nodeB, nodeC));
            Assert.True(graph.UnderlyingGraph.ContainsEdge(nodeC, nodeA));
        }
    }
}
