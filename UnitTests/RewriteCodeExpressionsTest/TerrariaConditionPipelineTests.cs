using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.UnitTests.Infrastructure;
using TerrariaTools.UnitTests.Scenarios;
using Xunit;
using System.Linq;
using System.Threading.Tasks;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

/// <summary>
/// 迁移自 TerrariaConditionRewriterTests.cs，验证 TerrariaConditionLayer 的语义重写能力。
/// </summary>
public class TerrariaConditionPipelineTests : RoslynTestBase
{
    [Fact]
    public async Task Pipeline_ShouldRecognizeNetModeSymbol()
    {
        // SemanticCheck_RecognizesNetModeSymbol
        string source = SharedScenarios.TerrariaConditions.SimpleNetMode;

        var result = await RunPipelineWithNodesAsync(source, _ => Enumerable.Empty<SyntaxNode>());

        Assert.DoesNotContain("if", result);
        Assert.DoesNotContain("Do()", result);
    }

    [Fact]
    public async Task Pipeline_ShouldHandleLiteralOnRight()
    {
        // SemanticCheck_LiteralOnRight
        string source = SharedScenarios.TerrariaConditions.LiteralOnRight;

        var result = await RunPipelineWithNodesAsync(source, _ => Enumerable.Empty<SyntaxNode>());

        Assert.DoesNotContain("if", result);
    }

    [Fact]
    public async Task Pipeline_ShouldHandleMainNetMode()
    {
        // SemanticCheck_MainNetMode
        string source = SharedScenarios.TerrariaConditions.MainNetMode;

        var result = await RunPipelineWithNodesAsync(source, _ => Enumerable.Empty<SyntaxNode>());

        Assert.DoesNotContain("if (Terraria.Main.netMode == 1)", result);
    }

    [Fact]
    public async Task Pipeline_ShouldHandleMultipleConditions_And()
    {
        // MultipleConditions_And
        string source = SharedScenarios.TerrariaConditions.AndConditions;

        var result = await RunPipelineWithNodesAsync(source, _ => Enumerable.Empty<SyntaxNode>());

        // 整个 if 被删除，因为 A && false && B 为 false
        Assert.DoesNotContain("if", result);
    }

    [Fact]
    public async Task Pipeline_ShouldHandleMultipleConditions_Or()
    {
        // MultipleConditions_Or
        string source = SharedScenarios.TerrariaConditions.OrConditions;

        var result = await RunPipelineWithNodesAsync(source, _ => Enumerable.Empty<SyntaxNode>());

        // netMode == 1 被替换为 false，A || false || B 简化为 A || B
        Assert.Contains("if (A || B)", result);
    }

    [Fact]
    public async Task Pipeline_ShouldHandleIfElse_PromoteElse()
    {
        // HandleIfElse_PromoteElse
        string source = SharedScenarios.TerrariaConditions.IfElsePromote;

        var result = await RunPipelineWithNodesAsync(source, _ => Enumerable.Empty<SyntaxNode>());

        Assert.Contains("ServerOnly();", result);
        Assert.DoesNotContain("if", result);
        Assert.DoesNotContain("ClientOnly();", result);
    }

    [Fact]
    public async Task Pipeline_ShouldHandleIfElseIf_PromoteElseIf()
    {
        // HandleIfElseIf_PromoteElseIf
        string source = SharedScenarios.TerrariaConditions.IfElseIfPromote;

        var result = await RunPipelineWithNodesAsync(source, _ => Enumerable.Empty<SyntaxNode>());

        Assert.Contains("if (A)", result);
        Assert.Contains("DoSomething();", result);
        Assert.DoesNotContain("ClientOnly();", result);
    }
}
