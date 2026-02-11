using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
using Xunit;
using System.Linq;
using System;

namespace TerrariaTools.UnitTests
{
    public class UpwardMarkCollectorTests
    {
        private SemanticModel GetSemanticModel(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create("Test")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(tree);
            return compilation.GetSemanticModel(tree);
        }

        [Theory]
        [MemberData(nameof(UpwardPropagationTestCases.GetCases), MemberType = typeof(UpwardPropagationTestCases))]
        public void UpwardPropagation_TestCase(string name, string source, string targetPredicateName, string[] expectedMissing, string[] expectedContains)
        {
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            Func<SyntaxNode, bool> predicate = targetPredicateName switch
            {
                "Literal_1" => n => n is LiteralExpressionSyntax l && l.Token.ValueText == "1",
                "AllLiterals" => n => n is LiteralExpressionSyntax,
                "Invocation" => n => n is InvocationExpressionSyntax,
                "IfCondition_b" => n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "b" && n.Parent is IfStatementSyntax,
                "Identifier_i" => n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "i",
                "Identifier_c" => n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "c",
                _ => throw new ArgumentException($"Unknown predicate: {targetPredicateName}")
            };

            var result = ExpressionProcessor.RemoveParts(root, predicate, model);
            var resultText = result.ToFullString();

            foreach (var missing in expectedMissing)
            {
                Assert.DoesNotContain(missing, resultText);
            }

            foreach (var contains in expectedContains)
            {
                Assert.Contains(contains, resultText);
            }
        }
    }
}