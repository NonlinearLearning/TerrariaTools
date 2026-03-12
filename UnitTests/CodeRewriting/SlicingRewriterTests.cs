using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TerrariaTools.Analysis;
using TerrariaTools.UnitTests.Infrastructure;

namespace TerrariaTools.UnitTests.CodeRewriting
{
    public class SlicingRewriterTests : RoslynTestBase
    {
        [Fact]
        public async Task MemberSlicingRewriter_ShouldRemoveUnusedMembers()
        {
            // Arrange
            string sourceCode = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.SlicingRewriterTests_sourceCode_1;

            var (workspace, solution, project) = await CreateSolutionAsync(("Player.cs", sourceCode));
            var document = project.Documents.First();
            var semanticModel = await document.GetSemanticModelAsync();
            var root = await document.GetSyntaxRootAsync();

            var classSymbol = semanticModel!.GetDeclaredSymbol(root!.DescendantNodes().OfType<ClassDeclarationSyntax>().First())!;
            var usedMethod = classSymbol.GetMembers("UsedMethod").First();
            var usedField = classSymbol.GetMembers("UsedField").First();

            var dependencyGraph = new DependencyGraph();
            dependencyGraph.GetOrAddNode(usedMethod).Status.MarkStaticallyReached();
            dependencyGraph.GetOrAddNode(usedField).Status.MarkStaticallyReached();

            // Act
            var rewriter = new MemberSlicingRewriter(semanticModel!, dependencyGraph, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
            var newRoot = rewriter.Visit(root);
            var newCode = newRoot!.ToFullString();

            // Assert
            Assert.Contains("UsedField", newCode);
            Assert.Contains("UsedMethod", newCode);
            Assert.DoesNotContain("UnusedField", newCode);
            Assert.DoesNotContain("UnusedMethod", newCode);
        }

        [Fact]
        public async Task MemberSlicingRewriter_ShouldAddNamespaceAliasesForAmbiguousTypes()
        {
            // Arrange
            string sourceCode = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.SlicingRewriterTests_sourceCode_2;
            string xnaStub = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.SlicingRewriterTests_xnaStub_1;
            string drawingStub = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.SlicingRewriterTests_drawingStub_1;
            string formsStub = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.SlicingRewriterTests_formsStub_1;
            var (workspace, solution, project) = await CreateSolutionAsync(
                ("Demo.cs", sourceCode),
                ("XnaStub.cs", xnaStub),
                ("DrawingStub.cs", drawingStub),
                ("FormsStub.cs", formsStub)
            );

            var document = project.Documents.First(d => d.Name == "Demo.cs");
            var semanticModel = await document.GetSemanticModelAsync();
            var root = await document.GetSyntaxRootAsync();

            var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl)!;
            var methodSymbol = classSymbol.GetMembers("DoSomething").First();

            var dependencyGraph = new DependencyGraph();
            dependencyGraph.GetOrAddNode(methodSymbol).IsStaticallyReached = true;

            // Act
            var rewriter = new MemberSlicingRewriter(semanticModel, dependencyGraph, new HashSet<ISymbol>(SymbolEqualityComparer.Default));
            var newRoot = rewriter.Visit(root);
            var newCode = newRoot!.ToFullString();

            // Assert
            Assert.Contains("using Color = Microsoft.Xna.Framework.Color;", newCode);
            Assert.Contains("using Keys = Microsoft.Xna.Framework.Input.Keys;", newCode);
        }
    }
}

