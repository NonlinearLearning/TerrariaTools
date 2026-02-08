using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
using Xunit;
using Xunit.Abstractions;
using System.Linq;

namespace TerrariaTools.UnitTests
{
    public class ExpressionTests
    {
        private readonly ITestOutputHelper _output;

        public ExpressionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private SyntaxNode ParseExpression(string expression)
        {
            return SyntaxFactory.ParseExpression(expression);
        }

        private SyntaxNode ParseStatement(string statement)
        {
            return SyntaxFactory.ParseStatement(statement);
        }

        private SemanticModel GetSemanticModel(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(syntaxTree);
            return compilation.GetSemanticModel(syntaxTree);
        }

        [Fact]
        public void HandleArgument_RemoveObjectArgument_ReplacesWithNull()
        {
            string source = @"
class Test {
    void Foo(string s) { }
    void Bar() { Foo(""hello""); }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var argument = root.DescendantNodes().OfType<ArgumentSyntax>().First();

            var result = ExpressionProcessor.RemoveExpressionPart(root, argument.Expression, model);

            string expected = "Foo(default(string))";
            Assert.Contains(expected, result!.ToFullString());
        }

        [Fact]
        public void HandleArgument_RemoveValueTypeArgument_ReplacesWithCastZero()
        {
            string source = @"
class Test {
    void Foo(int x) { }
    void Bar() { Foo(123); }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var argument = root.DescendantNodes().OfType<ArgumentSyntax>().First();

            var result = ExpressionProcessor.RemoveExpressionPart(root, argument.Expression, model);

            // 预期结果应该是 Foo(default(int))
            string expected = "Foo(default(int))";
            Assert.Contains(expected, result!.ToFullString());
        }

        [Fact]
        public void HandleInvocation_MissingParameter_ReplacesWithDefaultValue()
        {
            string source = @"
using System;
using System.Numerics;
public interface IEntitySource {}
public class Player {
    public void SpawnMinionOnCursor(IEntitySource source, int type, int damage, int knockback, float x, Vector2 v1, Vector2 v2) {}
    public void Test(IEntitySource s, Vector2 v) {
        SpawnMinionOnCursor(s, 1, 2, 3, 4.0f, v, v);
    }
}
public class Vector2 { public float X, Y; }
";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 找到调用 SpawnMinionOnCursor 的节点
            var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First(i => i.ToString().Contains("SpawnMinionOnCursor"));

            // 模拟其中一个参数被标记为删除（例如第三个参数 damage）
            var targetArg = invocation.ArgumentList.Arguments[2];

            var result = ExpressionProcessor.RemoveExpressionPart(root, targetArg.Expression, model);

            // 验证输出中参数数量仍然是 7 个，且被删除的参数被替换为了 (int)0
            var newInvocation = result!.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .First(i => i.ToString().Contains("SpawnMinionOnCursor"));

            Assert.Equal(7, newInvocation.ArgumentList.Arguments.Count);
            Assert.Equal("default(int)", newInvocation.ArgumentList.Arguments[2].Expression.ToString());
        }

        [Fact]
        public void HandleInvocation_MultipleMissingParameters_ReplacesAllWithPlaceholders()
        {
            string source = @"
public class Player {
    public void SpawnMinionOnCursor(object source, int projType, int damage, int originalDamage, float knockback) {}
    public void Test(object s) {
        SpawnMinionOnCursor(s, 1, 2, 3, 4.0f);
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();

            var damageExpr = invocation.ArgumentList.Arguments[2].Expression;

            // 第一次删除 damage
            var result = ExpressionProcessor.RemoveExpressionPart(root, damageExpr, model);

            // 为第二次删除获取新的语义模型
            var model2 = GetSemanticModel(result!.ToFullString());
            var root2 = model2.SyntaxTree.GetRoot();
            var invocation2 = root2.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
            var originalDamageExpr = invocation2.ArgumentList.Arguments[3].Expression;

            var result2 = ExpressionProcessor.RemoveExpressionPart(root2, originalDamageExpr, model2);

            var finalInvocation = result2!.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
            Assert.Equal(5, finalInvocation.ArgumentList.Arguments.Count);
            Assert.Equal("default(int)", finalInvocation.ArgumentList.Arguments[2].Expression.ToString());
            Assert.Equal("default(int)", finalInvocation.ArgumentList.Arguments[3].Expression.ToString());
        }

        [Fact]
        public void ReturnStatement_ValueType_ReplacesWithCastZero()
        {
            string source = @"
public class Test {
    public int GetInt() {
        int k = 10;
        return k;
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var returnStmt = root.DescendantNodes().OfType<ReturnStatementSyntax>().First();
            var kExpr = returnStmt.Expression!;

            var result = ExpressionProcessor.RemoveExpressionPart(root, kExpr, model);
            var newReturn = result!.DescendantNodes().OfType<ReturnStatementSyntax>().First();

            Assert.Equal("return default(int);", newReturn.ToString().Trim());
        }

        [Fact]
        public void ReturnStatement_ReferenceType_ReplacesWithNull()
        {
            string source = @"
public class Test {
    public string GetString() {
        string s = ""hello"";
        return s;
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var returnStmt = root.DescendantNodes().OfType<ReturnStatementSyntax>().First();
            var sExpr = returnStmt.Expression!;

            var result = ExpressionProcessor.RemoveExpressionPart(root, sExpr, model);
            var newReturn = result!.DescendantNodes().OfType<ReturnStatementSyntax>().First();

            Assert.Equal("return default(string);", newReturn.ToString().Trim());
        }

        [Fact]
        public void AttributeArgument_ReplacesWithPlaceholder()
        {
            string source = @"
using System;
[MyAttr(10, ""keep"")]
public class MyAttrAttribute : Attribute {
    public MyAttrAttribute(int val, string s) {}
}
public class Test {}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var attrArg = root.DescendantNodes().OfType<AttributeArgumentSyntax>().First();
            var tenExpr = attrArg.Expression;

            var result = ExpressionProcessor.RemoveExpressionPart(root, tenExpr, model);
            var newAttrArg = result!.DescendantNodes().OfType<AttributeArgumentSyntax>().First();

            Assert.Equal("default(int)", newAttrArg.Expression.ToString());
            Assert.Contains("\"keep\"", result.ToFullString());
        }

        [Fact]
        public void AnonymousObject_ReplacesWithPlaceholder()
        {
            string source = @"
public class Test {
    public void M() {
        var obj = new { A = 10 };
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var declarator = root.DescendantNodes().OfType<AnonymousObjectMemberDeclaratorSyntax>().First();
            var tenExpr = declarator.Expression;

            var result = ExpressionProcessor.RemoveExpressionPart(root, tenExpr, model);
            var newDeclarator = result!.DescendantNodes().OfType<AnonymousObjectMemberDeclaratorSyntax>().First();

            Assert.Equal("A = default(int)", newDeclarator.ToString().Trim());
        }

        [Fact]
        public void Tuple_ReplacesWithPlaceholder()
        {
            string source = @"
public class Test {
    public void M() {
        (int a, string b) t = (1, ""s"");
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var tuple = root.DescendantNodes().OfType<TupleExpressionSyntax>().First();
            var sExpr = tuple.Arguments[1].Expression;

            var result = ExpressionProcessor.RemoveExpressionPart(root, sExpr, model);
            var newTuple = result!.DescendantNodes().OfType<TupleExpressionSyntax>().First();

            Assert.Equal("(1, default(string))", newTuple.NormalizeWhitespace().ToString().Trim());
        }

        [Fact]
        public void ObjectInitializer_ReplacesWithPlaceholder()
        {
            string source = @"
public class Person { public string Name { get; set; } public int Age { get; set; } }
public class Test {
    public void M() {
        var p = new Person { Name = ""John"", Age = 30 };
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var assignment = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().First(a => a.Left.ToString() == "Name");
            var nameExpr = assignment.Right;

            var result = ExpressionProcessor.RemoveExpressionPart(root, nameExpr, model);
            var newAssignment = result!.DescendantNodes().OfType<AssignmentExpressionSyntax>().First(a => a.Left.ToString() == "Name");

            Assert.Equal("default(string)", newAssignment.Right.ToString());
        }

        [Fact]
        public void VariableDeletion_Isolation_BetweenMethods()
        {
            string source = @"
public class Test {
    public void Method1() {
        int v = 1;
        int x = v + 1;
    }
    public void Method2() {
        int v = 5;
        int y = v + 1;
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 找到 Method1 中的 v = 1 声明
            var method1 = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.ValueText == "Method1");
            var v1Decl = method1.DescendantNodes().OfType<VariableDeclaratorSyntax>().First();

            // 删除 Method1 中的 v 声明的初始化值
            // 这应该导致 v 被标记，然后通过 Visit 类传播到 Method1 中对 v 的所有引用
            // 最后 Method1 中的 v 和 x 的声明都应该被删除（因为 x 依赖于 v）
            var result = ExpressionProcessor.RemoveParts(root, node => node == v1Decl.Initializer!.Value, model);
            var resultText = result.ToFullString();

            // 验证 Method1 中的 v 和 x 声明被删除
            Assert.DoesNotContain("int v = 1;", resultText);
            Assert.DoesNotContain("int x = v + 1;", resultText);

            // 验证 Method2 中的 v 依然存在，没有被误删
            Assert.Contains("int v = 5;", resultText);
            Assert.Contains("int y = v + 1;", resultText);
        }

        [Fact]
        public void ReturnStatement_Placeholder_Boolean()
        {
            string source = @"
public class PlayerInput {
    public static bool AllowExecutionOfGamepadInstructions = true;
}
public class Test {
    public static bool CanExecuteCommand() {
        return PlayerInput.AllowExecutionOfGamepadInstructions;
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 找到 PlayerInput.AllowExecutionOfGamepadInstructions 引用
            var memberAccess = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .First(m => m.ToString().Contains("AllowExecutionOfGamepadInstructions"));

            // 删除该引用
            var result = ExpressionProcessor.RemoveExpressionPart(root, memberAccess, model);
            var resultText = result!.NormalizeWhitespace().ToFullString();

            // 预期结果应该是 return default(bool);
            string normalizedResult = resultText.Replace(" ", "").Replace("\r", "").Replace("\n", "");
            Assert.Contains("returndefault(bool);", normalizedResult);
        }

        [Fact]
        public void VariableDeletion_Cascading_WhenAssignmentRightRemoved()
        {
            string source = @"
public class Test {
    public void M() {
        int num2 = PlayerInput.SomeValue;
        if (num2 != 0) { }
        num2 = PlayerInput.OtherValue;
        if (num2 != 0) { num2 = 0; }
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 模拟删除 PlayerInput.SomeValue 和 PlayerInput.OtherValue
            // 这应该导致 num2 的初始化和后续赋值都被删除，从而 num2 变量本身也被移除
            var result = ExpressionProcessor.RemoveParts(root, node =>
                node.ToString().Contains("PlayerInput.SomeValue") ||
                node.ToString().Contains("PlayerInput.OtherValue"), model);
            var resultText = result?.NormalizeWhitespace().ToFullString() ?? "";

             // 预期 num2 被完全删除
             Assert.DoesNotContain("int num2", resultText);
             Assert.DoesNotContain("num2 =", resultText);
             Assert.DoesNotContain("num2 !=", resultText);
         }

        [Fact]
        public void VariableDeletion_Cascading_WithBinaryExpression()
        {
            string source = @"
public class Test {
    public void Method() {
        int num2 = PlayerInput.A - PlayerInput.B;
        if (num2 != 0) {
            System.Console.WriteLine(num2);
        }
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 模拟删除所有包含 PlayerInput 的节点
            var result = ExpressionProcessor.RemoveParts(root, node =>
                (node is IdentifierNameSyntax || node is MemberAccessExpressionSyntax) &&
                node.ToString().Contains("PlayerInput"), model);

            var resultText = result?.NormalizeWhitespace().ToFullString() ?? "";

            // num2 应该被级联删除
            Assert.DoesNotContain("num2", resultText);
        }

        [Fact]
        public void VariableDeletion_Cascading_WhenOneSideStays()
        {
            string source = @"
public class Test {
    public void Method() {
        int num = 0;
        int num2 = PlayerInput.A - 10;
        if (num2 != 0) {
            num += num2;
        }
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, node =>
                (node is IdentifierNameSyntax || node is MemberAccessExpressionSyntax) &&
                node.ToString().Contains("PlayerInput"), model);

            var resultText = result?.NormalizeWhitespace().ToFullString() ?? "";

            // PlayerInput.A 被删，num2 = PlayerInput.A - 10 变为 num2 = (int)0 - 10
            // 所以 num2 应该保留。
            Assert.True(resultText.Contains("int num2"), $"Actual text: {resultText}");
            Assert.Contains("num2", resultText);
        }

        [Fact]
        public void VariableDeletion_ComplexCase_HandleHotbarControls()
        {
            string source = @"
public class Test {
    private void HandleHotbarControls() {
        int num = 0;
        int num2 = PlayerInput.Triggers.Current.HotbarPlus.ToInt() - PlayerInput.Triggers.Current.HotbarMinus.ToInt();
        if (num2 != 0) {
        }
        if (num2 != 0) {
            num += num2;
        }
        if (!Main.inFancyUI && !Main.ingameOptionsWindow) {
            num += PlayerInput.ScrollWheelDelta / -120;
        }
        if (num != 0) {
            selectedItemState.Select(ClampHotbarOffset(selectedItemState.Hotbar + num));
        }
    }
}";
            var model = GetSemanticModel(source);
            
            var root = model.SyntaxTree.GetRoot();

            // 模拟删除所有包含 PlayerInput 的节点
             var result = ExpressionProcessor.RemoveParts(root, node =>
                (node is IdentifierNameSyntax || node is MemberAccessExpressionSyntax) &&
                node.ToString().Contains("PlayerInput"), model);

            var resultText = result?.NormalizeWhitespace().ToFullString() ?? "";

            // 1. num2 应该被级联删除，因为它的初始化完全依赖于被删除的 PlayerInput
            Assert.DoesNotContain("int num2", resultText);
            Assert.DoesNotContain("num2 !=", resultText);
            Assert.DoesNotContain("num += num2", resultText);

            // 2. num 应该保留，因为虽然它有一处修改依赖于 PlayerInput，但它的声明 and 后续使用 (num != 0) 还在
            Assert.Contains("int num = 0;", resultText);
            Assert.Contains("if (num != 0)", resultText);
        }

        [Fact]
        public void VariableDeletion_Cascading_CopyIntoDuringChat()
        {
            string source = @"
public class Test {
    public void CopyIntoDuringChat(Player p) {
        if (MouseLeft) {
            if (!Main.blockMouse && !p.mouseInterface) {
                p.controlUseItem = true;
            }
        } else {
            Main.blockMouse = false;
        }
        if (!MouseRight && !Main.playerInventory) {
            PlayerInput.LockGamepadTileUseButton = false;
        }
        if (MouseRight && !p.mouseInterface && !Main.blockMouse && !ShouldLockTileUsage() && !PlayerInput.InBuildingMode) {
            p.controlUseTile = true;
        }
        bool flag = PlayerInput.Triggers.Current.HotbarPlus || PlayerInput.Triggers.Current.HotbarMinus;
        if (flag) {
            HotbarHoldTime++;
        } else {
            HotbarHoldTime = 0;
        }
        if (HotbarScrollCD > 0 && (!(HotbarScrollCD == 1 && flag) || PlayerInput.CurrentProfile.HotbarRadialHoldTimeRequired <= 0)) {
            HotbarScrollCD--;
        }
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, node =>
                (node is IdentifierNameSyntax || node is MemberAccessExpressionSyntax) &&
                node.ToString().Contains("PlayerInput"), model);

            var resultText = result?.NormalizeWhitespace().ToFullString() ?? "";

            // 1. flag 声明应该被删除
            Assert.DoesNotContain("bool flag", resultText);
            // 2. if (flag) 应该被删除或简化
            Assert.DoesNotContain("if (flag)", resultText);
            // 3. HotbarScrollCD 的 if 条件应该被简化
            Assert.DoesNotContain("flag", resultText);
            // 4. 空的 if (!MouseRight && !Main.playerInventory) {} 应该被删除
            Assert.DoesNotContain("if (!MouseRight && !Main.playerInventory)", resultText);
        }
    }
}
