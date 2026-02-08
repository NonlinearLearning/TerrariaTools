/**
 * 功能描述：定义语义传播的所有测试用例，涵盖 50 种不同的 C# 语法场景
 */
using System.Collections.Generic;

namespace TerrariaTools.UnitTests
{
    public static class PropagationTestCases
    {
        /// <summary>
        /// 获取所有语义传播测试用例
        /// </summary>
        /// <returns>测试用例的数据集合</returns>
        public static IEnumerable<object[]> GetCases()
        {
            // 1. 局部变量简单引用
            yield return new object[] { "class C { void M() { int x = 1; int y = x; } }", "x", new[] { "x" } };
            // 2. 局部变量多处引用
            yield return new object[] { "class C { void M() { int x = 1; int y = x; int z = x + 1; } }", "x", new[] { "x" } };
            // 3. 嵌套作用域引用
            yield return new object[] { "class C { void M() { int x = 1; { int y = x; } } }", "x", new[] { "x" } };
            // 4. 方法参数引用
            yield return new object[] { "class C { void M(int x) { int y = x; } }", "x", new[] { "x" } };
            // 5. Lambda 参数引用
            yield return new object[] { "using System; class C { void M() { Action<int> a = x => { int y = x; }; } }", "x", new[] { "x" } };
            // 6. 字段引用
            yield return new object[] { "class C { int x; void M() { int y = x; } }", "x", new[] { "x" } };
            // 7. 静态字段引用
            yield return new object[] { "class C { static int x; void M() { int y = x; } }", "x", new[] { "x" } };
            // 8. 属性引用
            yield return new object[] { "class C { int X { get; set; } void M() { int y = X; } }", "X", new[] { "X" } };
            // 9. 方法调用引用
            yield return new object[] { "class C { void X() {} void M() { X(); } }", "X", new[] { "X" } };
            // 10. 构造函数参数引用
            yield return new object[] { "class C { int _x; public C(int x) { _x = x; } }", "x", new[] { "x" } };
            // 11. 别名引用 (标记目标改为 MyInt 以简化查询)
            yield return new object[] { "using MyInt = System.Int32; class C { void M() { MyInt x = 1; } }", "MyInt", new[] { "MyInt" } };
            // 12. 泛型参数引用
            yield return new object[] { "class C<T> { T x; T Get() => x; }", "T", new[] { "T" } };
            // 13. 特性引用
            yield return new object[] { "[My] class C {} class MyAttribute : System.Attribute {}", "MyAttribute", new[] { "My" } };
            // 14. Out 参数引用
            yield return new object[] { "class C { void M(out int x) { x = 1; } void M2() { int a; M(out a); } }", "x", new[] { "x" } };
            // 15. Ref 参数引用
            yield return new object[] { "class C { void M(ref int x) { x++; } }", "x", new[] { "x" } };
            // 16. 模式匹配引用
            yield return new object[] { "class C { void M(object o) { if (o is int x) { int y = x; } } }", "x", new[] { "x" } };
            // 17. Switch 表达式引用
            yield return new object[] { "class C { int M(int i) => i switch { 1 => i, _ => 0 }; }", "i", new[] { "i" } };
            // 18. 三元表达式引用
            yield return new object[] { "class C { void M(bool b, int x) { int y = b ? x : 0; } }", "x", new[] { "x" } };
            // 19. 空合并表达式引用
            yield return new object[] { "class C { void M(string s) { string y = s ?? \"\"; } }", "s", new[] { "s" } };
            // 20. 字符串插值引用
            yield return new object[] { "class C { void M(int x) { string s = $\"{x}\"; } }", "x", new[] { "x" } };
            // 21. 对象初始化器引用
            yield return new object[] { "class C { int X; void M() { var c = new C { X = 1 }; } }", "X", new[] { "X" } };
            // 22. 集合初始化器引用
            yield return new object[] { "using System.Collections.Generic; class C { void M() { var l = new List<int> { 1 }; } }", "List", new[] { "List" } };
            // 23. 数组索引引用
            yield return new object[] { "class C { void M(int[] a, int i) { int x = a[i]; } }", "i", new[] { "i" } };
            // 24. 递增表达式引用
            yield return new object[] { "class C { void M(int x) { x++; } }", "x", new[] { "x" } };
            // 25. 复合赋值引用
            yield return new object[] { "class C { void M(int x) { x += 1; } }", "x", new[] { "x" } };
            // 26. 二元运算引用
            yield return new object[] { "class C { void M(int x, int y) { int z = x * y; } }", "x", new[] { "x" } };
            // 27. 一元运算引用
            yield return new object[] { "class C { void M(int x) { int y = -x; } }", "x", new[] { "x" } };
            // 28. 类型转换引用
            yield return new object[] { "class C { void M(object o) { int x = (int)o; } }", "o", new[] { "o" } };
            // 29. Nameof 引用
            yield return new object[] { "class C { int x; string s = nameof(x); }", "x", new[] { "x" } };
            // 30. Typeof 引用
            yield return new object[] { "class C { System.Type t = typeof(C); }", "C", new[] { "C" } };
            // 31. Default 引用
            yield return new object[] { "class C<T> { T x = default(T); }", "T", new[] { "T" } };
            // 32. This 引用
            yield return new object[] { "class C { void M() { object o = this; } }", "C", new[] { "this" } };
            // 33. Base 引用
            yield return new object[] { "class B { public virtual void M() {} } class C : B { public override void M() { base.M(); } }", "B", new[] { "base" } };
            // 34. 强制类型转换 (as) 引用
            yield return new object[] { "class C { void M(object o) { var x = o as string; } }", "o", new[] { "o" } };
            // 35. 属性调用 (getter)
            yield return new object[] { "class C { int X => 1; void M() { int y = X; } }", "X", new[] { "X" } };
            // 36. 属性调用 (setter)
            yield return new object[] { "class C { int X { get; set; } void M() { X = 1; } }", "X", new[] { "X" } };
            // 37. 元组解构引用
            yield return new object[] { "class C { void M() { var (x, y) = (1, 2); int z = x; } }", "x", new[] { "x" } };
            // 38. 弃元引用
            yield return new object[] { "class C { void M() { var (_, y) = (1, 2); } }", "y", new[] { "y" } };
            // 39. 局部常量引用
            yield return new object[] { "class C { void M() { const int x = 1; int y = x; } }", "x", new[] { "x" } };
            // 40. Using 变量引用
            yield return new object[] { "using System.IO; class C { void M() { using (var s = new MemoryStream()) { s.ReadByte(); } } }", "s", new[] { "s" } };
            // 41. Foreach 变量引用
            yield return new object[] { "using System.Collections.Generic; class C { void M(List<int> l) { foreach (var x in l) { int y = x; } } }", "x", new[] { "x" } };
            // 42. For 循环变量引用
            yield return new object[] { "class C { void M() { for (int i = 0; i < 10; i++) { int y = i; } } }", "i", new[] { "i" } };
            // 43. Catch 异常变量引用
            yield return new object[] { "using System; class C { void M() { try {} catch (Exception ex) { string s = ex.Message; } } }", "ex", new[] { "ex" } };
            // 44. Lock 变量引用
            yield return new object[] { "class C { object l = new object(); void M() { lock(l) {} } }", "l", new[] { "l" } };
            // 45. Fixed 变量引用
            yield return new object[] { "class C { unsafe void M(int[] a) { fixed (int* p = a) { int x = *p; } } }", "p", new[] { "p" } };
            // 46. 索引器引用
            yield return new object[] { "class C { int this[int i] => i; void M() { int y = this[0]; } }", "this", new[] { "this" } };
            // 47. 显式接口实现引用
            yield return new object[] { "interface I { void M(); } class C : I { void I.M() {} }", "I", new[] { "I" } };
            // 48. 委托引用
            yield return new object[] { "using System; class C { void M() { Action a = M; a(); } }", "M", new[] { "M" } };
            // 49. 事件引用
            yield return new object[] { "using System; class C { event Action E; void M() { E?.Invoke(); } }", "E", new[] { "E" } };
            // 50. 运算符重载引用
            yield return new object[] { "class C { public static C operator +(C a, C b) => a; void M(C x, C y) { var z = x + y; } }", "x", new[] { "x" } };
        }
    }
}
