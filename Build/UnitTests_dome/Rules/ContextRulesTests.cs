using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuikGraph;
using TerrariaTools.Analysis.Dome;
using TerrariaTools.Rules.Dome.Mark.ContextRules;
using TerrariaTools.Rules.Dome.Mark.StaticRules;
using TerrariaTools.Rules.Dome.Mark;
using TerrariaTools.Dome.Tests.Infrastructure;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules
{
    public class ContextRulesTests : RoslynTestBase
    {
        [Fact]
        public async Task InheritanceShieldRule_ShouldBlockVirtualMethods()
        {
            var source = @"
class TestClass {
    public virtual void VirtualMethod() { }
    public void NormalMethod() { }
}";
            var (workspace, solution, project) = await CreateSolutionAsync(("Test.cs", source)).ConfigureAwait(false);
            var doc = project.Documents.First();
            var model = await doc.GetSemanticModelAsync().ConfigureAwait(false);
            var root = await doc.GetSyntaxRootAsync().ConfigureAwait(false);

            var virtualMethod = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "VirtualMethod");
            var normalMethod = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "NormalMethod");

            var virtualSymbol = model.GetDeclaredSymbol(virtualMethod);
            var normalSymbol = model.GetDeclaredSymbol(normalMethod);

            var context = new SpreadingContext
            {
                SemanticModel = model,
                InheritanceGraph = new BidirectionalGraph<ISymbol, InheritanceEdge>() // Empty graph for now
            };

            var rule = new InheritanceShieldRule();

            // Virtual Method -> Should Block
            var nodeVirtual = new DataFlowDependencyNode("1", virtualMethod, DataFlowDependencyNodeKind.Statement, "VirtualMethod", virtualSymbol);
            var resultVirtual = rule.Propagate(null, nodeVirtual, null, context);
            Assert.False(resultVirtual.ShouldPropagate);
            Assert.True(resultVirtual.IsHandled); // Blocked

            // Normal Method -> Should Pass
            var nodeNormal = new DataFlowDependencyNode("2", normalMethod, DataFlowDependencyNodeKind.Statement, "NormalMethod", normalSymbol);
            var resultNormal = rule.Propagate(null, nodeNormal, null, context);
            Assert.False(resultNormal.ShouldPropagate);
            Assert.False(resultNormal.IsHandled); // None
        }

        [Fact]
        public async Task ObjectInitializerRule_ShouldBlockInitializers()
        {
            var source = @"
class TestClass {
    void Method() {
        var x = new TestClass { Property = 1 };
        var y = 1;
    }
    public int Property { get; set; }
}";
            var (root, model) = await GetCompilationAsync(source).ConfigureAwait(false);

            var objectCreation = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().First();
            var initializerAssignment = objectCreation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>().First();

            var variableDecl = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Last(); // var y = 1;
            var normalAssignment = variableDecl.Declaration.Variables.First().Initializer.Value; // 1

            var context = new SpreadingContext
            {
                SemanticModel = model
            };
            var rule = new ObjectInitializerRule();

            // Initializer Assignment -> Should Block
            var nodeInitializer = new DataFlowDependencyNode("1", initializerAssignment, DataFlowDependencyNodeKind.Statement, "Initializer");
            var resultInitializer = rule.Propagate(null, nodeInitializer, null, context);
            Assert.False(resultInitializer.ShouldPropagate);
            Assert.True(resultInitializer.IsHandled); // Blocked

            // Normal Assignment (represented as expression) -> Should Pass
            var nodeNormal = new DataFlowDependencyNode("2", normalAssignment, DataFlowDependencyNodeKind.Statement, "Normal");
            var resultNormal = rule.Propagate(null, nodeNormal, null, context);
            Assert.False(resultNormal.ShouldPropagate);
            Assert.False(resultNormal.IsHandled); // None
        }

        [Fact]
        public void SpreadingRuleRegistry_ShouldRegisterRules()
        {
            var registry = SpreadingRuleRegistry.Instance;

            // Check specific rules are registered
            // MethodDeclarationSyntax is handled by InheritanceShieldRule
            var methodRules = registry.GetRulesForKind(SyntaxKind.MethodDeclaration);
            Assert.Contains(methodRules, r => r is InheritanceShieldRule);

            // AssignmentExpression is handled by ObjectInitializerRule
            var assignmentRules = registry.GetRulesForKind(SyntaxKind.SimpleAssignmentExpression);
            Assert.Contains(assignmentRules, r => r is ObjectInitializerRule);

            // Verify priorities
            var shieldRule = methodRules.First(r => r is InheritanceShieldRule);
            var shieldAttr = shieldRule.GetType().GetCustomAttributes(typeof(SpreadingRuleAttribute), false).FirstOrDefault() as SpreadingRuleAttribute;
            Assert.NotNull(shieldAttr);
            Assert.Equal(0, shieldAttr.Priority);

            var initRule = assignmentRules.First(r => r is ObjectInitializerRule);
            var initAttr = initRule.GetType().GetCustomAttributes(typeof(SpreadingRuleAttribute), false).FirstOrDefault() as SpreadingRuleAttribute;
            Assert.NotNull(initAttr);
            Assert.Equal(50, initAttr.Priority);
        }

        [Fact]
        public async Task SanitizationRule_ShouldIdentifyConstantAssignments()
        {
            var source = @"
class TestClass {
    void Method() {
        var a = 1; // Constant
        var b = a; // Not constant
        var c = 1 + 2; // Constant (no identifiers)
        var d = ""hello""; // Constant
        int e;
        e = 5; // Constant assignment
        e = b; // Not constant
    }
}";
            var (root, _) = await GetCompilationAsync(source).ConfigureAwait(false);
            var statements = root.DescendantNodes()
                .OfType<StatementSyntax>()
                .Where(s => !(s is BlockSyntax))
                .ToList();

            // var a = 1;
            Assert.True(SanitizationRule.IsSanitizingAssignment(statements[0]));

            // var b = a;
            Assert.False(SanitizationRule.IsSanitizingAssignment(statements[1]));

            // var c = 1 + 2;
            Assert.True(SanitizationRule.IsSanitizingAssignment(statements[2]));

            // var d = "hello";
            Assert.True(SanitizationRule.IsSanitizingAssignment(statements[3]));

            // int e; (LocalDeclarationStatement without initializer)
            Assert.False(SanitizationRule.IsSanitizingAssignment(statements[4]));

            // e = 5; (ExpressionStatement -> Assignment)
            Assert.True(SanitizationRule.IsSanitizingAssignment(statements[5]));

            // e = b;
            Assert.False(SanitizationRule.IsSanitizingAssignment(statements[6]));
        }

        [Fact]
        public void DefaultSpreadingRule_ShouldPropagateFromStatementToVariable()
        {
            var rule = new DefaultSpreadingRule();
            var context = new SpreadingContext();

            // Mock nodes
            var stmtNode = new DataFlowDependencyNode("1", null, DataFlowDependencyNodeKind.Statement, "Stmt");
            var varNode = new DataFlowDependencyNode("2", null, DataFlowDependencyNodeKind.Variable, "Var");

            // Edge: Stmt Defines Var
            var edge = new DataFlowDependencyEdge(stmtNode, varNode, DataFlowDependencyEdgeKind.Defines);

            // Statement (Marked) -> Variable
            // If Statement is marked, and it defines Variable, then Variable is tainted.
            var result = rule.Propagate(stmtNode, varNode, edge, context);

            Assert.True(result.ShouldPropagate);
            Assert.False(result.IsHandled);
        }
    }
}
