using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Rules.Dome.Mark;
using TerrariaTools.Rules.Dome.Mark.StaticRules;
using TerrariaTools.Rules.Dome;
using TerrariaTools.Dome.Tests.Infrastructure;
using Xunit;

namespace TerrariaTools.Dome.Tests.Rules
{
    public class StaticRulesTests : RoslynTestBase
    {
        [Fact]
        public async Task ExpressionMarkRule_ShouldMarkComplexLogic()
        {
            var source = @"
class TestClass {
    void Method() {
        bool k = true, l = true, j = true, h = true, f = true, g = true;
        if (k || l && j) { }
        if ((k || l) && j) { }
        if (k && l && h) { }
        if ((k || l) || (f && g)) { }
    }
}";
            var (root, _) = await GetCompilationAsync(source).ConfigureAwait(false);

            // Manually mark 'k' as bad source
            var kNodes = root.DescendantNodes().OfType<IdentifierNameSyntax>().Where(id => id.Identifier.Text == "k");
            var markedRoot = root.ReplaceNodes(kNodes, (o, n) => n.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, "Source")));

            var method = markedRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var statements = method.Body.Statements.OfType<IfStatementSyntax>().ToList();

            var rule = new ExpressionMarkRule();

            // Scene 1: k || l && j (k is bad) -> Root || is marked
            var stmt1 = (IfStatementSyntax)rule.MarkStatement(statements[0]);
            Assert.True(stmt1.Condition.HasAnnotations(RuleConstants.RewriteAnnotationKind));

            // Scene 2: (k || l) && j -> Root && not marked (isolated by ||)
            var stmt2 = (IfStatementSyntax)rule.MarkStatement(statements[1]);
            Assert.False(stmt2.Condition.HasAnnotations(RuleConstants.RewriteAnnotationKind));
            // But (k || l) should be marked
            var binary = (BinaryExpressionSyntax)stmt2.Condition;
            Assert.True(binary.Left.HasAnnotations(RuleConstants.RewriteAnnotationKind));

            // Scene 3: k && l && h -> Root && marked
            var stmt3 = (IfStatementSyntax)rule.MarkStatement(statements[2]);
            Assert.True(stmt3.Condition.HasAnnotations(RuleConstants.RewriteAnnotationKind));

            // Scene 4: (k || l) || (f && g) -> Root || NOT marked
            // Rationale: Inner (k || l) is marked but NOT infectious (|| is isolation zone).
            // So it does not propagate to the outer ||.
            var stmt4 = (IfStatementSyntax)rule.MarkStatement(statements[3]);
            Assert.False(stmt4.Condition.HasAnnotations(RuleConstants.RewriteAnnotationKind));
        }

        [Fact]
        public async Task ControlFlowMarkRule_ShouldDeleteMarkedStatements()
        {
            var source = @"
class TestClass {
    void Method() {
        if (true) { }
        while (true) { }
        return 1;
    }
}";
            var (root, _) = await GetCompilationAsync(source).ConfigureAwait(false);
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            var statements = method.Body.Statements.ToList();

            // Manually mark conditions/expressions
            var ifStmt = (IfStatementSyntax)statements[0];
            var markedIf = ifStmt.ReplaceNode(ifStmt.Condition,
                ifStmt.Condition.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind)));

            var whileStmt = (WhileStatementSyntax)statements[1];
            var markedWhile = whileStmt.ReplaceNode(whileStmt.Condition,
                whileStmt.Condition.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind)));

            var returnStmt = (ReturnStatementSyntax)statements[2];
            var markedReturn = returnStmt.ReplaceNode(returnStmt.Expression,
                returnStmt.Expression.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind)));

            var rule = new ControlFlowMarkRule();

            var resultIf = rule.MarkStatement(markedIf);
            Assert.Contains(resultIf.GetAnnotations(RuleConstants.RewriteAnnotationKind), a => a.Data.Contains(RuleConstants.ActionDelete));

            var resultWhile = rule.MarkStatement(markedWhile);
            Assert.Contains(resultWhile.GetAnnotations(RuleConstants.RewriteAnnotationKind), a => a.Data.Contains(RuleConstants.ActionDelete));

            var resultReturn = rule.MarkStatement(markedReturn);
            Assert.Contains(resultReturn.GetAnnotations(RuleConstants.RewriteAnnotationKind), a => a.Data.Contains(RuleConstants.ActionDelete));
        }

        [Fact]
        public async Task StatementMarkRule_ShouldMarkStatementsWithAnnotations()
        {
            var source = @"
class TestClass {
    void Method() {
        var x = 1;
        if (x > 0) { }
    }
}";
            var (root, _) = await GetCompilationAsync(source).ConfigureAwait(false);

            // Manually add annotation to 'x' identifier
            var variable = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().First();
            var markedRoot = root.ReplaceNode(variable, variable.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind)));

            var rule = new StatementMarkRule();
            var result = rule.Apply(markedRoot);

            var localDecl = result.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First();
            Assert.True(localDecl.HasAnnotations(RuleConstants.RewriteAnnotationKind));
        }

        [Fact]
        public async Task RuleEngine_ShouldPropagateTaint()
        {
            var source = @"
class TestClass {
    void Method() {
        var a = 1; // Marked initially
        var b = a; // Should be tainted
        var c = b; // Should be tainted
        var d = 10; // Safe
    }
}";
            var (root, _) = await GetCompilationAsync(source).ConfigureAwait(false);

            // Mark first statement
            var firstStmt = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().First();
            var markedRoot = root.ReplaceNode(firstStmt,
                firstStmt.WithAdditionalAnnotations(new SyntaxAnnotation(RuleConstants.RewriteAnnotationKind, "Source")));

            var engine = new RuleEngine();
            var result = engine.Apply(markedRoot);

            var stmts = result.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().ToList();

            // a=1 (Initial) -> Marked (Reason=Initial Marking)
            Assert.Contains(stmts[0].GetAnnotations(RuleConstants.RewriteAnnotationKind), a => a.Data.Contains(RuleConstants.ActionDelete));

            // b=a (Tainted by a) -> Marked
            Assert.Contains(stmts[1].GetAnnotations(RuleConstants.RewriteAnnotationKind), a => a.Data.Contains(RuleConstants.ActionDelete));

            // c=b (Tainted by b) -> Marked
            Assert.Contains(stmts[2].GetAnnotations(RuleConstants.RewriteAnnotationKind), a => a.Data.Contains(RuleConstants.ActionDelete));

            // d=10 (Safe) -> Not Marked
            Assert.False(stmts[3].HasAnnotations(RuleConstants.RewriteAnnotationKind));
        }
    }
}
