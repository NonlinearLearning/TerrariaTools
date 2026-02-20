using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using TerrariaTools.Analysis;
using Xunit;

namespace TerrariaTools.UnitTests.Analysis
{
    public class CodeDependencyAnalyzerTests
    {
        [Fact]
        public async Task AnalyzeRecursiveAsync_ShouldDetectDependencies()
        {
            // Arrange
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var versionStamp = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, versionStamp, "TestProject", "TestProject", LanguageNames.CSharp);
            var project = workspace.AddProject(projectInfo);

            // Create C# source with dependencies
            var source = @"
using System;

namespace TestNamespace
{
    public class ClassA
    {
        public ClassB PropB { get; set; }
        public void MethodA() { }
    }

    public class ClassB
    {
        public ClassC PropC { get; set; }
    }

    public class ClassC
    {
        public ClassA PropA { get; set; } // Creates cycle: A -> B -> C -> A
    }
}";
            
            // Add metadata reference for mscorlib (object, etc.)
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            project = project.AddMetadataReference(mscorlib);

            var document = workspace.AddDocument(projectId, "TestFile.cs", SourceText.From(source));
            
            // Get compilation
            var compilation = await document.Project.GetCompilationAsync();
            Assert.NotNull(compilation);

            var classASymbol = compilation.GetTypeByMetadataName("TestNamespace.ClassA");
            Assert.NotNull(classASymbol);

            // Re-fetch project/solution because adding document creates a new snapshot
            var solution = document.Project.Solution;
            var analyzer = new CodeDependencyAnalyzer(solution);

            // Act
            await analyzer.AnalyzeRecursiveAsync(classASymbol);

            // Assert
            var graph = analyzer.Graph;
            var nodes = graph.AllNodes.ToList();

            // Verify nodes existence
            Assert.Contains(nodes, n => n.Symbol.Name == "ClassA");
            Assert.Contains(nodes, n => n.Symbol.Name == "ClassB");
            Assert.Contains(nodes, n => n.Symbol.Name == "ClassC");

            // Verify edges
            var nodeA = nodes.First(n => n.Symbol.Name == "ClassA");
            var nodeB = nodes.First(n => n.Symbol.Name == "ClassB");
            var nodeC = nodes.First(n => n.Symbol.Name == "ClassC");

            // ClassA -> ClassB
            Assert.Contains(nodeB, nodeA.Dependencies);
            // ClassB -> ClassC
            Assert.Contains(nodeC, nodeB.Dependencies);
            // ClassC -> ClassA
            Assert.Contains(nodeA, nodeC.Dependencies);

            // Verify SCC (Cycle detection)
            var sccs = graph.FindSCCs();
            var cycleSCC = sccs.FirstOrDefault(s => s.Count > 1);
            Assert.NotNull(cycleSCC);
            Assert.Equal(3, cycleSCC.Count);
            Assert.Contains(cycleSCC, n => n.Symbol.Name == "ClassA");
            Assert.Contains(cycleSCC, n => n.Symbol.Name == "ClassB");
            Assert.Contains(cycleSCC, n => n.Symbol.Name == "ClassC");
        }
    }
}
