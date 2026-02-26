using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.Analysis;
using Xunit;
using Xunit.Abstractions;

namespace TerrariaTools.UnitTests
{
    public class DependencyAnalyzerDebugTests
    {
        private readonly ITestOutputHelper _output;

        public DependencyAnalyzerDebugTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Debug_Collection_Initializer()
        {
            var source = @"
using System;
using System.Collections;
using System.Collections.Generic;

public class MyCollection : IEnumerable
{
    public void Add(int i) { }
    public void Add(string s, int i) { }
    public IEnumerator GetEnumerator() => throw new NotImplementedException();
}

public class TestClass
{
    public void Run()
    {
        var c = new MyCollection
        {
            1,
            { ""key"", 2 }
        };
    }
}
";
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var versionStamp = VersionStamp.Create();
            var projectInfo = ProjectInfo.Create(projectId, versionStamp, "TestProject", "TestProject", LanguageNames.CSharp);
            var project = workspace.AddProject(projectInfo);

            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var systemCore = MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
            project = project.AddMetadataReference(mscorlib).AddMetadataReference(systemCore);

            var document = project.AddDocument("Source.cs", source);
            var compilation = await document.Project.GetCompilationAsync();

            var testClass = compilation.GetSymbolsWithName("TestClass").OfType<INamedTypeSymbol>().First();
            var runMethod = testClass.GetMembers("Run").FirstOrDefault();

            var analyzer = new CodeDependencyAnalyzer(document.Project.Solution);
            await analyzer.AnalyzeRecursiveAsync(runMethod);

            _output.WriteLine("Found dependencies:");
            foreach (var node in analyzer.Graph.AllNodes)
            {
                _output.WriteLine($"- {node.Symbol.ToDisplayString()}");
            }

            var addStringInt = analyzer.Graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "Add" && n.Symbol is IMethodSymbol m && m.Parameters.Length == 2);
            Assert.NotNull(addStringInt);
        }
    }
}
