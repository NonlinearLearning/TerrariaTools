using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using TerrariaTools.Analysis;
using TerrariaTools.UnitTests.Scenarios;
using Xunit;

namespace TerrariaTools.UnitTests.Analysis
{
    public class CodeDependencyAnalyzerImplicitTests
    {
        private AdhocWorkspace _workspace;
        private Project _project;

        public CodeDependencyAnalyzerImplicitTests()
        {
            _workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var versionStamp = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, versionStamp, "TestProject", "TestProject", LanguageNames.CSharp)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            _project = _workspace.AddProject(projectInfo);

            // 显式引用
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location));

            // 加载核心库
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Runtime.dll")));
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(coreDir, "netstandard.dll")));
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(coreDir, "mscorlib.dll")));
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Collections.dll")));
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Linq.dll")));
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Linq.Expressions.dll")));
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Private.CoreLib.dll")));
        }

        private void AddReference(Type type)
        {
            _project = _project.AddMetadataReference(MetadataReference.CreateFromFile(type.Assembly.Location));
        }

        private async Task<(CodeDependencyAnalyzer, INamedTypeSymbol)> SetupAnalyzerAsync(string source, string typeName)
        {
            var project = _project.AddDocument("TestFile.cs", SourceText.From(source)).Project;
            var compilation = await project.GetCompilationAsync();

            // 调试：打印编译错误
            var diagnostics = compilation.GetDiagnostics();
            foreach (var diag in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                Console.WriteLine($"Compilation Error: {diag}");
            }

            var symbol = compilation.GetTypeByMetadataName(typeName);
            if (symbol == null)
            {
                throw new Exception($"Type {typeName} not found. Diagnostics: {string.Join("\n", diagnostics)}");
            }

            // Must use the solution from the document, as it's a new snapshot
            return (new CodeDependencyAnalyzer(project.Solution), symbol);
        }

        [Fact]
        public async Task Analyze_ForEach_ShouldDetectEnumeratorDependencies()
        {
            var source = SharedScenarios.ImplicitDependencyScenarios.ForEachEnumerator;
            var (analyzer, symbol) = await SetupAnalyzerAsync(source, "Test.Consumer");
            await analyzer.AnalyzeRecursiveAsync(symbol);

            var graph = analyzer.Graph;

            // Check implicit dependency on MyEnumerator members
            var myEnumerator = graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "MyEnumerator")?.Symbol;
            Assert.NotNull(myEnumerator);

            var moveNext = (myEnumerator as INamedTypeSymbol).GetMembers("MoveNext").FirstOrDefault();
            var current = (myEnumerator as INamedTypeSymbol).GetMembers("Current").FirstOrDefault();

            Assert.NotNull(moveNext);
            Assert.NotNull(current);

            // Verify Consumer (via Consume) depends on MoveNext/Current implicitly
            // Actually, Consume -> MyCollection.GetEnumerator -> MyEnumerator
            // AND Consume -> MyEnumerator.MoveNext / Current (because foreach expands to while(e.MoveNext()))

            var consumeMethod = (symbol as INamedTypeSymbol).GetMembers("Consume").First();
            var consumeNode = graph.GetOrAddNode(consumeMethod);

            Assert.Contains(consumeNode.Dependencies.Keys, n => n.Symbol.Name == "MoveNext");
            Assert.Contains(consumeNode.Dependencies.Keys, n => n.Symbol.Name == "Current");
        }

        [Fact]
        public async Task Analyze_Using_ShouldDetectDisposeDependency()
        {
            var source = SharedScenarios.ImplicitDependencyScenarios.UsingDispose;
            var (analyzer, symbol) = await SetupAnalyzerAsync(source, "Test.Consumer");
            await analyzer.AnalyzeRecursiveAsync(symbol);

            var graph = analyzer.Graph;
            var useMethod = (symbol as INamedTypeSymbol).GetMembers("Use").First();
            var useNode = graph.GetOrAddNode(useMethod);

            Assert.Contains(useNode.Dependencies.Keys, n => n.Symbol.Name == "Dispose");
        }

        [Fact]
        public async Task Analyze_Linq_ShouldDetectSelectDependency()
        {
            var source = SharedScenarios.ImplicitDependencyScenarios.LinqQuery;
            var (analyzer, symbol) = await SetupAnalyzerAsync(source, "Test.Consumer");
            await analyzer.AnalyzeRecursiveAsync(symbol);

            var graph = analyzer.Graph;
            var queryMethod = (symbol as INamedTypeSymbol).GetMembers("Query").First();
            var queryNode = graph.GetOrAddNode(queryMethod);

            // Depends on Where, Select (extension methods from Enumerable)
            Assert.Contains(queryNode.Dependencies.Keys, n => n.Symbol.Name == "Where");
            Assert.Contains(queryNode.Dependencies.Keys, n => n.Symbol.Name == "Select");
        }

        [Fact]
        public async Task Analyze_Attribute_ShouldDetectAttributeClassDependency()
        {
            var source = SharedScenarios.ImplicitDependencyScenarios.AttributeDependency;
            var (analyzer, symbol) = await SetupAnalyzerAsync(source, "Test.Consumer");
            await analyzer.AnalyzeRecursiveAsync(symbol);

            var graph = analyzer.Graph;
            var consumerNode = graph.GetOrAddNode(symbol);

            // Depends on MyAttrAttribute (constructor) and DepClass
            Assert.Contains(consumerNode.Dependencies.Keys, n => n.Symbol.Name == "MyAttrAttribute");
            Assert.Contains(consumerNode.Dependencies.Keys, n => n.Symbol.Name == "DepClass");
        }
    }
}
