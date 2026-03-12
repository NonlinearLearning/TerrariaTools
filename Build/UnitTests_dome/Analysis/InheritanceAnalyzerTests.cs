using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Analysis.Dome;
using TerrariaTools.Dome.Tests.Infrastructure;
using Xunit;

namespace TerrariaTools.Dome.Tests.Analysis
{
    public class InheritanceAnalyzerTests : RoslynTestBase
    {
        [Fact]
        public void GetBaseTypes_ShouldReturnCorrectHierarchy()
        {
            var source = @"
                public class Base { }
                public class Middle : Base { }
                public class Derived : Middle { }
            ";

            var compilation = CreateCompilation(source);
            var tree = compilation.SyntaxTrees.First();
            var model = compilation.GetSemanticModel(tree);

            var derivedClass = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .First(c => c.Identifier.Text == "Derived");
            var derivedSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(derivedClass)!;

            var baseTypes = InheritanceAnalyzer.GetBaseTypes(derivedSymbol).ToList();

            Assert.Equal(2, baseTypes.Count);
            Assert.Equal("Middle", baseTypes[0].Name);
            Assert.Equal("Base", baseTypes[1].Name);
        }

        [Fact]
        public async Task HasDerivedClassesAsync_ShouldReturnTrue_WhenDerivedClassExists()
        {
            var source = @"
                public class Base { }
                public class Derived : Base { }
            ";

            var (workspace, solution, project) = await CreateSolutionAsync(("Test.cs", source));
            var compilation = await project.GetCompilationAsync();
            var tree = compilation.SyntaxTrees.First();
            var model = compilation.GetSemanticModel(tree);

            var baseClass = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .First(c => c.Identifier.Text == "Base");
            var baseSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(baseClass)!;

            var hasDerived = await InheritanceAnalyzer.HasDerivedClassesAsync(baseSymbol, solution);

            Assert.True(hasDerived);
        }

        [Fact]
        public async Task HasDerivedClassesAsync_ShouldReturnFalse_WhenNoDerivedClassExists()
        {
            var source = @"
                public class Base { }
            ";

            var (workspace, solution, project) = await CreateSolutionAsync(("Test.cs", source));
            var compilation = await project.GetCompilationAsync();
            var tree = compilation.SyntaxTrees.First();
            var model = compilation.GetSemanticModel(tree);

            var baseClass = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .First(c => c.Identifier.Text == "Base");
            var baseSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(baseClass)!;

            var hasDerived = await InheritanceAnalyzer.HasDerivedClassesAsync(baseSymbol, solution);

            Assert.False(hasDerived);
        }

        [Fact]
        public void IsInInheritanceChain_ShouldReturnTrue_ForOverrideMethod()
        {
            var source = @"
                public class Base { public virtual void Foo() { } }
                public class Derived : Base { public override void Foo() { } }
            ";

            var compilation = CreateCompilation(source);
            var tree = compilation.SyntaxTrees.First();
            var model = compilation.GetSemanticModel(tree);

            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .First(m => m.Identifier.Text == "Foo" && m.Modifiers.Any(mod => mod.Text == "override"));
            var symbol = (IMethodSymbol)model.GetDeclaredSymbol(method)!;

            var result = InheritanceAnalyzer.IsInInheritanceChain(symbol);

            Assert.True(result);
        }

        [Fact]
        public void IsInInheritanceChain_ShouldReturnTrue_ForInterfaceImplementation()
        {
            var source = @"
                public interface IFoo { void Bar(); }
                public class Implementation : IFoo { public void Bar() { } }
            ";

            var compilation = CreateCompilation(source);
            var tree = compilation.SyntaxTrees.First();
            var model = compilation.GetSemanticModel(tree);

            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .First(m => m.Identifier.Text == "Bar");
            var symbol = (IMethodSymbol)model.GetDeclaredSymbol(method)!;

            var result = InheritanceAnalyzer.IsInInheritanceChain(symbol);

            Assert.True(result);
        }

        [Fact]
        public void Build_ShouldCreateGraphWithCorrectEdges()
        {
            var source = @"
                public interface I { }
                public class Base { }
                public class Derived : Base, I { }
            ";

            var compilation = CreateCompilation(source);
            var analyzer = new InheritanceAnalyzer();
            var graph = analyzer.Build(compilation);

            // Find symbols
            var symbols = graph.Vertices.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First());

            Assert.True(symbols.ContainsKey("Base"));
            Assert.True(symbols.ContainsKey("Derived"));
            Assert.True(symbols.ContainsKey("I"));

            var derived = symbols["Derived"];
            var baseClass = symbols["Base"];
            var iface = symbols["I"];

            // Check edges
            var outEdges = graph.OutEdges(derived).ToList();

            Assert.Contains(outEdges, e => e.Target.Name == "Base" && e.RelationType == InheritanceRelationType.Inherits);
            Assert.Contains(outEdges, e => e.Target.Name == "I" && e.RelationType == InheritanceRelationType.Implements);
        }
    }
}
