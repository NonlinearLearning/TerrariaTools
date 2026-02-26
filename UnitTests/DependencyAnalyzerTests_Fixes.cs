using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TerrariaTools.Analysis;
using Xunit;

namespace TerrariaTools.UnitTests
{
    public class DependencyAnalyzerTests_Fixes
    {
        private async Task<(CodeDependencyAnalyzer Analyzer, ISymbol Seed)> AnalyzeAsync(string source, string seedTypeName, string? seedMethodName = null)
        {
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

            var typeSymbol = compilation.GetSymbolsWithName(seedTypeName).OfType<INamedTypeSymbol>().FirstOrDefault();
            ISymbol seedSymbol = typeSymbol;

            if (seedMethodName != null)
            {
                seedSymbol = typeSymbol.GetMembers(seedMethodName).FirstOrDefault();
            }

            var analyzer = new CodeDependencyAnalyzer(document.Project.Solution);
            await analyzer.AnalyzeRecursiveAsync(seedSymbol);

            return (analyzer, seedSymbol);
        }

        [Fact]
        public async Task Should_Track_Assignment_Left_Hand_Side_As_Dependency()
        {
            var source = @"
using System;

public class TestClass
{
    public static int StaticField;

    static TestClass()
    {
        StaticField = 42;
    }
}
";
            // Seed with TestClass. Static constructor is included implicitly.
            var (analyzer, seed) = await AnalyzeAsync(source, "TestClass");

            var staticField = analyzer.Graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "StaticField");
            Assert.NotNull(staticField);
        }

        [Fact]
        public async Task Should_Track_Field_Assigned_In_Constructor()
        {
            var source = @"
using System;

public class TestClass
{
    private int _field;

    public TestClass(int value)
    {
        _field = value;
    }

    public void Run()
    {
        var instance = new TestClass(10);
    }
}";
            // Seed with TestClass.Run
            var (analyzer, seed) = await AnalyzeAsync(source, "TestClass", "Run");

            // Assert
            var fieldNode = analyzer.Graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "_field");
            Assert.NotNull(fieldNode);

            var ctorNode = analyzer.Graph.AllNodes.FirstOrDefault(n => n.Symbol is IMethodSymbol m && m.MethodKind == MethodKind.Constructor && m.ContainingType.Name == "TestClass");
            Assert.NotNull(ctorNode);
        }

        [Fact]
        public async Task Should_Track_Collection_Initializer_Add_Method()
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
            // Seed with TestClass.Run
            var (analyzer, seed) = await AnalyzeAsync(source, "TestClass", "Run");

            // Find MyCollection type
            var myCollectionNode = analyzer.Graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "MyCollection");
            Assert.NotNull(myCollectionNode);

            // Find Add(int)
            var addInt = analyzer.Graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "Add" && n.Symbol is IMethodSymbol m && m.Parameters[0].Type.Name == "Int32" && m.Parameters.Length == 1);
            Assert.NotNull(addInt);

            // Find Add(string, int)
            var addStringInt = analyzer.Graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "Add" && n.Symbol is IMethodSymbol m && m.Parameters.Length == 2);
            Assert.NotNull(addStringInt);
        }
    }
}
