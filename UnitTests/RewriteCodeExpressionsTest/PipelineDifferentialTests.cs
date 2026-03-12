using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.UnitTests.Infrastructure;
using TerrariaTools.UnitTests.Scenarios;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.RewriteCodeExpressions.Pipeline;
using TerrariaTools.ConsistentBehaviorGuarantee;
using TerrariaTools.Diagnostics;
using Xunit;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

/// <summary>
/// 差异化测试：对比新版 Pipeline 架构与原版 ExpressionSimplifier 的行为一致性。
/// </summary>
public class PipelineDifferentialTests : RoslynTestBase
{
    [Theory]
    [InlineData("int x = 10; if (x > 0) { Console.WriteLine(x); }", "x > 0")]
    [InlineData("bool a = true, b = false; if (a && b) { }", "a")]
    [InlineData("try { Do(); } catch (Exception ex) { Log(ex); } finally { Cleanup(); }", "ex")]
    public async Task Pipeline_ShouldMatchOriginalSimplifierBehavior(string codeBody, string nodeToMarkSearch)
    {
        var source = BogusTestDataGenerator.GenerateFullClass(codeBody);
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync();

        // 查找要标记的节点
        var nodeToMark = root.DescendantNodes()
            .First(n => n.ToString().Contains(nodeToMarkSearch));
        var nodesToMark = new HashSet<SyntaxNode> { nodeToMark };

        // 1. 运行原版 ExpressionSimplifier
        var oldTrace = new RewritingTraceContext();
        var oldRewriter = new ExpressionSimplifier(_ => false, model, nodesToMark, oldTrace);
        var oldResultRoot = oldRewriter.Visit(root);
        var oldResult = oldResultRoot.ToFullString();

        // 2. 运行新版 PipelineExpressionSimplifier
        var newTrace = new RewritingTraceContext();
        var newResultRoot = await PipelineExpressionSimplifier.RewriteAsync(root, model, null, _ => false, nodesToMark, null, default, newTrace);
        var newResult = newResultRoot.ToFullString();

        // 3. 使用 DifferentialTester 进行对比
        var diffTester = new DifferentialTester(newTrace);

        // 我们期望结果在功能上一致（忽略格式差异，格式由 PostProcessingLayer 统一处理）
        // 为了公平对比，我们将 oldResult 也进行格式化，或者只对比关键结构
        var normalizedOld = oldResultRoot.NormalizeWhitespace().ToFullString();
        var normalizedNew = newResultRoot.NormalizeWhitespace().ToFullString();

        bool isMatch = diffTester.Compare(normalizedOld, normalizedNew, $"DifferentialTest: {nodeToMarkSearch}");

        // 如果不匹配，输出差异以供调试
        if (!isMatch)
        {
            var diagnostics = newTrace.GetDiagnostics();
            foreach (var diag in diagnostics)
            {
                System.Diagnostics.Debug.WriteLine(diag.ToString());
            }
        }

        Assert.True(isMatch, $"新旧逻辑输出不一致。输入代码: {codeBody}");
    }
}
