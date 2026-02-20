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

namespace TerrariaTools.UnitTests.Analysis
{
    public class MemberSlicingTests
    {
        [Fact]
        public async Task MemberSlicingRewriter_ShouldRemoveUnusedMembers()
        {
            // Arrange
            string sourceCode = @"
                public class Player {
                    public int UsedField;
                    public int UnusedField;
                    
                    public void UsedMethod() {
                        UsedField = 1;
                    }

                    public void UnusedMethod() {
                        UnusedField = 2;
                    }
                }";

            using var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
            var document = workspace.AddDocument(project.Id, "Player.cs", SourceText.From(sourceCode));
            var semanticModel = await document.GetSemanticModelAsync();
            var root = await document.GetSyntaxRootAsync();

            // 模拟分析结果：仅保留 UsedMethod 和 UsedField
            var classSymbol = semanticModel!.GetDeclaredSymbol(root!.DescendantNodes().OfType<ClassDeclarationSyntax>().First())!;
            var usedMethod = classSymbol.GetMembers("UsedMethod").First();
            var usedField = classSymbol.GetMembers("UsedField").First();

            var necessarySymbols = new List<ISymbol> { usedMethod, usedField };

            // Act
            var rewriter = new MemberSlicingRewriter(semanticModel!, necessarySymbols);
            var newRoot = rewriter.Visit(root);
            var newCode = newRoot!.ToFullString();

            // Assert
            Assert.Contains("UsedField", newCode);
            Assert.Contains("UsedMethod", newCode);
            Assert.DoesNotContain("UnusedField", newCode);
            Assert.DoesNotContain("UnusedMethod", newCode);
        }

        [Fact]
        public async Task MemberSlicingRewriter_ShouldHandlePartialFieldDeclarations()
        {
            // Arrange
            string sourceCode = @"
                public class Test {
                    public int Used, Unused;
                }";

            using var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("TestProject", LanguageNames.CSharp);
            var document = workspace.AddDocument(project.Id, "Test.cs", SourceText.From(sourceCode));
            var semanticModel = await document.GetSemanticModelAsync();
            var root = await document.GetSyntaxRootAsync();

            var classSymbol = semanticModel!.GetDeclaredSymbol(root!.DescendantNodes().OfType<ClassDeclarationSyntax>().First())!;
            var usedField = classSymbol.GetMembers("Used").First();

            // Act
            var rewriter = new MemberSlicingRewriter(semanticModel!, new List<ISymbol> { usedField });
            var newRoot = rewriter.Visit(root);
            var newCode = newRoot!.ToFullString();

            // Assert
            Assert.Contains("public int Used;", newCode);
            Assert.DoesNotContain("Unused", newCode);
        }
    }
}
