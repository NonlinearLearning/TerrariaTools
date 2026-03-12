using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.UnitTests.Infrastructure;
using TerrariaTools.UnitTests.Scenarios;
using Xunit;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

public class TriviaPreservationTests : RoslynTestBase
{
    [Fact]
    public async Task MergeRight_ShouldPreserveTrivia_ForConditionalAccess()
    {
        // 1. 构建带有注释的条件访问源代码
        var source = BogusTestDataGenerator.GenerateFullClass(@"
            var obj = new { Inner = new { Value = 1 } };
            /* Leading */ obj /* TrailingObj */ ?. /* TrailingOp */ Inner /* TrailingInner */ .Value;
        ");

        // 2. 标记 obj 进行移除，这会触发 MergeRight
        var result = await RunPipelineWithNodesAsync(source, root => {
            return root.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>()
                .Where(ca => ca.Expression.ToString() == "obj")
                .Select(ca => ca.Expression);
        });

        // 3. 验证所有注释都保留在结果中
        Assert.Contains("/* Leading */", result);
        Assert.Contains("/* TrailingObj */", result);
        Assert.Contains("/* TrailingOp */", result);
        Assert.Contains("Inner", result);
        Assert.Contains("/* TrailingInner */", result);
        Assert.Contains("Value", result);
    }

    [Fact]
    public async Task MergeRight_ShouldPreserveTrivia_ForBinaryExpression()
    {
        // 1. 构建带有注释的二元表达式
        var source = BogusTestDataGenerator.GenerateFullClass(@"
            bool a = true, b = false;
            if (/* LeadingA */ a /* TrailingA */ && /* TrailingOp */ b) { }
        ");

        // 2. 标记左侧 a 进行移除
        var result = await RunPipelineWithNodesAsync(source, root => {
            var binary = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                .First(b => b.Kind() == Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalAndExpression);
            return new SyntaxNode[] { binary.Left };
        });

        // 3. 验证注释是否被保留
        Assert.Contains("/* LeadingA */", result);
        Assert.Contains("/* TrailingA */", result);
        Assert.Contains("/* TrailingOp */", result);
    }
}
