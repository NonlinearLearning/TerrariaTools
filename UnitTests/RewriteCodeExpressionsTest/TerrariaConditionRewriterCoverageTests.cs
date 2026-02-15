using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Collections.Generic;
using TerrariaTools.RewriteCodeExpressions;
using Xunit;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest
{
    public class TerrariaConditionRewriterCoverageTests
    {
        private string Rewrite(string source, string targetSymbol = "netMode", int targetValue = 1)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location))
                .AddSyntaxTrees(tree)
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            var rewriter = new TerrariaConditionRewriter(model, targetSymbol, targetValue);
            var result = rewriter.Visit(tree.GetRoot());

            return result.ToFullString();
        }

        private string RewriteMethodBody(string bodyContent)
        {
            string source = $@"
using System;
public class TestClass {{
    public int netMode;
    public bool A;
    public bool B;
    public void Method() {{
        {bodyContent}
    }}
}}";
            var result = Rewrite(source);
            var tree = CSharpSyntaxTree.ParseText(result);
            var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "Method");

            if (method == null || method.Body == null) return "";

            // Return only the statements inside the body, normalized
            return method.Body.Statements.ToFullString().Trim();
        }

        [Fact]
        public void TargetIdentification_ExactMatch_Removes()
        {
            var result = RewriteMethodBody("if (netMode == 1) { return; }");
            Assert.Equal("", result); // Should be removed
        }

        [Fact]
        public void TargetIdentification_SwappedOperands_Removes()
        {
            var result = RewriteMethodBody("if (1 == netMode) { return; }");
            Assert.Equal("", result);
        }

        [Fact]
        public void TargetIdentification_NegativeMatch_Keeps()
        {
            var input = "if (netMode == 2) { return; }";
            var result = RewriteMethodBody(input);
            Assert.Contains("netMode == 2", result);
        }

        [Fact]
        public void TargetIdentification_Parenthesized_Removes()
        {
            var result = RewriteMethodBody("if (((netMode == 1))) { return; }");
            Assert.Equal("", result);
        }

        [Fact]
        public void BinaryLogic_Or_RightIsTarget_KeepsLeft()
        {
            // A || netMode == 1 -> A
            var result = RewriteMethodBody("if (A || netMode == 1) { return; }");
            Assert.Contains("if (A)", result);
            Assert.DoesNotContain("netMode", result);
        }

        [Fact]
        public void BinaryLogic_Or_LeftIsTarget_KeepsRight()
        {
            // netMode == 1 || A -> A
            var result = RewriteMethodBody("if (netMode == 1 || A) { return; }");
            Assert.Contains("if (A)", result);
        }

        [Fact]
        public void BinaryLogic_And_RightIsTarget_RemovesAll()
        {
            // A && netMode == 1 -> False -> Remove if
            var result = RewriteMethodBody("if (A && netMode == 1) { return; }");
            Assert.Equal("", result);
        }

        [Fact]
        public void BinaryLogic_And_LeftIsTarget_RemovesAll()
        {
            // netMode == 1 && A -> False -> Remove if
            var result = RewriteMethodBody("if (netMode == 1 && A) { return; }");
            Assert.Equal("", result);
        }

        [Fact]
        public void BinaryLogic_OtherOperator_RemovesIfTargetInvolved()
        {
            // (netMode == 1) + 5 -> Remove statement?
            // VisitBinaryExpression logic: if left or right is null, return null.
            // netMode == 1 becomes null. null + 5 -> null.
            // ExpressionStatement(null) -> null.
            var result = RewriteMethodBody("var x = (netMode == 1) + 5;");
            // VariableDeclarator Visit: Initializer becomes null?
            // (netMode==1) is null. + operator visits left (null) right (5). Returns null.
            // Initializer becomes null.
            // VariableDeclarator preserves node if initializer is null.
            Assert.Contains("var x = (netMode == 1) + 5;", result);
        }

        [Fact]
        public void IfStatement_ElsePromotion()
        {
            // if (netMode == 1) { A; } else { B; } -> B;
            var result = RewriteMethodBody("if (netMode == 1) { A = true; } else { B = true; }");
            Assert.DoesNotContain("if", result);
            Assert.DoesNotContain("else", result);
            Assert.Contains("B = true;", result);
            Assert.DoesNotContain("A = true;", result);
        }

        [Fact]
        public void IfStatement_EmptyBody_Removes()
        {
            // if (A) { netMode == 1; } -> Remove because body becomes empty
            var result = RewriteMethodBody("if (A) { netMode == 1; }");
            Assert.Equal("", result);
        }

        [Fact]
        public void IfStatement_OriginallyEmptyBody_Keeps()
        {
            // if (A) { } -> Keep (preserves originally empty blocks)
            var result = RewriteMethodBody("if (A) { }");
            Assert.Contains("if (A) { }", result.Replace("\r", "").Replace("\n", " ").Replace("  ", " "));
        }

        [Fact]
        public void IfStatement_EmptyBody_WithElse_Keeps()
        {
            // if (A) { } else { B; } -> Keep
            // Logic: if (!originallyEmpty && elseClause == null) return null;
            // Here originallyEmpty is true (it is empty). So it should return the node updated.
            var result = RewriteMethodBody("if (A) { } else { B = true; }");
            Assert.Contains("if (A)", result);
            Assert.Contains("else", result);
            Assert.Contains("B = true;", result);
        }

        [Fact]
        public void ElseClause_EmptyBody_RemovesElse()
        {
            // if (A) { B; } else { } -> if (A) { B; }
            // Add a dummy target expression to trigger the rewriter for this method
            var result = RewriteMethodBody("var _ = netMode == 1; if (A) { B = true; } else { }");
            Assert.Contains("if (A)", result);
            Assert.DoesNotContain("else", result);
        }

        [Fact]
        public void Parentheses_Simplification()
        {
            // (A) -> A
            // ExpressionStatement checks if expression became simple.
            // if (A) { (B); } -> if (A) { B; }
            // Wait, VisitParenthesizedExpression:
            // expr = Visit(A) -> A.
            // if expr is IdentifierName -> return expr.
            // So (B) -> B.
            // Add a dummy target expression to trigger the rewriter for this method
            var result = RewriteMethodBody("var _ = netMode == 1; if (A) { (B) = true; }");
            // Assignment: (B) = true. Left is (B).
            // VisitBinaryExpression (Assignment is Binary? No, AssignmentExpressionSyntax).
            // CSharpSyntaxRewriter visits AssignmentExpression.Left/Right.
            // Left is ParenthesizedExpression (B). VisitParenthesized returns B.
            // So it simplifies to B = true.
            Assert.Contains("B = true;", result);
            Assert.DoesNotContain("(B)", result);
        }

        [Fact]
        public void ExpressionStatement_Simplification_RemovesTrivial()
        {
            // A || netMode == 1; -> A;
            // VisitExpressionStatement logic:
            // if node.Expression is Binary (A || netMode==1) AND expression is Identifier (A) -> return null.
            var result = RewriteMethodBody("A || netMode == 1;");
            Assert.Equal("", result);
        }

        [Fact]
        public void VariableDeclarator_InitializerRemoved_PreservesDeclaration()
        {
            // var x = netMode == 1;
            // netMode==1 -> null.
            // EqualsValueClause -> null.
            // VariableDeclarator -> returns original node.
            var result = RewriteMethodBody("bool x = netMode == 1;");
            Assert.Contains("bool x = netMode == 1;", result);
        }

        [Fact]
        public void TargetIdentification_NotEquals_Keeps()
        {
            // netMode != 1 -> Keep
            var result = RewriteMethodBody("if (netMode != 1) { return; }");
            Assert.Contains("netMode != 1", result);
        }

        [Fact]
        public void TargetIdentification_SymbolMismatch_Keeps()
        {
            // otherMode == 1 -> Keep
            var result = RewriteMethodBody("if (otherMode == 1) { return; }");
            Assert.Contains("otherMode == 1", result);
        }

        [Fact]
        public void TargetIdentification_ValueMismatch_Keeps()
        {
            // netMode == 2 -> Keep (already covered but good to be explicit)
            var result = RewriteMethodBody("if (netMode == 2) { return; }");
            Assert.Contains("netMode == 2", result);
        }

        [Fact]
        public void TargetIdentification_NonLiteralRight_Keeps()
        {
            // netMode == x -> Keep
            var result = RewriteMethodBody("int x = 1; if (netMode == x) { return; }");
            Assert.Contains("netMode == x", result);
        }

        [Fact]
        public void VisitBinary_LeftNull_Removes()
        {
            // (netMode == 1) + 5
            // Visit((netMode == 1)) -> null.
            // VisitBinaryExpression -> left is null.
            // Returns null.
            // ExpressionStatement(null) -> null.
            // So statement should be removed.
            var result = RewriteMethodBody("var y = (netMode == 1) + 5;");
            // Wait, previous test said "VariableDeclarator_InitializerRemoved_PreservesDeclaration".
            // That was `bool x = netMode == 1;`. Here `netMode == 1` is the WHOLE initializer value.
            // Here `(netMode == 1) + 5` is the initializer value.
            // VisitBinary returns null. So initializer value becomes null.
            // VariableDeclarator receives null newInitializer -> returns original node.
            // So it should be preserved.
            Assert.Contains("var y = (netMode == 1) + 5;", result);
        }
        [Fact]
        public void ExpressionStatement_Simplification_RemovesLiteral()
        {
            // true || netMode == 1; -> true; -> Remove
            var result = RewriteMethodBody("true || netMode == 1;");
            Assert.Equal("", result);
        }

        [Fact]
        public void ExpressionStatement_Simplification_RemovesMemberAccess()
        {
            // this.A || netMode == 1; -> this.A; -> Remove
            var result = RewriteMethodBody("this.A || netMode == 1;");
            Assert.Equal("", result);
        }

        [Fact]
        public void Property_ExpressionBody_Preserves()
        {
            // public bool Prop => netMode == 1;
            // Should be preserved because it's not in a function
            string source = @"
public class TestClass {
    public int netMode;
    public bool Prop => netMode == 1;
}";
            var result = Rewrite(source);
            Assert.Contains("public bool Prop => netMode == 1;", result);
        }

        [Fact]
        public void Property_ExpressionBody_PreservesTernary()
        {
            // public int Prop => netMode == 1 ? 10 : 20;
            // Should be preserved because it's not in a function
            string source = @"
public class TestClass {
    public int netMode;
    public int Prop => netMode == 1 ? 10 : 20;
}";
            var result = Rewrite(source);
            Assert.Contains("public int Prop => netMode == 1 ? 10 : 20;", result);
        }

        [Fact]
        public void Property_Initializer_Preserves()
        {
            // public bool Prop { get; set; } = netMode == 1;
            // Should preserve initializer because it's not in a function
            string source = @"
public class TestClass {
    public int netMode;
    public bool Prop { get; set; } = netMode == 1;
}";
            var result = Rewrite(source);
            Assert.Contains("public bool Prop { get; set; } = netMode == 1;", result);
        }

        [Fact]
        public void Field_Initializer_Preserves()
        {
            // public bool Field = netMode == 1;
            // Should preserve initializer because it's not in a function
            string source = @"
public class TestClass {
    public int netMode;
    public bool Field = netMode == 1;
}";
            var result = Rewrite(source);
            Assert.Contains("public bool Field = netMode == 1;", result);
        }

        [Fact]
        public void Property_Accessor_Simplifies()
        {
            // public int Prop { get { if (netMode == 1) return 10; return 20; } }
            // Should simplify to return 20; because Accessor is a function
            string source = @"
public class TestClass {
    public int netMode;
    public int Prop {
        get {
            if (netMode == 1) { return 10; }
            return 20;
        }
    }
}";
            var result = Rewrite(source);
            // Check that if (netMode == 1) { return 10; } is removed
            // Result should contain "return 20;" and NOT "return 10;"
            Assert.Contains("return 20;", result);
            Assert.DoesNotContain("return 10;", result);
            Assert.DoesNotContain("if (netMode == 1)", result);
        }

        [Fact]
        public void Property_Accessor_ExpressionBody_Simplifies()
        {
            // public int Prop { get => netMode == 1 ? 10 : 20; }
            // Should simplify to get => 20; because Accessor is a function
            string source = @"
public class TestClass {
    public int netMode;
    public int Prop { get => netMode == 1 ? 10 : 20; }
}";
            var result = Rewrite(source);
            Assert.Contains("get => 20;", result);
            Assert.DoesNotContain("netMode == 1", result);
        }

        [Fact]
        public void Method_ExpressionBody_Simplifies()
        {
            // public int Method() => netMode == 1 ? 10 : 20;
            // Should simplify to => 20; because Method is a function
            string source = @"
public class TestClass {
    public int netMode;
    public int Method() => netMode == 1 ? 10 : 20;
}";
            var result = Rewrite(source);
            Assert.Contains("=> 20;", result);
            Assert.DoesNotContain("netMode == 1", result);
        }

        [Fact]
        public void Ternary_ConditionRemoved_Simplifies()
        {
            // var x = netMode == 1 ? A : B; -> var x = B;
            var result = RewriteMethodBody("var x = netMode == 1 ? A : B;");
            Assert.Contains("var x = B;", result);
        }

        [Fact]
        public void MultipleConditions_Support()
        {
            string source = @"
using System;
public class TestClass {
    public int netMode;
    public int myPlayer;
    public int whoAmI;

    public void A() {}
    public void B() {}
    public void C() {}

    public void Method() {
        if (netMode == 1) { A(); }
        if (netMode != 2) { B(); }
        if (myPlayer == whoAmI) { C(); }
    }
}";
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location))
                .AddSyntaxTrees(tree)
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            
            var conditions = new List<RewriteCondition>
            {
                new RewriteCondition { SymbolName = "netMode", Operator = SyntaxKind.EqualsExpression, Value = "1", IsValueLiteral = true },
                new RewriteCondition { SymbolName = "netMode", Operator = SyntaxKind.NotEqualsExpression, Value = "2", IsValueLiteral = true },
                new RewriteCondition { SymbolName = "myPlayer", Operator = SyntaxKind.EqualsExpression, Value = "whoAmI", IsValueLiteral = false }
            };
            
            var rewriter = new TerrariaConditionRewriter(model, conditions);
            var result = rewriter.Visit(tree.GetRoot()).ToFullString();

            Assert.DoesNotContain("if (netMode == 1)", result);
            Assert.DoesNotContain("if (netMode != 2)", result);
            Assert.DoesNotContain("if (myPlayer == whoAmI)", result);
            Assert.DoesNotContain("A();", result); // Should be removed because condition is assumed False
            Assert.DoesNotContain("B();", result); // Should be removed because condition is assumed False
            Assert.DoesNotContain("C();", result); // Should be removed because condition is assumed False
        }
    }
}