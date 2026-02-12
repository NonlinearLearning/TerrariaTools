using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
using Xunit;
using System.Linq;

namespace TerrariaTools.UnitTests
{
    public class ReferencePropagatorTests
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
        [MemberData(nameof(PropagationTestCases.GetCases), MemberType = typeof(PropagationTestCases))]
        public void Propagation_TestCase(string source, string targetName, string[] expectedMissing, string[] expectedContains, string[] Ignored)
        {
            _ = Ignored;
            _ = expectedContains;
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 查找要标记的目标节点
            var targetNode = root.DescendantNodes()
                .FirstOrDefault(n => (n is VariableDeclaratorSyntax v && v.Identifier.ValueText == targetName) ||
                                     (n is ParameterSyntax p && p.Identifier.ValueText == targetName) ||
                                     (n is MethodDeclarationSyntax m && m.Identifier.ValueText == targetName) ||
                                     (n is PropertyDeclarationSyntax prop && prop.Identifier.ValueText == targetName) ||
                                     (n is FieldDeclarationSyntax f && f.Declaration.Variables.Any(v => v.Identifier.ValueText == targetName)) ||
                                     (n is TypeDeclarationSyntax t && t.Identifier.ValueText == targetName) ||
                                     (n is SingleVariableDesignationSyntax s && s.Identifier.ValueText == targetName) ||
                                     (n is CatchDeclarationSyntax cd && cd.Identifier.ValueText == targetName) ||
                                     (n is FromClauseSyntax fc && fc.Identifier.ValueText == targetName) ||
                                     (n is ForEachStatementSyntax fe && fe.Identifier.ValueText == targetName) ||
                                     (n is IdentifierNameSyntax id && id.Identifier.ValueText == targetName) ||
                                     (n is GenericNameSyntax gn && gn.Identifier.ValueText == targetName));

            Assert.True(targetNode != null, $"Could not find target node with name '{targetName}' in source:\n{source}");

            var result = ExpressionProcessor.RemoveParts(root, n => n == targetNode, model);
            Assert.NotNull(result);
            var resultText = result!.ToFullString();

            foreach (var missing in expectedMissing)
            {
                Assert.DoesNotContain(missing, resultText);
            }

            foreach (var contains in expectedContains)
            {
                if (!string.IsNullOrEmpty(contains))
                {
                    Assert.Contains(contains, resultText);
                }
            }
        }
    }
}