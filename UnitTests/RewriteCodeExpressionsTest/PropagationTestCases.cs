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
            // 格式: { source, targetName, expectedMissing, expectedContains }

            // 1. 局部变量简单引用
            yield return new object[] { "class C { void M() { int x = 1; int y = x; } }", "x", new[] { "int x", "int y" }, new string[0] };
            // 2. 局部变量多处引用
            yield return new object[] { "class C { void M() { int x = 1; int y = x; int z = x + 1; } }", "x", new[] { "int x", "int y" }, new[] { "int z = 0 + 1" } };
            // 3. 嵌套作用域引用
            yield return new object[] { "class C { void M() { int x = 1; { int y = x; } } }", "x", new[] { "int x", "int y = x" }, new string[0] };
            // 4. 方法参数引用
            yield return new object[] { "class C { void M(int x) { int y = x; } }", "x", new[] { "int x", "int y = x" }, new string[0] };
            // 5. Lambda 参数引用
            yield return new object[] { "using System; class C { void M() { Action<int> a = x => { int y = x; }; } }", "x", new[] { "int y = x" }, new string[0] };
            // 6. 字段引用
            yield return new object[] { "class C { int x; void M() { int y = x; } }", "x", new[] { "int x", "int y = x" }, new string[0] };
            // 7. 静态字段引用
            yield return new object[] { "class C { static int x; void M() { int y = x; } }", "x", new[] { "static int x", "int y = x" }, new string[0] };
            // 8. 属性引用
            yield return new object[] { "class C { int X { get; set; } void M() { int y = X; } }", "X", new[] { "int X", "int y = X" }, new string[0] };
            // 9. 方法调用引用
            yield return new object[] { "class C { void X() {} void M() { X(); } }", "X", new[] { "void X()", "X()" }, new string[0] };
            // 10. 构造函数参数引用
            yield return new object[] { "class C { int _x; public C(int x) { _x = x; } }", "x", new[] { "int x", "_x = x" }, new string[0] };
            // 11. 别名引用
            yield return new object[] { "using MyInt = System.Int32; class C { void M() { MyInt x = 1; } }", "MyInt", new[] { "using MyInt", "MyInt x" }, new string[0] };
            // 12. 泛型参数引用
            yield return new object[] { "class C<T> { T x; T Get() => x; }", "T", new[] { "T x", "T Get()" }, new string[0] };
            // 13. 特性引用
            yield return new object[] { "[My] class C {} class MyAttribute : System.Attribute {}", "MyAttribute", new[] { "[My]", "class MyAttribute" }, new string[0] };
            // 14. Out 参数引用
            yield return new object[] { "class C { void M(out int x) { x = 1; } void M2() { int a; M(out a); } }", "x", new[] { "out int x", "x = 1" }, new string[0] };
            // 15. Ref 参数引用
            yield return new object[] { "class C { void M(ref int x) { x++; } }", "x", new[] { "ref int x", "x++" }, new string[0] };
            // 16. 模式匹配引用
            yield return new object[] { "class C { void M(object o) { if (o is int x) { int y = x; } } }", "x", new[] { "int y = x" }, new string[0] };
            // 17. Switch 表达式引用
            yield return new object[] { "class C { int M(int i) => i switch { 1 => i, _ => 0 }; }", "i", new[] { "1 => i" }, new[] { "1 => 0" } };
            // 18. 三元表达式引用
            yield return new object[] { "class C { void M(bool b, int x) { int y = b ? x : 0; } }", "x", new[] { "? x" }, new[] { "? 0" } };
            // 19. 空合并表达式引用
            yield return new object[] { "class C { void M(string s) { string y = s ?? \"\"; } }", "s", new[] { "s ?? \"\"" }, new string[0] };
            // 20. 字符串插值引用
            yield return new object[] { "class C { void M(int x) { string s = $\"{x}\"; } }", "x", new[] { "{x}" }, new[] { "{0}" } };
            // 21. 对象初始化器引用
            yield return new object[] { "class C { int X; void M() { var c = new C { X = 1 }; } }", "X", new[] { "X = 1" }, new string[0] };
            // 22. 集合初始化器引用
            yield return new object[] { "using System.Collections.Generic; class C { void M() { var l = new List<int> { 1 }; } }", "List", new[] { "new List<int>" }, new string[0] };
            // 23. 数组索引引用
            yield return new object[] { "class C { void M(int[] a, int i) { int x = a[i]; } }", "i", new[] { "a[i]" }, new[] { "a[0]" } };
            // 24. 递增表达式引用
            yield return new object[] { "class C { void M(int x) { x++; } }", "x", new[] { "x++" }, new string[0] };
            // 25. 复合赋值引用
            yield return new object[] { "class C { void M(int x) { x += 1; } }", "x", new[] { "x += 1" }, new string[0] };
            // 26. 二元运算引用
            yield return new object[] { "class C { void M(int x, int y) { int z = x * y; } }", "x", new[] { "x * y" }, new[] { "0 * y" } };
            // 27. 一元运算引用
            yield return new object[] { "class C { void M(int x) { int y = -x; } }", "x", new[] { "-x" }, new[] { "-0" } };
            // 28. 类型转换引用
            yield return new object[] { "class C { void M(object o) { int x = (int)o; } }", "o", new[] { "(int)o" }, new[] { "(int)null" } };
            // 29. Nameof 引用
            yield return new object[] { "class C { int x; string s = nameof(x); }", "x", new[] { "nameof(x)" }, new string[0] };
            // 30. Typeof 引用
            yield return new object[] { "class C { System.Type t = typeof(C); }", "C", new[] { "typeof(C)" }, new string[0] };
            // 31. Default 引用
            yield return new object[] { "class C<T> { T x = default(T); }", "T", new[] { "default(T)" }, new string[0] };
            // 32. This 引用
            yield return new object[] { "class C { void M() { object o = this; } }", "C", new[] { "this" }, new string[0] };
            // 33. Base 引用
            yield return new object[] { "class B { public virtual void M() {} } class C : B { public override void M() { base.M(); } }", "B", new[] { "base.M()" }, new string[0] };
            // 34. 强制类型转换 (as) 引用
            yield return new object[] { "class C { void M(object o) { var x = o as string; } }", "o", new[] { "o as string" }, new[] { "null as string" } };
            // 35. 属性调用 (getter)
            yield return new object[] { "class C { int X => 1; void M() { int y = X; } }", "X", new[] { "int X", "int y = X" }, new string[0] };
            // 36. 属性调用 (setter)
            yield return new object[] { "class C { int X { get; set; } void M() { X = 1; } }", "X", new[] { "int X", "X = 1" }, new string[0] };
            // 37. 元组解构引用
            yield return new object[] { "class C { void M() { var (x, y) = (1, 2); int z = x; } }", "x", new[] { "int z = x" }, new string[0] };
            // 38. 弃元引用
            yield return new object[] { "class C { void M() { var (_, y) = (1, 2); } }", "y", new[] { "y" }, new string[0] };
            // 39. 局部常量引用
            yield return new object[] { "class C { void M() { const int x = 1; int y = x; } }", "x", new[] { "const int x", "int y = x" }, new string[0] };
            // 40. Using 变量引用
            yield return new object[] { "using System.IO; class C { void M() { using (var s = new MemoryStream()) { s.ReadByte(); } } }", "s", new[] { "s.ReadByte()" }, new string[0] };
            // 41. Foreach 变量引用
            yield return new object[] { "using System.Collections.Generic; class C { void M(List<int> l) { foreach (var x in l) { int y = x; } } }", "x", new[] { "int y = x" }, new string[0] };
            // 42. For 循环变量引用
            yield return new object[] { "class C { void M() { for (int i = 0; i < 10; i++) { int y = i; } } }", "i", new[] { "int y = i", "i < 10", "i++" }, new string[0] };
            // 43. Catch 异常变量引用
            yield return new object[] { "using System; class C { void M() { try {} catch (Exception ex) { string s = ex.Message; } } }", "ex", new[] { "ex.Message" }, new string[0] };
            // 44. Lock 语句引用
            yield return new object[] { "class C { void M(object l) { lock(l) { } } }", "l", new[] { "lock(l)" }, new string[0] };
            // 45. Fixed 语句引用
            yield return new object[] { "class C { unsafe void M(int[] a) { fixed(int* p = a) { int x = *p; } } }", "p", new[] { "int x = *p" }, new string[0] };
            // 46. Stackalloc 引用
            yield return new object[] { "class C { unsafe void M() { int* p = stackalloc int[10]; int x = p[0]; } }", "p", new[] { "p[0]" }, new string[0] };
            // 47. Yield return 引用
            yield return new object[] { "using System.Collections.Generic; class C { IEnumerable<int> M(int x) { yield return x; } }", "x", new[] { "yield return x" }, new[] { "yield return 0" } };
            // 48. Await 表达式引用
            yield return new object[] { "using System.Threading.Tasks; class C { async Task M(Task t) { await t; } }", "t", new[] { "await t" }, new string[0] };
            // 49. Record 类型引用
            yield return new object[] { "record R(int X); class C { void M(R r) { int x = r.X; } }", "r", new[] { "r.X" }, new string[0] };
            // 50. With 表达式引用
            yield return new object[] { "record R(int X); class C { void M(R r) { var r2 = r with { X = 1 }; } }", "r", new[] { "r with" }, new string[0] };
            // 51. Index/Range 引用
            yield return new object[] { "class C { void M(int[] a) { var x = a[^1]; var y = a[1..2]; } }", "a", new[] { "a[^1]", "a[1..2]" }, new string[0] };
            // 52. Dynamic 引用
            yield return new object[] { "class C { void M(dynamic d) { d.DoSomething(); } }", "d", new[] { "d.DoSomething()" }, new string[0] };

            // 53. Nullable 类型引用
            yield return new object[] { "class C { void M(int? x) { int y = x ?? 0; } }", "x", new[] { "int? x", "x ?? 0" }, new string[0] };

            // 54. Foreach 中的元组解构
            yield return new object[] { "using System.Collections.Generic; class C { void M(List<(int, int)> l) { foreach (var (x, y) in l) { int z = x; } } }", "x", new[] { "int z = x" }, new string[0] };

            // 55. Using 声明 (C# 8.0)
            yield return new object[] { "using System.IO; class C { void M() { using var s = new MemoryStream(); long l = s.Length; } }", "s", new[] { "s.Length" }, new string[0] };

            // 56. 局部函数递归调用
            yield return new object[] { "class C { void M() { void LF(int n) { if (n > 0) LF(n - 1); } } }", "LF", new[] { "LF(n - 1)" }, new string[0] };

            // 57. 单个声明中的多个变量
            yield return new object[] { "class C { void M() { int x = 1, y = x; } }", "x", new[] { "int x = 1", "y = x" }, new string[0] };

            // 58. Partial 方法引用
            yield return new object[] { "partial class C { partial void M(); void M2() => M(); }", "M", new[] { "partial void M()", "M()" }, new string[0] };
        }
    }
}
