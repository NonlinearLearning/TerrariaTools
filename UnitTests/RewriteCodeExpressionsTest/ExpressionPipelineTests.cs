using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.UnitTests.Infrastructure;
using TerrariaTools.UnitTests.Scenarios;
using Xunit;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using TerrariaTools.RewriteCodeExpressions.Pipeline;
using System;
using System.Text;

namespace TerrariaTools.UnitTests.RewriteCodeExpressionsTest;

/// <summary>
/// 迁移�?ExpressionTests.cs，使用新的管道集成测试基础设施�?
/// </summary>
public class ExpressionPipelineTests : RoslynTestBase
{
    [Fact]
    public async Task Pipeline_ShouldReplaceStringArgumentWithEmptyString()
    {
        // HandleArgument_RemoveObjectArgument_ReplacesWithNull
        string source = BogusTestDataGenerator.GenerateFullClass(@"
            void Foo(string s) { }
            void Bar() { Foo(""hello""); }
        ");

        var result = await RunPipelineWithNodesAsync(source, root => {
            return root.DescendantNodes().OfType<ArgumentSyntax>()
                .Where(a => a.Expression is LiteralExpressionSyntax l && l.Token.ValueText == "hello");
        });

        Assert.Contains("Foo(string.Empty)", result);
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceIntArgumentWithZero()
    {
        // HandleArgument_RemoveValueTypeArgument_ReplacesWithCastZero
        string source = BogusTestDataGenerator.GenerateFullClass(@"
            void Foo(int x) { }
            void Bar() { Foo(123); }
        ");

        var result = await RunPipelineWithNodesAsync(source, root => {
            return root.DescendantNodes().OfType<ArgumentSyntax>()
                .Where(a => a.Expression is LiteralExpressionSyntax l && l.Token.ValueText == "123");
        });

        Assert.Contains("Foo(0)", result);
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceReturnValueTypeWithZero()
    {
        // ReturnStatement_ValueType_ReplacesWithCastZero
        string source = BogusTestDataGenerator.GenerateFullClass(@"
            public int GetInt() {
                int k = 10;
                return k;
            }
        ");

        var result = await RunPipelineWithNodesAsync(source, root => {
            var returnStmt = root.DescendantNodes().OfType<ReturnStatementSyntax>().First();
            return new[] { returnStmt.Expression! };
        });

        Assert.Contains("return 0;", result);
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceReturnReferenceTypeWithEmptyString()
    {
        // ReturnStatement_ReferenceType_ReplacesWithNull
        string source = BogusTestDataGenerator.GenerateFullClass(@"
            public string GetString() {
                string s = ""hello"";
                return s;
            }
        ");

        var result = await RunPipelineWithNodesAsync(source, root => {
            var returnStmt = root.DescendantNodes().OfType<ReturnStatementSyntax>().First();
            return new[] { returnStmt.Expression! };
        });

        Assert.Contains("return string.Empty;", result);
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceAttributeArgumentWithPlaceholder()
    {
        // AttributeArgument_ReplacesWithPlaceholder
        string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.ExpressionPipelineTests_Source_1;

        var result = await RunPipelineWithNodesAsync(source, root => {
            var attrArg = root.DescendantNodes().OfType<AttributeArgumentSyntax>().First();
            return new[] { attrArg.Expression };
        });

        Assert.Contains("[MyAttr(0, \"keep\")]", result);
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceAnonymousObjectMemberWithPlaceholder()
    {
        // AnonymousObject_ReplacesWithPlaceholder
        string source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.ComplexExpressions.AnonymousObject);

        var result = await RunPipelineWithNodesAsync(source, root => {
            var declarator = root.DescendantNodes().OfType<AnonymousObjectMemberDeclaratorSyntax>().First();
            return new[] { declarator.Expression };
        });

        Assert.Contains("A = 0", result);
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceTupleElementWithPlaceholder()
    {
        // Tuple_ReplacesWithPlaceholder
        string source = BogusTestDataGenerator.GenerateFullClass(SharedScenarios.ComplexExpressions.TupleExpression);

        var result = await RunPipelineWithNodesAsync(source, root => {
            var tuple = root.DescendantNodes().OfType<TupleExpressionSyntax>().First();
            return new[] { tuple.Arguments[1].Expression };
        });

        Assert.Contains("(1, string.Empty)", result);
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceObjectInitializerMemberWithPlaceholder()
    {
        // ObjectInitializer_ReplacesWithPlaceholder
        string source = SharedScenarios.ComplexExpressions.ObjectInitializer;

        var result = await RunPipelineWithNodesAsync(source, root => {
            var assignment = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().First(a => a.Left.ToString() == "Name");
            return new[] { assignment.Right };
        });

        Assert.Contains("Name = string.Empty", result);
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceBooleanReturnWithFalse()
    {
        // ReturnStatement_Placeholder_Boolean
        string source = SharedScenarios.ComplexExpressions.ReturnBoolean;

        var result = await RunPipelineWithNodesAsync(source, root => {
            var memberAccess = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .First(m => m.ToString().Contains("AllowExecution"));
            return new[] { memberAccess };
        });

        Assert.Contains("return false;", result);
    }

    [Fact]
    public async Task Pipeline_ShouldSupportCascadingDeletions()
    {
        // VariableDeletion_Isolation_BetweenMethods + Cascading (Simplified)
        string source = SharedScenarios.ComplexExpressions.IsolationBetweenMethods;

        var result = await RunPipelineWithNodesAsync(source, root => {
            var method1 = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.ValueText == "Method1");
            var v1Decl = method1.DescendantNodes().OfType<VariableDeclaratorSyntax>().First();
            return new[] { v1Decl.Initializer!.Value };
        });

        // 验证 Method1 中的 v �?x 声明被删�?
        Assert.DoesNotContain("int v = 1;", result);
        Assert.DoesNotContain("int x = v + 1;", result);

        // 验证 Method2 中的 v 依然存在
        Assert.Contains("int v = 5;", result);
        Assert.Contains("int y = v + 1;", result);
    }

    [Fact]
    public async Task Pipeline_ShouldSupportCascadingAssignmentDeletion()
    {
        string source = SharedScenarios.ComplexExpressions.CascadingAssignment;

        var result = await RunPipelineWithNodesAsync(source, root => {
            return root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Where(m => m.ToString().Contains("PlayerInput.SomeValue") || m.ToString().Contains("PlayerInput.OtherValue"));
        });

        // 预期 num2 被完全删�?
        Assert.DoesNotContain("int num2", result);
        Assert.DoesNotContain("num2 =", result);
        Assert.DoesNotContain("num2 !=", result);
    }

    [Fact]
    public async Task Pipeline_ShouldSupportCascadingBinaryDeletion()
    {
        string source = SharedScenarios.ComplexExpressions.CascadingBinary;

        var result = await RunPipelineWithNodesAsync(source, root => {
            return root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Where(m => m.ToString().Contains("PlayerInput"));
        });

        // num2 应该被级联删�?
        Assert.DoesNotContain("num2", result);
    }

    [Fact]
    public async Task Pipeline_ShouldMergeIfToElseWhenConditionIsRemoved()
    {
        string source = BogusTestDataGenerator.GenerateFullClass(@"
            void M(bool cond) {
                if (cond) {
                    Console.WriteLine(""true"");
                } else {
                    Console.WriteLine(""false"");
                }
            }
        ");

        var result = await RunPipelineWithNodesAsync(source, root => {
            var ifStmt = root.DescendantNodes().OfType<IfStatementSyntax>().First();
            // 标记 Condition �?Remove
            return new[] { ifStmt.Condition };
        });

        Assert.DoesNotContain("if (cond)", result);
        Assert.Contains("Console.WriteLine(\"false\");", result);
        Assert.DoesNotContain("Console.WriteLine(\"true\");", result);
    }

    [Fact]
    public async Task Pipeline_ShouldMergeTryToBlockWhenCatchAndFinallyAreRemoved()
    {
        string source = BogusTestDataGenerator.GenerateFullClass(@"
            void M() {
                try {
                    Console.WriteLine(""try"");
                } catch (Exception) {
                    Console.WriteLine(""catch"");
                } finally {
                    Console.WriteLine(""finally"");
                }
            }
        ");

        var result = await RunPipelineWithNodesAsync(source, root => {
            var catchClause = root.DescendantNodes().OfType<CatchClauseSyntax>().First();
            var finallyClause = root.DescendantNodes().OfType<FinallyClauseSyntax>().First();
            return new SyntaxNode[] { catchClause, finallyClause };
        });

        Assert.DoesNotContain("try {", result);
        Assert.DoesNotContain("catch", result);
        Assert.DoesNotContain("finally", result);
        Assert.Contains("Console.WriteLine(\"try\");", result);
    }

    [Fact]
    public async Task Pipeline_ShouldMergeConditionalToTrueWhenFalseBranchIsRemoved()
    {
        // ConditionalExpression_MergeLeft (True branch)
        string source = BogusTestDataGenerator.GenerateFullClass(@"
            string M(bool cond) {
                return cond ? ""true"" : ""false"";
            }
        ");

        var result = await RunPipelineWithNodesAsync(source, root => {
            var condExpr = root.DescendantNodes().OfType<ConditionalExpressionSyntax>().First();
            // 标记 WhenFalse �?Remove
            return new[] { condExpr.WhenFalse };
        });

        Assert.DoesNotContain("?", result);
        Assert.DoesNotContain(":", result);
        Assert.Contains("return \"true\";", result);
        Assert.DoesNotContain("\"false\"", result);
    }

    [Fact]
    public async Task Pipeline_ShouldMergeConditionalToFalseWhenTrueBranchIsRemoved()
    {
        // ConditionalExpression_MergeRight (False branch)
        string source = BogusTestDataGenerator.GenerateFullClass(@"
            string M(bool cond) {
                return cond ? ""true"" : ""false"";
            }
        ");

        var result = await RunPipelineWithNodesAsync(source, root => {
            var condExpr = root.DescendantNodes().OfType<ConditionalExpressionSyntax>().First();
            // 标记 WhenTrue �?Remove
            return new[] { condExpr.WhenTrue };
        });

        Assert.DoesNotContain("?", result);
        Assert.DoesNotContain(":", result);
        Assert.Contains("return \"false\";", result);
        Assert.DoesNotContain("\"true\"", result);
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceMissingInvocationArgumentWithDefault()
    {
        // HandleInvocation_MissingParameter_ReplacesWithDefaultValue
        string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.ExpressionPipelineTests_Source_1;

        var result = await RunPipelineWithNodesAsync(source, root => {
            var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .First(i => i.ToString().Contains("SpawnMinionOnCursor"));
            // The 3rd argument (index 2) is '2' (passed as int damage)
            return new[] { invocation.ArgumentList.Arguments[2].Expression };
        });

        Assert.Contains("SpawnMinionOnCursor(s, 1, 0, 3, 4.0f, v, v)", result);
    }

    [Fact]
    public async Task Pipeline_ShouldReplaceMultipleMissingArgumentsWithPlaceholders()
    {
        // HandleInvocation_MultipleMissingParameters_ReplacesAllWithPlaceholders
        string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.ExpressionPipelineTests_Source_1;

        var result = await RunPipelineWithNodesAsync(source, root => {
            var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
            // Remove '2' (damage) and '3' (originalDamage)
            // In original code: Arguments[2] is '2', Arguments[3] is '3'
            return new[] {
                invocation.ArgumentList.Arguments[2].Expression,
                invocation.ArgumentList.Arguments[3].Expression
            };
        });

        Assert.Contains("SpawnMinionOnCursor(s, 1, 0, 0, 4.0f)", result);
    }

    [Fact]
    public async Task Pipeline_ShouldCascadeDeletion_WhenAssignmentRightRemoved()
    {
        // VariableDeletion_Cascading_WhenAssignmentRightRemoved
        string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.ExpressionPipelineTests_Source_1;

        var result = await RunPipelineWithNodesAsync(source, root => {
            return root.DescendantNodes().Where(node =>
                node.ToString().Contains("PlayerInput.SomeValue") ||
                node.ToString().Contains("PlayerInput.OtherValue"));
        });

        // num2 should be completely removed
        Assert.DoesNotContain("int num2", result);
        Assert.DoesNotContain("num2 =", result);
        Assert.DoesNotContain("num2 !=", result);
    }

    [Fact]
    public async Task Pipeline_ShouldCascadeDeletion_WithBinaryExpression()
    {
        // VariableDeletion_Cascading_WithBinaryExpression
        string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.ExpressionPipelineTests_Source_1;

        var result = await RunPipelineWithNodesAsync(source, root => {
            return root.DescendantNodes().Where(node =>
                (node is IdentifierNameSyntax || node is MemberAccessExpressionSyntax) &&
                node.ToString().Contains("PlayerInput"));
        });

        Assert.DoesNotContain("num2", result);
    }

    [Fact]
    public async Task Pipeline_ShouldCascadeDeletion_WhenOneSideStays()
    {
        // VariableDeletion_Cascading_WhenOneSideStays
        string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.ExpressionPipelineTests_Source_1;

        var result = await RunPipelineWithNodesAsync(source, root => {
            return root.DescendantNodes().Where(node =>
                (node is IdentifierNameSyntax || node is MemberAccessExpressionSyntax) &&
                node.ToString().Contains("PlayerInput"));
        });

        // PlayerInput.A removed, num2 = 0 - 10 (or similar), so num2 should remain
        Assert.Contains("int num2", result);
        Assert.Contains("num2", result);
    }

    [Fact]
    public async Task Pipeline_ShouldHandleComplexCascading_HotbarControls()
    {
        // VariableDeletion_ComplexCase_HandleHotbarControls
        // Mocking dependencies to make it compile
        string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.ExpressionPipelineTests_Source_1;

        var result = await RunPipelineWithNodesAsync(source, root => {
            return root.DescendantNodes().Where(node =>
               (node is IdentifierNameSyntax || node is MemberAccessExpressionSyntax) &&
               node.ToString().Contains("PlayerInput"));
        });

        // 1. num2 should be removed (init depends on PlayerInput)
        Assert.DoesNotContain("int num2", result);
        Assert.DoesNotContain("num2 !=", result);
        Assert.DoesNotContain("num += num2", result);

        // 2. num should remain (has other usages/modifications not fully dependent on deleted parts?
        // Actually in the original test:
        // num init is 0.
        // num += num2 is removed because num2 is removed.
        // num += PlayerInput... is removed because PlayerInput is removed.
        // So num stays 0.
        // The check 'if (num != 0)' remains.
        // The original test asserted: Assert.Contains("int num = 0;", resultText);
        Assert.Contains("int num = 0;", result);
        Assert.Contains("if (num != 0)", result);
    }

    [Fact]
    public async Task Pipeline_ShouldHandleCascading_CopyIntoDuringChat()
    {
        // VariableDeletion_Cascading_CopyIntoDuringChat
        // Mocking dependencies
        string source = TerrariaTools.UnitTests.Scenarios.AutoMigratedScenarios.ExpressionPipelineTests_Source_1;

        var result = await RunPipelineWithNodesAsync(source, root => {
            return root.DescendantNodes().Where(node =>
                (node is IdentifierNameSyntax || node is MemberAccessExpressionSyntax) &&
                node.ToString().Contains("PlayerInput"));
        });

        // 1. flag decl removed
        Assert.DoesNotContain("bool flag", result);
        // 2. if (flag) removed/simplified
        Assert.DoesNotContain("if (flag)", result);
        // 3. HotbarScrollCD condition simplified (flag removed)
        // Note: checking for 'flag' string might be risky if 'flag' appears elsewhere, but here it's a variable name.
        // It shouldn't appear in the result if the variable is gone.
        // However, 'flag' string might be part of other things? No, variable name.
        // But wait, Assert.DoesNotContain("flag", resultText) in original test.
        Assert.DoesNotContain("flag", result);

        // 4. The empty if (!MouseRight...) block should be removed?
        // In original test: "if (!MouseRight && !Main.playerInventory) { PlayerInput... = false; }"
        // PlayerInput... is removed. So block becomes empty.
        // If block is empty, does pipeline remove the if?
        // Original test: Assert.DoesNotContain("if (!MouseRight && !Main.playerInventory)", resultText);
        Assert.DoesNotContain("if (!MouseRight && !Main.playerInventory)", result);
    }
}

