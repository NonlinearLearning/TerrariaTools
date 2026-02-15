using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
using Xunit;
using System;

namespace TerrariaTools.UnitTests
{
    public class TerrariaConditionRewriterTests
    {
        private string Rewrite(string source)
        {
            var model = GetModel(source);
            var root = model.SyntaxTree.GetRoot();
            var rewriter = new TerrariaConditionRewriter(model, "netMode", 1);
            var result = rewriter.Visit(root);

            // 重新获取 Method 内部
            var method = result.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .First(m => m.Identifier.Text == "Method");
            string bodyText = method.Body?.ToFullString() ?? "";
            return bodyText.Replace(" ", "").Replace("\n", "").Replace("\r", "");
        }

        private string Rewrite(string source, SemanticModel model)
        {
            var root = model.SyntaxTree.GetRoot();
            var rewriter = new TerrariaConditionRewriter(model, "netMode", 1);
            var result = rewriter.Visit(root);

            // 重新获取 Method 内部
            var method = result.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .First(m => m.Identifier.Text == "Method");
            string bodyText = method.Body?.ToFullString() ?? "";
            return bodyText.Replace(" ", "").Replace("\n", "").Replace("\r", "");
        }

        private SemanticModel GetModel(string source)
        {
            string fullSource = $@"
using System;
class Test {{
    public int netMode;
    public bool A;
    public bool B;
    void Do() {{ }}
    void DoSomething() {{ }}
    void ClientOnly() {{ }}
    void ServerOnly() {{ }}
    void SomethingElse() {{ }}
    void Method() {{
        {source}
    }}
}}";
            var syntaxTree = CSharpSyntaxTree.ParseText(fullSource);
            var compilation = CSharpCompilation.Create("TestAssembly")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(syntaxTree);

            var diagnostics = compilation.GetDiagnostics();
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var errors = string.Join("\n", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
                throw new Exception("Compilation errors in test source:\n" + errors + "\nSource:\n" + fullSource);
            }

            return compilation.GetSemanticModel(syntaxTree);
        }

        [Fact]
        public void SemanticCheck_RecognizesNetModeSymbol()
        {
            // 语义模型能识别 this.netMode
            string source = "if (this.netMode == 1) { Do(); }";
            string result = Rewrite(source);

            Assert.DoesNotContain("if", result);
            Assert.DoesNotContain("Do()", result);
        }

        [Fact]
        public void SemanticCheck_LiteralOnRight()
        {
            // 语义模型能识别 1 == netMode
            string source = "if (1 == netMode) { Do(); }";
            string result = Rewrite(source);

            Assert.DoesNotContain("if", result);
        }

        [Fact]
        public void SemanticCheck_MainNetMode()
        {
            string fullSource = @"
namespace Terraria {
    public class Main {
        public static int netMode;
    }
}
class Test {
    void Do() { }
    void Method() {
        if (Terraria.Main.netMode == 1) { Do(); }
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(fullSource);
            var compilation = CSharpCompilation.Create("TestAssembly")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(syntaxTree);
            var model = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            var rewriter = new TerrariaConditionRewriter(model, "netMode", 1);
            var result = rewriter.Visit(root);

            string normalized = result.ToFullString().Replace(" ", "").Replace("\n", "").Replace("\r", "");
            Assert.DoesNotContain("if(Terraria.Main.netMode==1)", normalized);
        }

        [Fact]
        public void SimpleExpression()
        {
            string source = "if (netMode == 1) { Do(); }";
            string result = Rewrite(source);
            Assert.Equal("{}", result);
        }

        [Fact]
        public void MultipleConditions_And()
        {
            string source = "if (A && netMode == 1 && B) { Do(); }";
            string result = Rewrite(source);
            Assert.Equal("{}", result);
        }

        [Fact]
        public void MultipleConditions_Or()
        {
            string source = "if (A || netMode == 1 || B) { Do(); }";
            string result = Rewrite(source);
            Assert.Contains("if(A||B)", result);
        }

        [Fact]
        public void KeepIfWithOr()
        {
            string source = "if (A || netMode == 1) { Do(); }";
            string result = Rewrite(source);
            Assert.Contains("if(A)", result);
        }

        [Fact]
        public void HandleIfElse_PromoteElse()
        {
            string source = "if (netMode == 1) { ClientOnly(); } else { ServerOnly(); }";
            string result = Rewrite(source);
            Assert.Contains("ServerOnly();", result);
            Assert.DoesNotContain("if", result);
        }

        [Fact]
        public void HandleIfElseIf_PromoteElseIf()
        {
            string source = "if (netMode == 1) { ClientOnly(); } else if (A) { DoSomething(); }";
            string result = Rewrite(source);
            Assert.Contains("if(A)", result);
            Assert.Contains("DoSomething();", result);
        }

        [Fact]
        public void ComplexExpression()
        {
            string source = "if ((A || netMode == 1) && B) { Do(); }";
            string result = Rewrite(source);
            Assert.Contains("if(A&&B)", result);
        }
    }
}
