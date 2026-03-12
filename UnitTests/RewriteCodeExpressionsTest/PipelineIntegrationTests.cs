using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.UnitTests.Infrastructure;
using TerrariaTools.UnitTests.Scenarios;
using Xunit;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

/// <summary>
/// 演示如何使用新的测试基础设施进行集成测试
/// </summary>
public class PipelineIntegrationTests : RoslynTestBase
{
    [Fact]
    public async Task Pipeline_ShouldRemoveMarkedWhileStatement()
    {
        // 使用新的 Scenario 模式
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.WhileLoop);

        await Given(source, "RemoveWhile")
        .WhenMarking<WhileStatementSyntax>()
        .Then(result => {
            Assert.DoesNotContain("while", result);
            Assert.Contains("Console.WriteLine", result);
        });
    }

    [Fact]
    public async Task Pipeline_WhileVariants_SimpleWhile()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.WhileLoopVariants.SimpleWhile);
        await Given(source, "SimpleWhile")
        .WhenMarking<WhileStatementSyntax>()
        .Then(result => Assert.DoesNotContain("while", result));
    }

    [Fact]
    public async Task Pipeline_WhileVariants_DoWhile()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.WhileLoopVariants.DoWhile);
        await Given(source, "DoWhile")
        .WhenMarking<DoStatementSyntax>()
        .Then(result => Assert.DoesNotContain("do", result));
    }

    [Fact]
    public async Task Pipeline_WhileVariants_WhileWithContinue()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.WhileLoopVariants.WhileWithContinue);
        await Given(source, "WhileWithContinue")
        .WhenMarking<WhileStatementSyntax>()
        .Then(result => {
            Assert.DoesNotContain("while", result);
            Assert.DoesNotContain("continue", result);
        });
    }

    [Fact]
    public async Task Pipeline_WhileVariants_NestedWhile()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.WhileLoopVariants.NestedWhile);
        await Given(source, "NestedWhile")
        .WhenMarking<WhileStatementSyntax>() // Marks both
        .Then(result => Assert.DoesNotContain("while", result));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Pipeline_StressTest_NestedWhiles(int depth)
    {
        // 1. 生成嵌套的 while 语句作为压力测试
        var nestedBody = BogusTestDataGenerator.GenerateNestedWhile(depth);
        var source = BogusTestDataGenerator.GenerateFullClass(nestedBody);

        await Given(source, "StressTest_NestedWhiles")
        .WhenMarking<WhileStatementSyntax>()
        .Then(result => {
            Assert.DoesNotContain("while", result);
        });
    }

    [Fact]
    public async Task Pipeline_ShouldPromoteElseWhenIfStatementTrueBranchIsRemoved()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.IfElse);

        await Given(source, "PromoteElse")
        .WhenMarking(root => {
            var ifStmt = root.DescendantNodes().OfType<IfStatementSyntax>().First();
            return new SyntaxNode[] { ifStmt.Statement };
        })
        .Then(result => {
            Assert.DoesNotContain("if", result);
            Assert.DoesNotContain("True", result);
            Assert.Contains("False", result);
        });
    }

    [Fact]
    public async Task Pipeline_IfVariants_SimpleIf()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.IfElseVariants.SimpleIf);
        await Given(source, "SimpleIf")
        .WhenMarking<IfStatementSyntax>()
        .Then(result => Assert.DoesNotContain("if", result));
    }

    [Fact]
    public async Task Pipeline_IfVariants_IfElseChain()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.IfElseVariants.IfElseChain);
        await Given(source, "IfElseChain")
        .WhenMarking(root => {
            // Mark the entire if chain (by marking the top level statement)
            return new[] { root.DescendantNodes().OfType<IfStatementSyntax>().First() };
        })
        .Then(result => Assert.DoesNotContain("if", result));
    }

    [Fact]
    public async Task Pipeline_IfVariants_NestedIf()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.IfElseVariants.NestedIf);
        await Given(source, "NestedIf")
        .WhenMarking<IfStatementSyntax>()
        .Then(result => Assert.DoesNotContain("if", result));
    }

    [Fact]
    public async Task Pipeline_IfVariants_TernaryExpression()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.IfElseVariants.TernaryExpression);
        await Given(source, "TernaryExpression")
        .WhenMarking<ConditionalExpressionSyntax>()
        .Then(result => {
            // Conditional expression should be replaced by a placeholder (default(T))
            // or if it's an assignment, the expression side is replaced.
            // Our PipelineExpressionSimplifier replaces marked expressions with placeholders.
            Assert.DoesNotContain("?", result);
            Assert.DoesNotContain(":", result);
        });
    }

    [Fact]
    public async Task Pipeline_ShouldMergeBinaryLogicalExpressionWhenOneSideIsRemoved()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.LogicalAnd);

        await Given(source, "MergeLogicalAnd")
        .WhenMarking(root => {
            var binary = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                .First(b => b.Kind() == Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalAndExpression);
            return new SyntaxNode[] { binary.Left };
        })
        .Then(result => {
            Assert.Contains("if (b)", result);
        });
    }

    [Fact]
    public async Task Pipeline_Differential_ConditionalAccess_MergeRight()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.ConditionalAccess);

        await Given(source, "ConditionalAccess_MergeRight")
        .WhenMarking(root => {
            return root.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>()
                .Where(ca => ca.Expression.ToString() == "obj")
                .Select(ca => ca.Expression);
        })
        .ThenDifferential("ConditionalAccess 场景");
    }

    [Fact]
    public async Task Pipeline_Differential_MethodGroup_Overloads()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.MethodGroupOverloads);

        await Given(source, "MethodGroup_Overloads")
        .WhenMarking(root => {
            return root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.ValueText == "Do" && m.ParameterList.Parameters.Any(p => p.Type.ToString() == "int"));
        })
        .ThenDifferential("方法组重载场景");
    }

    [Fact]
    public async Task Pipeline_Differential_VariableDeclarator_Usage()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.VariableDeclarator);

        await Given(source, "VariableDeclarator_Usage")
        .WhenMarking<VariableDeclaratorSyntax>(v => v.Identifier.ValueText == "x")
        .Then(result =>
        {
            Assert.DoesNotContain("int x;", result);
            Assert.DoesNotContain("int;", result);
        });
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceArithmeticBinarySideWithPlaceholder()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.ArithmeticAddition);

        await Given(source, "ReplaceArithmeticSide")
        .WhenMarking(root => {
            var binary = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                .First(b => b.Kind() == Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddExpression);
            return new SyntaxNode[] { binary.Left };
        })
        .Then(result => {
            Assert.Contains("0 + y", result);
        });
    }

    [Fact]
    public async Task Pipeline_ShouldRemoveTryStatementWhenAllHandlersAreRemoved()
    {
        var source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.TryCatchFinally);

        await Given(source, "RemoveTry")
        .WhenMarking(root => {
            var tryStmt = root.DescendantNodes().OfType<TryStatementSyntax>().First();
            var toRemove = new List<SyntaxNode>();
            toRemove.AddRange(tryStmt.Catches);
            if (tryStmt.Finally != null) toRemove.Add(tryStmt.Finally);
            return toRemove;
        })
        .Then(result => {
            Assert.DoesNotContain("try", result);
            Assert.DoesNotContain("catch", result);
            Assert.DoesNotContain("finally", result);
        });
    }
}
