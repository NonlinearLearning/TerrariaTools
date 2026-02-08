using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
using Xunit;
using System.Linq;
using System.Collections.Generic;

namespace TerrariaTools.UnitTests
{
    /// <summary>
    /// 专门测试 UpwardMarkCollector 的向上传播逻辑。
    /// 虽然 UpwardMarkCollector 是私有类，但我们通过 ExpressionProcessor.RemoveParts 验证其行为。
    /// </summary>
    public class UpwardMarkCollectorTests
    {
        private SemanticModel GetSemanticModel(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(syntaxTree);
            return compilation.GetSemanticModel(syntaxTree);
        }

        [Fact]
        public void Test01_VariableDeclarator_Propagation()
        {
            // 测试：初始化器被移除 -> 变量声明器被移除
            string source = "class C { void M() { int x = 1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 标记数字字面量 1 为移除
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax l && l.Token.ValueText == "1");

            Assert.DoesNotContain("int x", result.ToFullString());
        }

        [Fact]
        public void Test02_VariableDeclaration_Propagation()
        {
            // 测试：所有声明器被移除 -> 整个变量声明被移除
            string source = "class C { void M() { int x = 1, y = 2; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 标记 1 和 2 为移除，导致 x 和 y 都被移除，进而导致 int x, y 被移除
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);

            Assert.DoesNotContain("int x", result.ToFullString());
            Assert.DoesNotContain("int y", result.ToFullString());
        }

        [Fact]
        public void Test03_LocalDeclarationStatement_Propagation()
        {
            // 测试：变量声明被移除 -> 整个局部声明语句被移除
            string source = "class C { void M() { int x = 1; Console.WriteLine(2); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax l && l.Token.ValueText == "1");

            Assert.DoesNotContain("int x = 1;", result.ToFullString());
            Assert.Contains("Console.WriteLine(2)", result.ToFullString());
        }

        [Fact]
        public void Test04_ExpressionStatement_Propagation()
        {
            // 测试：表达式被移除 -> 整个表达式语句被移除
            string source = "class C { void M() { M2(); } void M2() {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is InvocationExpressionSyntax);

            Assert.DoesNotContain("M2();", result.ToFullString());
        }

        [Fact]
        public void Test05_ReturnStatement_Propagation()
        {
            // 测试：返回表达式被移除 -> 整个返回语句被移除（如果返回类型允许或通过占位符处理）
            // 注意：ExpressionSimplifier 会尝试为 return 提供占位符，但在向上传播阶段，如果表达式被标记，语句也会被标记。
            string source = "class C { void M() { return; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 标记空返回（虽然没有表达式，但可以手动标记返回语句本身或其部分）
            // 这里测试 ReturnStatement 本身被标记的情况
            var result = ExpressionProcessor.RemoveParts(root, n => n is ReturnStatementSyntax);

            Assert.DoesNotContain("return;", result.ToFullString());
        }

        [Fact]
        public void Test06_IfStatement_ConditionPropagation()
        {
            // 测试：If 条件被移除 -> 整个 If 语句被移除
            string source = "class C { void M(bool b) { if (b) { Do(); } } void Do() {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "b" && n.Parent is IfStatementSyntax);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("if (b)", resultText);
            // 确保 Do() 调用消失了（不匹配定义）
            Assert.Single(result.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.ValueText == "Do"));
            Assert.Empty(result.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i => i.ToString().Contains("Do")));
        }

        [Fact]
        public void Test07_SwitchStatement_ExpressionPropagation()
        {
            // 测试：Switch 表达式被移除 -> 整个 Switch 语句被移除
            string source = "class C { void M(int i) { switch(i) { case 1: break; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "i");

            Assert.DoesNotContain("switch", result.ToFullString());
        }

        [Fact]
        public void Test08_Block_AllStatementsPropagation()
        {
            // 测试：代码块内所有语句被标记 -> 整个代码块被标记
            string source = "class C { void M() { { int x = 1; int y = 2; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);

            // 内部代码块应该消失。外部方法块应该保留。
            var method = result.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
            if (method.Body != null)
            {
                // 验证方法体内没有嵌套的代码块（即内部那个 block 被移除了）。
                Assert.Empty(method.Body.Statements.OfType<BlockSyntax>());
            }
        }

        [Fact]
        public void Test09_BinaryExpression_BothSidesPropagation()
        {
            // 测试：二元表达式左右都被标记 -> 整个二元表达式被标记
            string source = "class C { void M() { int x = 1 + 2; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);

            Assert.DoesNotContain("1 + 2", result.ToFullString());
        }

        [Fact]
        public void Test10_AssignmentExpression_RightSidePropagation()
        {
            // 测试：赋值表达式右侧被标记（且作为语句） -> 整个赋值被标记
            string source = "class C { int x; void M() { x = 1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);

            Assert.DoesNotContain("x = 1", result.ToFullString());
        }

        [Fact]
        public void Test11_ConditionalExpression_AllPartsPropagation()
        {
            // 测试：三元表达式三部分都被标记 -> 整个三元表达式被标记
            string source = "class C { void M(bool b) { int x = b ? 1 : 2; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax || n is LiteralExpressionSyntax);

            Assert.DoesNotContain("?", result.ToFullString());
        }

        [Fact]
        public void Test12_MemberAccess_ExpressionPropagation()
        {
            // 测试：成员访问的基础表达式被标记 -> 整个成员访问被标记
            string source = "class C { void M(C c) { var x = c.M2(); } void M2() {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "c");

            Assert.DoesNotContain("c.M2", result.ToFullString());
        }

        [Fact]
        public void Test13_ConditionalAccess_ExpressionPropagation()
        {
            // 测试：条件访问的基础表达式被标记 -> 整个条件访问被标记
            string source = "class C { void M(C c) { var x = c?.M2(); } void M2() {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "c");

            Assert.DoesNotContain("c?.M2", result.ToFullString());
        }

        [Fact]
        public void Test14_Invocation_ExpressionPropagation()
        {
            // 测试：被调用的方法表达式被标记 -> 整个调用被标记
            string source = "class C { void M() { M2(); } void M2() {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 明确标记调用处的 M2，而不是定义处
            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "M2" && n.Parent is InvocationExpressionSyntax);

            // 验证 M2() 调用消失
            Assert.Empty(result.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i => i.ToString().Contains("M2")));
            // 验证 M2 定义还在
            Assert.Contains("void M2()", result.ToFullString());
        }

        [Fact]
        public void Test15_Invocation_AllArgumentsPropagation()
        {
            // 测试：调用的所有参数被标记 -> 整个调用被标记
            string source = "class C { void M() { M2(1, 2); } void M2(int a, int b) {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);

            Assert.DoesNotContain("M2(1, 2)", result.ToFullString());
        }

        [Fact]
        public void Test16_CastExpression_ExpressionPropagation()
        {
            // 测试：转换表达式的内部表达式被标记 -> 整个转换被标记
            string source = "class C { void M() { object o = (object)1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);

            Assert.DoesNotContain("(object)1", result.ToFullString());
        }

        [Fact]
        public void Test17_ParenthesizedExpression_ExpressionPropagation()
        {
            // 测试：括号表达式内部被标记 -> 整个括号被标记
            string source = "class C { void M() { int x = (1); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);

            Assert.DoesNotContain("(1)", result.ToFullString());
        }

        [Fact]
        public void Test18_PrefixUnaryExpression_OperandPropagation()
        {
            // 测试：前缀一元表达式操作数被标记 -> 整个表达式被标记
            string source = "class C { void M(int i) { int x = -i; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "i");

            Assert.DoesNotContain("-i", result.ToFullString());
        }

        [Fact]
        public void Test19_ObjectCreation_TypePropagation()
        {
            // 测试：对象创建的类型被标记 -> 整个创建被标记
            string source = "class C { void M() { var x = new C(); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 标记 new C() 中的 C
            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "C" && n.Parent is ObjectCreationExpressionSyntax);

            Assert.DoesNotContain("new C()", result.ToFullString());
        }

        [Fact]
        public void Test20_InterpolatedString_AllContentsPropagation()
        {
            // 测试：内插字符串所有内容被标记 -> 整个字符串被标记
            string source = "class C { void M(int i) { string s = $\"{i}\"; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is InterpolationSyntax);

            Assert.DoesNotContain("$\"{i}\"", result.ToFullString());
        }

        [Fact]
        public void Test21_TryCatch_TryBodyPropagation()
        {
            // 测试：Try 块内所有语句被标记 -> 整个 Try 语句被标记
            string source = "class C { void M() { try { int x = 1; } catch { } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("try", result.ToFullString());
        }

        [Fact]
        public void Test22_UsingStatement_BodyPropagation()
        {
            // 测试：Using 块内所有语句被标记 -> 整个 Using 语句被标记
            string source = "class C { void M() { using (var s = null) { int x = 1; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("using", result.ToFullString());
        }

        [Fact]
        public void Test23_LockStatement_BodyPropagation()
        {
            // 测试：Lock 块内所有语句被标记 -> 整个 Lock 语句被标记
            string source = "class C { void M(object l) { lock(l) { int x = 1; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("lock", result.ToFullString());
        }

        [Fact]
        public void Test24_ArrayInitializer_AllElementsPropagation()
        {
            // 测试：数组初始化器所有元素被标记 -> 整个数组创建被标记
            string source = "class C { void M() { int[] a = new int[] { 1, 2 }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("new int[]", result.ToFullString());
        }

        [Fact]
        public void Test25_CollectionExpression_AllElementsPropagation()
        {
            // 测试：集合表达式所有元素被标记 -> 整个集合表达式被标记
            string source = "class C { void M() { System.Collections.Generic.List<int> l = [1, 2]; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("[1, 2]", result.ToFullString());
        }

        [Fact]
        public void Test26_ObjectInitializer_AllAssignmentsPropagation()
        {
            // 测试：对象初始化器所有赋值被标记 -> 整个初始化器被标记
            string source = "class Point { public int X; public int Y; } class C { void M() { var p = new Point { X = 1, Y = 2 }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("{ X = 1, Y = 2 }", result.ToFullString());
        }

        [Fact]
        public void Test27_FieldDeclaration_AllVariablesPropagation()
        {
            // 测试：字段声明中所有变量被标记 -> 整个字段声明被标记
            string source = "class C { int x = 1, y = 2; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("int x", result.ToFullString());
            Assert.DoesNotContain("int y", result.ToFullString());
        }

        [Fact]
        public void Test28_PropertyDeclaration_AllAccessorsPropagation()
        {
            // 测试：属性的所有访问器被标记 -> 整个属性被标记
            // 注意：通常访问器不会被自动标记，除非明确指定。这里测试标记访问器本身。
            string source = "class C { int Prop { get { return 1; } set { } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is AccessorDeclarationSyntax);
            Assert.DoesNotContain("int Prop", result.ToFullString());
        }

        [Fact]
        public void Test29_Attribute_AllArgumentsPropagation()
        {
            // 测试：特性的所有参数被标记 -> 整个特性被标记
            string source = "[MyAttr(1, 2)] class C { } class MyAttrAttribute : System.Attribute { public MyAttrAttribute(int a, int b) {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("[MyAttr", result.ToFullString());
        }

        [Fact]
        public void Test30_ForEachStatement_ExpressionPropagation()
        {
            // 测试：foreach 集合表达式被标记 -> 整个 foreach 被标记
            string source = "class C { void M(int[] arr) { foreach(var x in arr) { } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "arr" && n.Parent is ForEachStatementSyntax);
            Assert.DoesNotContain("foreach", result.ToFullString());
        }

        [Fact]
        public void Test31_ForStatement_ConditionPropagation()
        {
            // 测试：for 循环条件被标记 -> 整个 for 被标记
            string source = "class C { void M() { for(int i = 0; i < 10; i++) { } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is BinaryExpressionSyntax b && b.OperatorToken.IsKind(SyntaxKind.LessThanToken) && n.Parent is ForStatementSyntax);
            Assert.DoesNotContain("for", result.ToFullString());
        }

        [Fact]
        public void Test32_WhileStatement_ConditionPropagation()
        {
            // 测试：while 循环条件被标记 -> 整个 while 被标记
            string source = "class C { void M(bool b) { while(b) { } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "b" && n.Parent is WhileStatementSyntax);
            Assert.DoesNotContain("while", result.ToFullString());
        }

        [Fact]
        public void Test33_DoStatement_ConditionPropagation()
        {
            // 测试：do-while 循环条件被标记 -> 整个 do-while 被标记
            string source = "class C { void M(bool b) { do { } while(b); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "b" && n.Parent is DoStatementSyntax);
            Assert.DoesNotContain("do", result.ToFullString());
        }

        [Fact]
        public void Test34_FixedStatement_AllVariablesPropagation()
        {
            // 测试：fixed 语句中所有变量被标记 -> 整个 fixed 语句被标记
            string source = "class C { unsafe void M(int[] arr) { fixed(int* p = arr) { } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "arr");
            Assert.DoesNotContain("fixed", result.ToFullString());
        }

        [Fact]
        public void Test35_LocalFunction_BodyPropagation()
        {
            // 测试：局部函数主体内所有语句被标记 -> 整个局部函数被标记
            string source = "class C { void M() { void LF() { int x = 1; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("void LF()", result.ToFullString());
        }

        [Fact]
        public void Test36_YieldReturn_ExpressionPropagation()
        {
            // 测试：yield return 表达式被标记 -> 整个 yield return 语句被标记
            string source = "using System.Collections.Generic; class C { IEnumerable<int> M() { yield return 1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("yield return", result.ToFullString());
        }

        [Fact]
        public void Test37_YieldBreak_Propagation()
        {
            // 测试：yield break 语句被直接标记
            string source = "using System.Collections.Generic; class C { IEnumerable<int> M() { yield break; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is YieldStatementSyntax);
            Assert.DoesNotContain("yield break", result.ToFullString());
        }

        [Fact]
        public void Test38_IndexerDeclaration_AllAccessorsPropagation()
        {
            // 测试：索引器所有访问器被标记 -> 整个索引器被标记
            string source = "class C { int this[int i] { get { return 1; } set { } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is AccessorDeclarationSyntax);
            Assert.DoesNotContain("int this[int i]", result.ToFullString());
        }

        [Fact]
        public void Test39_EventDeclaration_AllAccessorsPropagation()
        {
            // 测试：事件声明所有访问器被标记 -> 整个事件声明被标记
            string source = "using System; class C { event EventHandler E { add { } remove { } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is AccessorDeclarationSyntax);
            Assert.DoesNotContain("event EventHandler E", result.ToFullString());
        }

        [Fact]
        public void Test40_EventFieldDeclaration_AllVariablesPropagation()
        {
            // 测试：事件字段声明所有变量被标记 -> 整个事件字段声明被标记
            string source = "using System; class C { event EventHandler E1, E2; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax);
            Assert.DoesNotContain("event EventHandler", result.ToFullString());
        }

        [Fact]
        public void Test41_EnumMemberDeclaration_InitializerPropagation()
        {
            // 测试：枚举成员初始值被标记 -> 整个枚举成员被标记
            string source = "enum E { A = 1 }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is EqualsValueClauseSyntax);
            Assert.DoesNotContain("A =", result.ToFullString());
        }

        [Fact]
        public void Test42_EnumDeclaration_AllMembersPropagation()
        {
            // 测试：枚举所有成员被标记 -> 整个枚举被标记
            string source = "enum E { A, B }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is EnumMemberDeclarationSyntax);
            Assert.DoesNotContain("enum E", result.ToFullString());
        }

        [Fact]
        public void Test43_NamespaceDeclaration_AllMembersPropagation()
        {
            // 测试：命名空间所有成员被标记 -> 整个命名空间被标记
            string source = "namespace N { class C {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ClassDeclarationSyntax);
            Assert.DoesNotContain("namespace N", result.ToFullString());
        }

        [Fact]
        public void Test44_ClassDeclaration_AllMembersPropagation()
        {
            // 测试：类所有成员被标记 -> 整个类被标记
            string source = "class C { int x; void M() {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is MemberDeclarationSyntax);
            Assert.DoesNotContain("class C", result.ToFullString());
        }

        [Fact]
        public void Test45_TupleExpression_AllElementsPropagation()
        {
            // 测试：元组所有元素被标记 -> 整个元组表达式被标记
            string source = "class C { void M() { var t = (1, 2); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ArgumentSyntax);
            Assert.DoesNotContain("(1, 2)", result.ToFullString());
        }

        [Fact]
        public void Test46_AwaitExpression_ExpressionPropagation()
        {
            // 测试：await 表达式被标记 -> 整个 await 表达式被标记
            string source = "using System.Threading.Tasks; class C { async Task M(Task t) { await t; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "t");
            Assert.DoesNotContain("await", result.ToFullString());
        }

        [Fact]
        public void Test47_ThrowStatement_ExpressionPropagation()
        {
            // 测试：throw 表达式被标记 -> 整个 throw 语句被标记
            string source = "using System; class C { void M() { throw new Exception(); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ObjectCreationExpressionSyntax);
            Assert.DoesNotContain("throw", result.ToFullString());
        }

        [Fact]
        public void Test48_LabeledStatement_StatementPropagation()
        {
            // 测试：带标签语句的语句被标记 -> 整个带标签语句被标记
            string source = "class C { void M() { label: int x = 1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LocalDeclarationStatementSyntax);
            Assert.DoesNotContain("label:", result.ToFullString());
        }

        [Fact]
        public void Test49_CheckedStatement_BlockPropagation()
        {
            // 测试：checked 语句块被标记 -> 整个 checked 语句被标记
            string source = "class C { void M() { checked { int x = 1; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is BlockSyntax);
            Assert.DoesNotContain("checked", result.ToFullString());
        }

        [Fact]
        public void Test50_UnsafeStatement_BlockPropagation()
        {
            // 测试：unsafe 语句块被标记 -> 整个 unsafe 语句被标记
            string source = "class C { void M() { unsafe { int* p; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is BlockSyntax);
            Assert.DoesNotContain("unsafe", result.ToFullString());
        }

        [Fact]
        public void Test51_ConstructorDeclaration_BodyPropagation()
        {
            // 测试：构造函数主体被标记 -> 整个构造函数被标记
            string source = "class C { C() { int x = 1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is BlockSyntax);
            Assert.DoesNotContain("C()", result.ToFullString());
        }

        [Fact]
        public void Test52_ArrowExpressionClause_Propagation()
        {
            // 测试：箭头表达式被标记 -> 整个箭头表达式子句被标记
            string source = "class C { int M() => 1; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("=> 1", result.ToFullString());
        }

        [Fact]
        public void Test53_LambdaExpression_BodyPropagation()
        {
            // 测试：Lambda 表达式主体被标记 -> 整个 Lambda 表达式被标记
            string source = "using System; class C { void M() { Action a = () => { int x = 1; }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is BlockSyntax);
            Assert.DoesNotContain("=>", result.ToFullString());
        }

        [Fact]
        public void Test54_SwitchExpression_AllArmsPropagation()
        {
            // 测试：switch 表达式所有分支被标记 -> 整个 switch 表达式被标记
            string source = "class C { int M(int i) => i switch { 1 => 1, _ => 0 }; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is SwitchExpressionArmSyntax);
            Assert.DoesNotContain("switch", result.ToFullString());
        }

        [Fact]
        public void Test55_ObjectInitializer_AllExpressionsPropagation()
        {
            // 测试：对象初始化器所有表达式被标记 -> 整个对象初始化器被标记
            string source = "class Point { public int X, Y; } class C { void M() { var p = new Point { X = 1, Y = 2 }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is AssignmentExpressionSyntax);
            Assert.DoesNotContain("{ X = 1, Y = 2 }", result.ToFullString());
        }

        [Fact]
        public void Test56_AttributeList_AllAttributesPropagation()
        {
            // 测试：特性列表所有特性被标记 -> 整个特性列表被标记
            string source = "[Serializable, Obsolete] class C { }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is AttributeSyntax);
            Assert.DoesNotContain("[", result.ToFullString());
        }

        [Fact]
        public void Test57_UsingStatement_ExpressionPropagation()
        {
            // 测试：using 语句表达式被标记 -> 整个 using 语句被标记
            string source = "class C { void M(object d) { using((System.IDisposable)d) { } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            // 标记所有的 'd'
            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "d");
            Assert.DoesNotContain("using", result.ToFullString());
        }

        [Fact]
        public void Test58_LockStatement_ExpressionPropagation()
        {
            // 测试：lock 语句表达式被标记 -> 整个 lock 语句被标记
            string source = "class C { void M(object o) { lock(o) { } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "o");
            Assert.DoesNotContain("lock", result.ToFullString());
        }

        [Fact]
        public void Test59_GlobalStatement_Propagation()
        {
            // 测试：顶层语句被标记 -> 整个顶层语句被标记
            string source = "System.Console.WriteLine(1);";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ExpressionStatementSyntax);
            Assert.Empty(result.ToFullString().Trim());
        }

        [Fact]
        public void Test60_WithExpression_ExpressionPropagation()
        {
            // 测试：with 表达式基础表达式被标记 -> 整个 with 表达式被标记
            string source = "record R(int X); class C { void M(R r) { var r2 = r with { X = 1 }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is IdentifierNameSyntax id && id.Identifier.ValueText == "r");
            Assert.DoesNotContain("with", result.ToFullString());
        }

        [Fact]
        public void Test61_WithExpression_InitializerPropagation()
        {
            // 测试：with 表达式初始化器被标记 -> 整个 with 表达式被标记
            string source = "record R(int X); class C { void M(R r) { var r2 = r with { X = 1 }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is InitializerExpressionSyntax);
            Assert.DoesNotContain("with", result.ToFullString());
        }

        [Fact]
        public void Test62_QueryExpression_AllClausesPropagation()
        {
            // 测试：LINQ 查询所有子句被标记 -> 整个查询表达式被标记
            string source = "using System.Linq; class C { void M(int[] arr) { var q = from x in arr select x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is QueryClauseSyntax || n is SelectOrGroupClauseSyntax);
            Assert.DoesNotContain("from", result.ToFullString());
        }

        [Fact]
        public void Test63_PrimaryConstructor_AllUsesPropagation()
        {
            // 测试：主构造函数参数所有用途被标记 -> 参数本身被标记(由 ReferencePropagator 处理，此处仅测试结构)
            // 实际上结构上参数是 ParameterSyntax，如果它在所有地方都不被需要，会被移除。
            string source = "class C(int x) { int X = x; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax);
            Assert.DoesNotContain("(int x)", result.ToFullString());
        }

        [Fact]
        public void Test64_DestructorDeclaration_BodyPropagation()
        {
            // 测试：析构函数主体被标记 -> 整个析构函数被标记
            string source = "class C { ~C() { } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is BlockSyntax);
            Assert.DoesNotContain("~C()", result.ToFullString());
        }

        [Fact]
        public void Test65_OperatorDeclaration_BodyPropagation()
        {
            // 测试：操作符重载主体被标记 -> 整个操作符声明被标记
            string source = "class C { public static C operator +(C a, C b) => a; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ArrowExpressionClauseSyntax);
            Assert.DoesNotContain("operator +", result.ToFullString());
        }

        [Fact]
        public void Test66_ConversionOperatorDeclaration_BodyPropagation()
        {
            // 测试：转换操作符主体被标记 -> 整个转换操作符声明被标记
            string source = "class C { public static implicit operator int(C c) => 1; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ArrowExpressionClauseSyntax);
            Assert.DoesNotContain("implicit operator", result.ToFullString());
        }

        [Fact]
        public void Test67_TypeParameterConstraintClause_AllConstraintsPropagation()
        {
            // 测试：类型参数约束子句所有约束被标记 -> 整个约束子句被标记
            string source = "class C<T> where T : class { }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is TypeParameterConstraintSyntax);
            Assert.DoesNotContain("where", result.ToFullString());
        }

        [Fact]
        public void Test68_AnonymousMethodExpression_BodyPropagation()
        {
            // 测试：匿名方法主体被标记 -> 整个匿名方法被标记
            string source = "using System; class C { void M() { Action a = delegate { }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is BlockSyntax);
            Assert.DoesNotContain("delegate", result.ToFullString());
        }

        [Fact]
        public void Test69_ImplicitObjectCreation_AllArgsPropagation()
        {
            // 测试：隐式对象创建所有参数被标记 -> 整个隐式对象创建被标记
            string source = "class C { void M() { C c = new(1); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ArgumentSyntax);
            Assert.DoesNotContain("new(1)", result.ToFullString());
        }

        [Fact]
        public void Test70_ParenthesizedExpression_ExpressionPropagation()
        {
            // 测试：括号表达式内容被标记 -> 整个括号表达式被标记
            string source = "class C { void M() { int x = (1); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax);
            Assert.DoesNotContain("(1)", result.ToFullString());
        }
    }
}
