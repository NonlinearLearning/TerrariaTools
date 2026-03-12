using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Rules.Dome.Mark;
using TerrariaTools.Rules.Dome;
using TerrariaTools.Dome.Tests.Infrastructure;
using TerrariaTools.Dome.Tests.Scenarios;
using Xunit;

namespace TerrariaTools.Rules.Dome.UnitTests
{
    public class RuleEngineTests : RoslynTestBase
    {
        private SyntaxNode MarkNode(SyntaxNode root, SyntaxNode target)
        {
            return root.ReplaceNode(target, target.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, "Action=Delete;Reason=Test")));
        }

        [Fact]
        public void SimplePropagation_ShouldMarkDependentVariable()
        {
            var source = @"
                class Test {
                    void M() {
                        int i = 0;
                        int j = i;
                    }
                }
            ";

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            // Find 'int i = 0;'
            var iDecl = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "i"));

            // Mark 'i'
            var markedRoot = MarkNode(root, iDecl);

            var engine = new RuleEngine();
            var result = engine.Apply(markedRoot);

            // Find 'int j = i;' in result
            var jDecl = result.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "j"));

            // Verify 'j' is marked
            Assert.True(jDecl.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(), "j should be marked for deletion because it depends on i");
        }

        [Fact]
        public void Sanitization_ShouldStopPropagation()
        {
            var source = @"
                class Test {
                    void M() {
                        int i = 0;
                        int j = i;
                        j = 1; // Sanitization
                        int k = j;
                    }
                }
            ";

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            var iDecl = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "i"));
            var markedRoot = MarkNode(root, iDecl);

            var engine = new RuleEngine();
            var result = engine.Apply(markedRoot);

            var kDecl = result.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "k"));

            // Verify 'k' is NOT marked
            Assert.False(kDecl.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(), "k should NOT be marked because j was sanitized");
        }

        [Fact]
        public void TryBlockCascading_ShouldMarkTryStatement()
        {
            var source = @"
                class Test {
                    void M() {
                        int i = 0;
                        try {
                            int j = i;
                        } catch { }
                    }
                }
            ";

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            var iDecl = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "i"));
            var markedRoot = MarkNode(root, iDecl);

            var engine = new RuleEngine();
            var result = engine.Apply(markedRoot);

            var tryStmt = result.DescendantNodes().OfType<TryStatementSyntax>().First();

            // Verify 'try' is marked
            Assert.True(tryStmt.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(), "Try statement should be marked because all its statements are marked");
        }

        [Fact]
        public void ScenarioTest_InheritanceAndPropagation()
        {
            // Use a scenario from SharedScenarios
            var source = @"
                class Base { public int X; }
                class Derived : Base {
                    void M() {
                        int i = X;
                        int j = i;
                    }
                }
            ";

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            // Mark 'X' in Base
            var fieldX = root.DescendantNodes().OfType<FieldDeclarationSyntax>().First(f => f.Declaration.Variables.Any(v => v.Identifier.Text == "X"));
            var markedRoot = MarkNode(root, fieldX);

            var engine = new RuleEngine();
            var result = engine.Apply(markedRoot);

            var iDecl = result.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "i"));
            var jDecl = result.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "j"));

            Assert.True(iDecl.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(), "i should be marked because it depends on Base.X");
            Assert.True(jDecl.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(), "j should be marked because it depends on i");
        }

        [Fact]
        public void ScenarioTest_IfElsePropagation()
        {
            var source = @"
                class Test {
                    void M() {
                        int i = 0;
                        int j = i;
                        if (j > 0) {
                            System.Console.WriteLine(1);
                        }
                    }
                }
            ";

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            var iDecl = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "i"));
            var markedRoot = MarkNode(root, iDecl);

            var engine = new RuleEngine();
            var result = engine.Apply(markedRoot);

            var ifStmt = result.DescendantNodes().OfType<IfStatementSyntax>().First();

            // If j is marked, then 'if (j > 0)' should also be marked because its condition depends on j
            Assert.True(ifStmt.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(), "If statement should be marked because its condition depends on j");
        }

        [Fact]
        public void ScenarioTest_WhileLoopPropagation()
        {
            var source = @"
                class Test {
                    void M() {
                        int i = 0;
                        int j = i;
                        while (j < 10) {
                            j++;
                        }
                    }
                }
            ";

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            var iDecl = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "i"));
            var markedRoot = MarkNode(root, iDecl);

            var engine = new RuleEngine();
            var result = engine.Apply(markedRoot);

            var whileStmt = result.DescendantNodes().OfType<WhileStatementSyntax>().First();

            // While loop should be marked because its condition depends on j
            Assert.True(whileStmt.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(), "While loop should be marked because its condition depends on j");
        }

        [Fact]
        public async Task Fluent_ScenarioTest_IfElsePropagation()
        {
            await Given(SharedScenarios.IfElse)
                .WhenMarking<LocalDeclarationStatementSyntax>(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "condition"))
                .ThenVerify(result =>
                {
                    var ifStmt = result.DescendantNodes().OfType<IfStatementSyntax>().First();
                    Assert.True(ifStmt.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(), "If statement should be marked");
                });
        }

        [Fact]
        public async Task Fluent_ScenarioTest_WhileLoopPropagation()
        {
            await Given(SharedScenarios.WhileLoop)
                .WhenMarking<LocalDeclarationStatementSyntax>(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "i"))
                .ThenVerify(result =>
                {
                    var whileStmt = result.DescendantNodes().OfType<WhileStatementSyntax>().First();
                    Assert.True(whileStmt.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(), "While loop should be marked");
                });
        }

        [Fact]
        public async Task Fluent_ScenarioTest_ByName_IfElse()
        {
            await GivenScenario("IfElse")
                .WhenMarking<LocalDeclarationStatementSyntax>(d => d.Declaration.Variables.Any(v => v.Identifier.Text == "condition"))
                .ThenVerify(result =>
                {
                    var ifStmt = result.DescendantNodes().OfType<IfStatementSyntax>().First();
                    Assert.True(ifStmt.GetAnnotations(RuleConstants.RewriteAnnotationKind).Any(), "If statement should be marked");
                });
        }
    }
}
