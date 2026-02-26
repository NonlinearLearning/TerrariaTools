using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using TerrariaTools.Analysis;

namespace UnitTests
{
    public class BugFixTests
    {
        [Fact]
        public async Task ShouldTrack_FieldInitializerDependencies_In_CodeDependencyAnalyzer()
        {
            var source = @"
public class Seed {
    public Seed(string code) {}
}

public class WorldGen
{
    public static Seed paintEverythingGray = Register(""code"");

    public static Seed Register(string code)
    {
        return new Seed(code);
    }
}
";
            var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            var document = project.AddDocument("Test.cs", source);
            var solution = document.Project.Solution;
            var compilation = await document.Project.GetCompilationAsync();
            var worldGenSymbol = compilation.GetTypeByMetadataName("WorldGen");
            var paintField = worldGenSymbol.GetMembers("paintEverythingGray").First();
            var registerMethod = worldGenSymbol.GetMembers("Register").First();

            var analyzer = new CodeDependencyAnalyzer(solution);
            await analyzer.AnalyzeRecursiveAsync(paintField);

            var graph = analyzer.Graph;
            var nodes = graph.AllNodes.ToList();

            // Verify that 'Register' is in the dependency graph because it's called in the field initializer
            Assert.Contains(nodes, n => SymbolEqualityComparer.Default.Equals(n.Symbol, registerMethod));
        }

        private async Task<(Compilation Compilation, SyntaxTree Tree, SemanticModel Model)> CreateCompilationAsync(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var systemCore = MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);

            var compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(mscorlib, systemCore)
                .AddSyntaxTrees(tree);

            var model = compilation.GetSemanticModel(tree);
            return (compilation, tree, model);
        }

        [Fact]
        public async Task ShouldKeep_StubImplementation_For_UnusedInterfaceMethod()
        {
            var source = @"
using System;

public interface IWorker
{
    void Work();
}

public class MyWorker : IWorker
{
    public void Work()
    {
        Console.WriteLine(""Working..."");
    }
}

public class Program
{
    public static void Main()
    {
        // We use MyWorker, but we never call Work()
        // And we never cast it to IWorker
        var w = new MyWorker();
    }
}
";
            var (compilation, tree, model) = await CreateCompilationAsync(source);
            var solution = new AdhocWorkspace().CurrentSolution; // Mock solution not fully used here

            // 1. Build Call Graph
            var builder = new CallGraphBuilder(solution);
            // We manually build the graph for this single tree since CallGraphBuilder expects a solution with projects
            // But CallGraphBuilder is tightly coupled to Solution.
            // So let's mock the necessary parts or just use the logic we want to test directly.

            // To properly test MemberSlicingRewriter, we need:
            // 1. A set of necessary symbols (MyWorker should be necessary)
            // 2. A set of method actions (MyWorker.Work should be Delete because it's not called)

            var root = await tree.GetRootAsync();
            var myWorkerClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First(c => c.Identifier.Text == "MyWorker");
            var workMethod = myWorkerClass.Members.OfType<MethodDeclarationSyntax>().First();
            var workSymbol = model.GetDeclaredSymbol(workMethod);
            var myWorkerSymbol = model.GetDeclaredSymbol(myWorkerClass);

            var necessarySymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            necessarySymbols.Add(myWorkerSymbol); // MyWorker is used

            var actions = new Dictionary<ISymbol, CallGraphBuilder.GraphMethodAction>(SymbolEqualityComparer.Default);
            actions[workSymbol] = CallGraphBuilder.GraphMethodAction.Delete; // Explicitly mark as Delete

            // 2. Run MemberSlicingRewriter
            var rewriter = new MemberSlicingRewriter(model, actions, necessarySymbols);
            var newRoot = rewriter.Visit(root);

            // 3. Verify
            var newSource = newRoot.ToFullString();

            // The Work method should still exist (as a stub), not be deleted
            Assert.Contains("public void Work()", newSource);
            // The body should be empty or return default
            Assert.DoesNotContain("Console.WriteLine", newSource);
        }

        [Fact]
        public async Task ShouldKeep_StubImplementation_For_UnusedAbstractMethod()
        {
            var source = @"
public abstract class BaseWorker
{
    public abstract void Work();
}

public class MyWorker : BaseWorker
{
    public override void Work()
    {
        // Heavy implementation
        int x = 1 + 1;
    }
}

public class Program
{
    public static void Main()
    {
        var w = new MyWorker();
    }
}
";
            var (compilation, tree, model) = await CreateCompilationAsync(source);

            var root = await tree.GetRootAsync();
            var myWorkerClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First(c => c.Identifier.Text == "MyWorker");
            var workMethod = myWorkerClass.Members.OfType<MethodDeclarationSyntax>().First();
            var workSymbol = model.GetDeclaredSymbol(workMethod);
            var myWorkerSymbol = model.GetDeclaredSymbol(myWorkerClass);

            var necessarySymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            necessarySymbols.Add(myWorkerSymbol);

            var actions = new Dictionary<ISymbol, CallGraphBuilder.GraphMethodAction>(SymbolEqualityComparer.Default);
            actions[workSymbol] = CallGraphBuilder.GraphMethodAction.Delete;

            var rewriter = new MemberSlicingRewriter(model, actions, necessarySymbols);
            var newRoot = rewriter.Visit(root);
            var newSource = newRoot.ToFullString();

            Assert.Contains("public override void Work()", newSource);
            Assert.DoesNotContain("int x", newSource);
        }

        [Fact]
        public async Task ShouldTrack_InterfaceDependencies_In_CodeDependencyAnalyzer()
        {
             var source = @"
public interface IAudioTrack {}
public class CueAudioTrack : IAudioTrack {}

public class Program
{
    public static void Main()
    {
        // If we use CueAudioTrack, we implicitly depend on IAudioTrack
        var t = new CueAudioTrack();
    }
}
";
            var (compilation, tree, model) = await CreateCompilationAsync(source);
            var solution = new AdhocWorkspace().CurrentSolution;
            // This test is harder to mock because CodeDependencyAnalyzer depends on Solution/Project structure heavily
            // But we can test the specific logic if we can access it.
            // Since CodeDependencyAnalyzer uses semantic model to find dependencies.

            // Actually, we can just test the logic inside CodeDependencyAnalyzer if we can instantiate it.
            // It requires a Solution.

            // Let's create a real workspace for this test
            var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            var document = project.AddDocument("Test.cs", source);
            var newSol = document.Project.Solution;
            var newModel = await document.GetSemanticModelAsync();

            var analyzer = new CodeDependencyAnalyzer(newSol);

            var root = await document.GetSyntaxRootAsync();
            var programClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First(c => c.Identifier.Text == "Program");
            var mainMethod = programClass.Members.OfType<MethodDeclarationSyntax>().First();
            var mainSymbol = newModel.GetDeclaredSymbol(mainMethod);

            // Analyze from Main
            await analyzer.AnalyzeRecursiveAsync(mainSymbol);

            var graph = analyzer.Graph;

            // Check if IAudioTrack is in the graph
            var iAudioTrack = graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "IAudioTrack");
            Assert.NotNull(iAudioTrack);
        }

        [Fact]
        public async Task ShouldSupport_MultipleSeeds_In_CodeDependencyAnalyzer()
        {
             var source = @"
public class A {}
public class B {}

public class Program
{
    public static void Main() {} // Uses nothing
    public static void Other() { var a = new A(); } // Uses A
}
";
            // If we seed only Main, A should not be found.
            // If we seed Main AND Other, A should be found.

            var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            var document = project.AddDocument("Test.cs", source);
            var newSol = document.Project.Solution;
            var newModel = await document.GetSemanticModelAsync();

            var analyzer = new CodeDependencyAnalyzer(newSol);

            var root = await document.GetSyntaxRootAsync();
            var programClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First(c => c.Identifier.Text == "Program");
            var mainMethod = programClass.Members.OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Main");
            var otherMethod = programClass.Members.OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Other");

            var mainSymbol = newModel.GetDeclaredSymbol(mainMethod);
            var otherSymbol = newModel.GetDeclaredSymbol(otherMethod);

            // Test 1: Only Main
            await analyzer.AnalyzeRecursiveAsync(mainSymbol);
            Assert.Null(analyzer.Graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "A"));

            // Test 2: Multiple seeds
            var analyzer2 = new CodeDependencyAnalyzer(newSol);
            await analyzer2.AnalyzeRecursiveAsync(new[] { mainSymbol, otherSymbol });
            Assert.NotNull(analyzer2.Graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "A"));
        }

        [Fact]
        public async Task ShouldTrack_ExtensionMethod_Dependency()
        {
            var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location))
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));

            var source = @"
using System;

public class Data {}

public static class Extensions
{
    public static void DoSomething(this Data data)
    {
        Console.WriteLine(""Done"");
    }
}

public class Program
{
    public static void Main()
    {
        var d = new Data();
        d.DoSomething();
    }
}
";
            var document = project.AddDocument("Test.cs", SourceText.From(source));
            var compilation = await document.Project.GetCompilationAsync();
            var syntaxTree = await document.GetSyntaxTreeAsync();
            var model = compilation.GetSemanticModel(syntaxTree);

            // Get symbols
            var root = await syntaxTree.GetRootAsync();
            var programClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First(c => c.Identifier.Text == "Program");
            var mainMethod = programClass.Members.OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Main");
            var mainSymbol = model.GetDeclaredSymbol(mainMethod);

            // Run Analyzer
            var analyzer = new CodeDependencyAnalyzer(document.Project.Solution);
            await analyzer.AnalyzeRecursiveAsync(mainSymbol);

            // Verify dependency
            // Main -> DoSomething (extension method)
            // The graph should contain DoSomething
            var node = analyzer.Graph.AllNodes.FirstOrDefault(n => n.Symbol.Name == "DoSomething" && n.Symbol.ContainingType.Name == "Extensions");
            Assert.NotNull(node);
        }
    }
}
