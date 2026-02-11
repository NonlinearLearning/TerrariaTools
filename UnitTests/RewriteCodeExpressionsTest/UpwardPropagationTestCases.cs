using System.Collections.Generic;

namespace TerrariaTools.UnitTests
{
    public static class UpwardPropagationTestCases
    {
        public static IEnumerable<object[]> GetCases()
        {
            // 格式: { name, source, targetPredicateName, expectedMissing, expectedContains }
            
            // 1. 初始化器 -> 变量声明器
            yield return new object[] { "Initializer_To_Declarator", "class C { void M() { int x = 1; } }", "Literal_1", new[] { "int x" }, new string[0] };
            
            // 2. 所有声明器 -> 变量声明
            yield return new object[] { "AllDeclarators_To_Declaration", "class C { void M() { int x = 1, y = 2; } }", "AllLiterals", new[] { "int x", "int y" }, new string[0] };
            
            // 3. 变量声明 -> 局部声明语句
            yield return new object[] { "Declaration_To_Statement", "class C { void M() { int x = 1; Console.WriteLine(2); } }", "Literal_1", new[] { "int x = 1;" }, new[] { "Console.WriteLine(2)" } };
            
            // 4. 表达式 -> 表达式语句
            yield return new object[] { "Expression_To_Statement", "class C { void M() { M2(); } void M2() {} }", "Invocation", new[] { "M2();" }, new string[0] };
            
            // 5. If 条件 -> If 语句
            yield return new object[] { "IfCondition_To_IfStatement", "class C { void M(bool b) { if (b) { Do(); } } void Do() {} }", "IfCondition_b", new[] { "if (b)" }, new string[0] };
            
            // 6. Switch 表达式 -> Switch 语句
            yield return new object[] { "SwitchExpr_To_SwitchStatement", "class C { void M(int i) { switch(i) { case 1: break; } } }", "Identifier_i", new[] { "switch" }, new string[0] };
            
            // 7. 代码块内所有语句 -> 代码块
            yield return new object[] { "AllStatements_To_Block", "class C { void M() { { int x = 1; int y = 2; } } }", "AllLiterals", new[] { "{ int x = 1; int y = 2; }" }, new string[0] };
            
            // 8. 二元表达式两边 -> 二元表达式
            yield return new object[] { "BothSides_To_BinaryExpr", "class C { void M() { int x = 1 + 2; } }", "AllLiterals", new[] { "1 + 2" }, new string[0] };
            
            // 9. 成员访问基础 -> 成员访问
            yield return new object[] { "MemberAccessBase_To_MemberAccess", "class C { void M(C c) { var x = c.M2(); } void M2() {} }", "Identifier_c", new[] { "c.M2" }, new string[0] };
            
            // 10. Try 块内所有语句 -> Try 语句
            yield return new object[] { "TryBody_To_TryStatement", "class C { void M() { try { int x = 1; } catch { } } }", "Literal_1", new[] { "try" }, new string[0] };
        }
    }
}