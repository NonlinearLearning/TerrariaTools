using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
using Xunit;

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

        [Fact]
        public void Test01_LocalVariable_SimpleReference()
        {
            // 测试：局部变量声明被标记 -> 其唯一引用被标记并移除
            string source = "class C { void M() { int x = 1; int y = x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 标记变量 x 的声�?
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int x", resultText);
            Assert.DoesNotContain("int y", resultText); // y 依赖�?x，且 y 本身也被标记移除（通过向上/向左传播�?
            }

        [Fact]
        public void Test02_LocalVariable_MultipleReferences()
        {
            // 测试：局部变量声明被标记 -> 其所有引用都被移除或替换为默认�?
             string source = "class C { void M() { int x = 1; int y = x; int z = x + 1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int x", resultText);
            Assert.DoesNotContain("int y", resultText); // y 仅依赖于 x，因此被完全移除
            Assert.Contains("intz=default(int)+1", resultText.Replace(" ", "")); // z 包含额外部分，因此仅 x 被替�?
        }

        [Fact]
        public void Test03_LocalVariable_NestedScopes()
        {
            // 测试：局部变量在嵌套作用域内的引用被移除
            string source = "class C { void M() { int x = 1; { int y = x; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int x", resultText);
            Assert.DoesNotContain("int y = x", resultText);
        }

        [Fact]
        public void Test04_MethodParameter_SimpleReference()
        {
            // 测试：方法参数被标记 -> 其引用被移除
            string source = "class C { void M(int x) { int y = x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 标记参数 x
            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int x", resultText);
            Assert.DoesNotContain("int y = x", resultText);
        }

        [Fact]
        public void Test05_MethodParameter_MultipleReferences()
        {
            // 测试：方法参数被标记 -> 所有引用都被移�?
            string source = "class C { void M(int x) { int y = x; Do(x); } void Do(int a) {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int x", resultText);
            Assert.DoesNotContain("int y = x", resultText);
            Assert.DoesNotContain("Do(x)", resultText);
        }

        [Fact]
        public void Test06_MethodParameter_InLambda()
        {
            // 测试：方法参数在内部 Lambda 表达式中的引用被移除
            string source = "using System; class C { void M(int x) { Action a = () => { int y = x; }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int x", resultText);
            Assert.DoesNotContain("int y = x", resultText);
        }

        [Fact]
        public void Test07_Lambda_Parenthesized_Parameter()
        {
            // 测试：带括号 Lambda 参数被标�?-> Lambda 体内引用被移�?
            string source = "using System; class C { void M() { Action<int> a = (x) => { int y = x; }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int y = x", resultText);
        }

        [Fact]
        public void Test08_Lambda_Simple_Parameter()
        {
            // 测试：简�?Lambda 参数被标�?-> Lambda 体内引用被移�?
            string source = "using System; class C { void M() { Action<int> a = x => { int y = x; }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int y = x", resultText);
        }

        [Fact]
        public void Test09_Lambda_LocalVariable()
        {
            // 测试：Lambda 体内局部变量声明被标记 -> 引用被移�?
            string source = "using System; class C { void M() { Action a = () => { int x = 1; int y = x; }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int x = 1", resultText);
            Assert.DoesNotContain("int y = x", resultText);
        }

        [Fact]
        public void Test10_AnonymousMethod_Parameter()
        {
            // 测试：匿名方法参数被标记 -> 引用被移�?
            string source = "using System; class C { void M() { Action<int> a = delegate(int x) { int y = x; }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int y = x", resultText);
        }

        [Fact]
        public void Test11_LocalFunction_Parameter()
        {
            // 测试：局部函数参数被标记 -> 引用被移�?
            string source = "class C { void M() { void LF(int x) { int y = x; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int y = x", resultText);
        }

        [Fact]
        public void Test12_LocalFunction_OuterVariable()
        {
            // 测试：外部变量被标记 -> 局部函数内部的引用被移�?
            string source = "class C { void M() { int x = 1; void LF() { int y = x; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int x = 1", resultText);
            Assert.DoesNotContain("int y = x", resultText);
        }

        [Fact]
        public void Test13_PropertyAccessor_Get()
        {
            // 测试：属�?Get 访问器内的变量引�?
            string source = "class C { int Prop { get { int x = 1; return x; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int x = 1", resultText);
            Assert.DoesNotContain("return x", resultText);
        }

        [Fact]
        public void Test14_PropertyAccessor_Set_Value()
        {
            // 测试：属�?Set 访问器内的变量引�?
            string source = "class C { int Prop { set { int x = 1; int y = x; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);

            Assert.DoesNotContain("int y = x", result.ToFullString());
        }

        [Fact]
        public void Test15_Constructor_Parameter()
        {
            // 测试：构造函数参数被标记 -> 引用被移�?
            string source = "class C { int _x; public C(int x) { _x = x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("_x = x", resultText);
        }

        [Fact]
        public void Test16_Constructor_Initializer()
        {
            // 测试：构造函数初始化器中的参数引用被移除
            string source = "class B { public B(int x) {} } class C : B { public C(int x) : base(x) {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x" && p.Parent.Parent is ConstructorDeclarationSyntax c && c.Identifier.ValueText == "C", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain(": base(x)", resultText);
        }

        [Fact]
        public void Test17_Destructor_LocalVariable()
        {
            // 测试：析构函数中的局部变量引用被移除
            string source = "class C { ~C() { int x = 1; int y = x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            Assert.DoesNotContain("int y = x", resultText);
        }

        [Fact]
        public void Test18_ArrowExpressionClause_Method()
        {
            // 测试：表达式主体方法的参数引用被移除
            string source = "class C { int AddOne(int x) => x + 1; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();
            // 应该移除参数 x，并将引用替换为默认�?
            Assert.Contains("int AddOne()", resultText);
            Assert.Contains("=> default(int)", resultText);
            Assert.Contains("+ 1", resultText);
        }

        [Fact]
        public void Test19_ArrowExpressionClause_Property()
        {
            // 测试：表达式主体属性的变量引用（局部函数版本）
            string source = "class C { void M() { int x = 1; int Prop() => x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);

            Assert.DoesNotContain("=> x", result.ToFullString());
        }

        [Fact]
        public void Test20_Negative_UnmarkedDeclaration()
        {
            // 测试：声明未被标�?-> 引用不应被移�?
            string source = "class C { void M() { int x = 1; int y = x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 没有任何标记
            var result = ExpressionProcessor.RemoveParts(root, n => false, model);

            var resultText = result.ToFullString();
            Assert.Contains("int x = 1", resultText);
            Assert.Contains("int y = x", resultText);
        }

        [Fact]
        public void Test21_GenericMethod_TypeParameterReference()
        {
            // 测试：泛型方法参数被标记 -> 引用被移�?
              string source = "class C { void M<T>(T x) { T y = x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("T y = x", result.ToFullString());
        }

        [Fact]
        public void Test22_ExtensionMethod_ThisParameterReference()
        {
            // 测试：扩展方�?this 参数被标�?-> 引用被移�?
            string source = "static class Extensions { public static void M(this string s) { int len = s.Length; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "s", model);
            Assert.DoesNotContain("s.Length", result.ToFullString());
        }

        [Fact]
        public void Test23_OutParameter_Reference()
        {
            // 测试：out 参数被标�?-> 赋值引用被移除
            string source = "class C { void M(out int x) { x = 1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("x = 1", result.ToFullString());
        }

        [Fact]
        public void Test24_RefParameter_Reference()
        {
            // 测试：ref 参数被标�?-> 引用被移�?
            string source = "class C { void M(ref int x) { int y = x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("int y = x", result.ToFullString());
        }

        [Fact]
        public void Test25_TupleDeconstruction_Reference()
        {
            // 测试：元组解构变量被标记 -> 引用被移�?
            string source = "class C { void M() { var (x, y) = (1, 2); int z = x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is SingleVariableDesignationSyntax s && s.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("int z = x", result.ToFullString());
        }

        [Fact]
        public void Test26_SwitchExpression_ArmReference()
        {
            // 测试：switch 表达式分支变量被标记 -> 引用被移�?
            string source = "class C { int M(object o) => o switch { string s => s.Length, _ => 0 }; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is SingleVariableDesignationSyntax s && s.Identifier.ValueText == "s", model);
            Assert.DoesNotContain("s.Length", result.ToFullString());
        }

        [Fact]
        public void Test27_IsPattern_VariableReference()
        {
            // 测试：is 模式变量被标�?-> 引用被移�?
            string source = "class C { void M(object o) { if (o is string s) { int len = s.Length; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is SingleVariableDesignationSyntax s && s.Identifier.ValueText == "s", model);
            Assert.DoesNotContain("s.Length", result.ToFullString());
        }

        [Fact]
        public void Test28_LINQ_FromVariableReference()
        {
            // 测试：LINQ from 变量被标�?-> 引用被移�?
            string source = "using System.Linq; class C { void M(int[] arr) { var q = from x in arr select x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is FromClauseSyntax f && f.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("select x", result.ToFullString());
        }

        [Fact]
        public void Test29_Foreach_VariableReference()
        {
            // 测试：foreach 变量被标�?-> 引用被移�?
            string source = "class C { void M(int[] arr) { foreach(var x in arr) { int y = x; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ForEachStatementSyntax f && f.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("int y = x", result.ToFullString());
        }

        [Fact]
        public void Test30_For_VariableReference()
        {
            // 测试：for 变量被标�?-> 引用被移�?
             string source = "class C { void M() { for(int i = 0; i < 10; i++) { int y = i; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "i", model);
            Assert.DoesNotContain("int y = i", result.ToFullString());
        }

        [Fact]
        public void Test31_Catch_VariableReference()
        {
            // 测试：catch 异常变量被标�?-> 引用被移�?
            string source = "using System; class C { void M() { try { } catch(Exception ex) { string s = ex.Message; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is CatchDeclarationSyntax c && c.Identifier.ValueText == "ex", model);
            Assert.DoesNotContain("ex.Message", result.ToFullString());
        }

        [Fact]
        public void Test32_UsingDeclaration_Reference()
        {
            // 测试：C# 8.0 using 声明变量被标�?-> 引用被移�?
             string source = "using System.IO; class C { void M() { using var s = new MemoryStream(); long l = s.Length; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "s", model);
            Assert.DoesNotContain("s.Length", result.ToFullString());
        }

        [Fact]
        public void Test33_Indexer_ArgumentReference()
        {
            // 测试：索引器参数引用被移�?
            string source = "class C { int this[int i] { get { return i; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ParameterSyntax p && p.Identifier.ValueText == "i", model);
            Assert.DoesNotContain("return i", result.ToFullString());
        }

        [Fact]
        public void Test34_LocalFunction_RecursiveReference()
        {
            // 测试：局部函数递归引用被移�?
            string source = "class C { void M() { void LF(int n) { if (n > 0) LF(n - 1); } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            // 标记局部函�?LF 本身
            var result = ExpressionProcessor.RemoveParts(root, n => n is LocalFunctionStatementSyntax l && l.Identifier.ValueText == "LF", model);
            Assert.DoesNotContain("LF(n - 1)", result.ToFullString());
        }

        [Fact]
        public void Test35_Multiple_Interdependent_Variables()
        {
            // 测试：多个相互依赖的变量，其中一个被标记 -> 链式移除
            string source = "class C { void M() { int x = 1; int y = x; int z = y; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);
            var resultText = result.ToFullString();
            Assert.DoesNotContain("int x", resultText);
            Assert.DoesNotContain("int y", resultText);
            Assert.DoesNotContain("int z", resultText);
        }

        [Fact]
        public void Test36_Field_Reference()
        {
            // 测试：字段被标记 -> 引用被移�?
             string source = "class C { int x; void M() { x = 1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("x = 1", result.ToFullString());
        }

        [Fact]
        public void Test37_Property_Reference()
        {
            // 测试：属性被标记 -> 引用被移�?
             string source = "class C { int Prop { get; set; } void M() { Prop = 1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is PropertyDeclarationSyntax p && p.Identifier.ValueText == "Prop", model);
            Assert.DoesNotContain("Prop = 1", result.ToFullString());
        }

        [Fact]
        public void Test38_Method_Reference()
        {
            // 测试：方法被标记 -> 引用被移�?
            string source = "class C { void M1() { M2(); } void M2() {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is MethodDeclarationSyntax m && m.Identifier.ValueText == "M2", model);
            Assert.DoesNotContain("M2()", result.ToFullString());
        }

        [Fact]
        public void Test39_Event_Reference()
        {
            // 测试：事件被标记 -> 引用被移�?
            string source = "using System; class C { event EventHandler E; void M() { E?.Invoke(this, EventArgs.Empty); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "E", model);
            Assert.DoesNotContain("E?.Invoke", result.ToFullString());
        }

        [Fact]
        public void Test40_Indexer_Reference()
        {
            // 测试：索引器被标�?-> 引用被移�?
            string source = "class C { int this[int i] => i; void M() { int x = this[0]; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is IndexerDeclarationSyntax, model);
            Assert.DoesNotContain("this[0]", result.ToFullString());
        }

        [Fact]
        public void Test41_Constructor_Reference()
        {
            // 测试：构造函数被标记 -> 引用被移�?
            string source = "class C { public C(int i) {} void M() { var c = new C(1); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ConstructorDeclarationSyntax, model);
            Assert.DoesNotContain("new C(1)", result.ToFullString());
        }

        [Fact]
        public void Test42_Destructor_Reference()
        {
            // 测试：析构函数（不直接引用，但应能标记）
            string source = "class C { ~C() {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is DestructorDeclarationSyntax, model);
            Assert.DoesNotContain("~C()", result.ToFullString());
        }

        [Fact]
        public void Test43_Operator_Reference()
        {
            // 测试：操作符被标�?-> 引用被移�?
            string source = "class C { public static C operator +(C a, C b) => a; void M(C c1, C c2) { var c3 = c1 + c2; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is OperatorDeclarationSyntax, model);
            Assert.DoesNotContain("c1 + c2", result.ToFullString());
        }

        [Fact]
        public void Test44_ConversionOperator_Reference()
        {
            // 测试：转换操作符被标�?-> 引用被移�?
    string source = "class C { public static implicit operator int(C c) => 0; void M(C c) { int i = (int)c; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ConversionOperatorDeclarationSyntax, model);
            Assert.DoesNotContain("(int)c", result.ToFullString());
        }

        [Fact]
        public void Test45_EnumMember_Reference()
        {
            // 测试：枚举成员被标记 -> 引用被移�?
            string source = "enum E { A, B } class C { void M() { E e = E.A; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is EnumMemberDeclarationSyntax e && e.Identifier.ValueText == "A", model);
            Assert.DoesNotContain("E.A", result.ToFullString());
        }

        [Fact]
        public void Test46_Constant_Reference()
        {
            // 测试：常量被标记 -> 引用被移�?
            string source = "class C { const int X = 1; void M() { int y = X; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "X", model);
            Assert.DoesNotContain("int y = X", result.ToFullString());
        }

        [Fact]
        public void Test47_StaticField_Reference()
        {
            // 测试：静态字段被标记 -> 引用被移�?
            string source = "class C { static int x; void M() { x = 1; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("x = 1", result.ToFullString());
        }

        [Fact]
        public void Test48_InterfaceMethod_Reference()
        {
            // 测试：接口方法被标记 -> 引用被移�?
            string source = "interface I { void M(); } class C { void Do(I i) { i.M(); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is MethodDeclarationSyntax m && m.Identifier.ValueText == "M", model);
            Assert.DoesNotContain("i.M()", result.ToFullString());
        }

        [Fact]
        public void Test49_BaseMethod_Reference()
        {
            // 测试：基类方法被标记 -> 派生�?override 引用被移�?
            string source = "class B { public virtual void M() {} } class D : B { public override void M() { base.M(); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is MethodDeclarationSyntax m && m.Identifier.ValueText == "M" && m.Parent is ClassDeclarationSyntax c && c.Identifier.ValueText == "B", model);
            Assert.DoesNotContain("base.M()", result.ToFullString());
        }

        [Fact]
        public void Test50_AnonymousType_PropertyReference()
        {
            // 测试：匿名类型属性引�?
            string source = "class C { void M() { var anon = new { x = 1 }; int y = anon.x; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is AnonymousObjectMemberDeclaratorSyntax a && a.NameEquals?.Name.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("anon.x", result.ToFullString());
        }

        [Fact]
        public void Test51_LocalFunction_VariableReference()
        {
            // 测试：局部函数引用外部变�?-> 变量标记 -> 局部函数内引用移除
            string source = "class C { void M() { int x = 1; void LF() { int y = x; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("int y = x", result.ToFullString());
        }

        [Fact]
        public void Test52_Lambda_VariableReference()
        {
            // 测试：Lambda 引用外部变量 -> 变量标记 -> Lambda 内引用移�?
            string source = "using System; class C { void M() { int x = 1; Action a = () => Console.WriteLine(x); } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("Console.WriteLine(x)", result.ToFullString());
        }

        [Fact]
        public void Test53_ObjectInitializer_PropertyReference()
        {
            // 测试：对象初始化器属性引�?
              string source = "class C { public int X; void M() { var c = new C { X = 1 }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "X", model);
            Assert.DoesNotContain("X = 1", result.ToFullString());
        }

        [Fact]
        public void Test54_CollectionInitializer_Reference()
        {
            // 测试：集合初始化器引用（Add 方法�?
    string source = "using System.Collections.Generic; class C { void M() { var list = new List<int> { 1 }; } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            // 标记 List<T>.Add 方法（虽然是在库中，但模拟场景）
            // 这里我们标记 1 这个表达式，看是否被移除
            var result = ExpressionProcessor.RemoveParts(root, n => n is LiteralExpressionSyntax l && l.Token.ValueText == "1", model);
            Assert.DoesNotContain("1", result.ToFullString());
        }

        [Fact]
        public void Test55_Attribute_Reference()
        {
            // 测试：特性类被标�?-> 特性使用被移除
            string source = "using System; [My] class MyAttribute : Attribute {} class C {}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ClassDeclarationSyntax c && c.Identifier.ValueText == "MyAttribute", model);
            Assert.DoesNotContain("[My]", result.ToFullString());
        }

        [Fact]
        public void Test56_AttributeArgument_Reference()
        {
            // 测试：特性参数引�?
            string source = "using System; class MyAttribute : Attribute { public MyAttribute(int i) {} } [My(X)] class C { const int X = 1; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "X", model);
            Assert.DoesNotContain("My(X)", result.ToFullString());
        }

        [Fact]
        public void Test57_DefaultParameterValue_Reference()
        {
            // 测试：默认参数值引�?
        string source = "class C { const int X = 1; void M(int i = X) {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "X", model);
            Assert.DoesNotContain("= X", result.ToFullString());
        }

        [Fact]
        public void Test58_TypeArgument_Reference()
        {
            // 测试：类型实参引�?
         string source = "class G<T> {} class C { class T1 {} G<T1> g; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ClassDeclarationSyntax c && c.Identifier.ValueText == "T1", model);
            Assert.DoesNotContain("G<T1>", result.ToFullString());
        }

        [Fact]
        public void Test59_GenericConstraint_Reference()
        {
            // 测试：泛型约束引�?
            string source = "interface I {} class G<T> where T : I {}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is InterfaceDeclarationSyntax i && i.Identifier.ValueText == "I", model);
            Assert.DoesNotContain("where T : I", result.ToFullString());
        }

        [Fact]
        public void Test60_BaseClass_Reference()
        {
            // 测试：基类引�?
            string source = "class B {} class D : B {}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ClassDeclarationSyntax c && c.Identifier.ValueText == "B", model);
            Assert.DoesNotContain(": B", result.ToFullString());
        }

        [Fact]
        public void Test61_InterfaceImplementation_Reference()
        {
            // 测试：实现接口引�?
            string source = "interface I {} class C : I {}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is InterfaceDeclarationSyntax i && i.Identifier.ValueText == "I", model);
            Assert.DoesNotContain(": I", result.ToFullString());
        }

        [Fact]
        public void Test62_Nameof_Reference()
        {
            // 测试：nameof 引用
            string source = "class C { int x; string Name => nameof(x); }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);
            Assert.DoesNotContain("nameof(x)", result.ToFullString());
        }

        [Fact]
        public void Test63_UsingAlias_Reference()
        {
            // 测试：using 别名引用
            string source = "using MyInt = System.Int32; class C { MyInt x; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is UsingDirectiveSyntax u && u.Alias?.Name.Identifier.ValueText == "MyInt", model);
            Assert.DoesNotContain("MyInt x", result.ToFullString());
        }

        [Fact]
        public void Test64_Lock_ExpressionReference()
        {
            // 测试：lock 表达式引�?
             string source = "class C { object obj = new object(); void M() { lock(obj) {} } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "obj", model);
            Assert.DoesNotContain("lock(obj)", result.ToFullString());
        }

        [Fact]
        public void Test65_Fixed_PointerReference()
        {
            // 测试：fixed 指针引用
            string source = "class C { unsafe void M(int[] arr) { fixed(int* p = arr) { int x = *p; } } }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "p", model);
            Assert.DoesNotContain("int x = *p", result.ToFullString());
        }

        [Fact]
        public void Test66_Delegate_Reference()
        {
            // 测试：委托类型被标记 -> 使用被移�?
            string source = "delegate void D(); class C { D d; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is DelegateDeclarationSyntax d && d.Identifier.ValueText == "D", model);
            Assert.DoesNotContain("D d", result.ToFullString());
        }

        [Fact]
        public void Test67_Namespace_Reference()
        {
            // 测试：命名空间引用（通过 using�?
            string source = "namespace N { class A {} } namespace M { using N; class B : A {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is NamespaceDeclarationSyntax ns && ns.Name.ToString() == "N", model);
            Assert.DoesNotContain("using N", result.ToFullString());
        }

        [Fact]
        public void Test68_PartialClass_Reference()
        {
            // 测试：分部类的一个部分被标记
            string source = "partial class C {} partial class C { void M() {} }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is ClassDeclarationSyntax c && c.Members.Count == 0, model);
            // 应该只移除空的部分，保留有方法的部分
            Assert.Contains("void M()", result.ToFullString());
        }

        [Fact]
        public void Test69_Recursive_Property_Dependency()
        {
            // 测试：递归属性依�?
            string source = "class C { int P1 => P2; int P2 => P1; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is PropertyDeclarationSyntax p && p.Identifier.ValueText == "P1", model);
            Assert.DoesNotContain("P1", result.ToFullString());
            Assert.DoesNotContain("P2", result.ToFullString());
        }

        [Fact]
        public void Test70_Deep_Dependency_Chain()
        {
            // 测试：深层依赖链
            string source = "class C { int a = 1; int b => a; int c => b; int d => c; int e => d; }";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "a", model);
            var text = result.ToFullString();
            Assert.DoesNotContain("int a", text);
            Assert.DoesNotContain("int b", text);
            Assert.DoesNotContain("int c", text);
            Assert.DoesNotContain("int d", text);
            Assert.DoesNotContain("int e", text);
        }

        [Fact]
        public void Test71_ComplexVariablePropagation_SliderInput()
        {
            // 测试：HandleSliderHorizontalInput 场景�?
            // 当变�?x 的声明被标记移除时，其后的赋值语句和 return 语句中的引用都应被清理�?
            string source = @"
class PlayerInput { public static dynamic GamepadThumbstickLeft = new { X = 0f }; }
class MathHelper { public static float Lerp(float a, float b, float t) => 0; public static float Clamp(float v, float min, float max) => 0; }
class Math { public static float Abs(float v) => 0; public static int Sign(float v) => 0; }

class C {
    public static float HandleSliderHorizontalInput(float currentValue, float min, float max, float deadZone = 0.2f, float sensitivity = 0.5f)
    {
        float x = PlayerInput.GamepadThumbstickLeft.X;
        x = ((!(x < 0f - deadZone) && !(x > deadZone)) ? 0f : (MathHelper.Lerp(0f, sensitivity / 60f, (Math.Abs(x) - deadZone) / (1f - deadZone)) * (float)Math.Sign(x)));
        return MathHelper.Clamp((currentValue - min) / (max - min) + x, 0f, 1f) * (max - min) + min;
    }
}";
            var model = GetSemanticModel(source);
            var root = model.SyntaxTree.GetRoot();

            // 模拟标记：标记变�?x 的初始化表达式（从而导致变�?x 的声明被移除�?
            // 或者直接标记变�?x 的声�?
            var result = ExpressionProcessor.RemoveParts(root, n => n is VariableDeclaratorSyntax v && v.Identifier.ValueText == "x", model);

            var resultText = result.ToFullString();

             // 验证�?
             // 1. 变量 x 的声明应该消�?
             Assert.DoesNotContain("float x =", resultText);
             // 2. �?x 的赋值语句应该消失（因为赋值目标已失效�?
              Assert.DoesNotContain("x = ((!(x < 0f - deadZone)", resultText);
             // 3. return 语句中的 x 引用应该被清�?
             // 由于 return 语句是受保护的，它不会被删除，但其中的表达式部分会被简�?
             // �?(currentValue - min) / (max - min) + x 中，+ x 部分应该因为 x 失效而被移除
             Assert.DoesNotContain("+ x", resultText);

             // 调整断言以匹配可能的占位符行为或简化后的行�?
             var normalizedText = resultText.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
             Assert.Contains("returnMathHelper.Clamp((currentValue-min)/(max-min)", normalizedText);
         }
    }
}
