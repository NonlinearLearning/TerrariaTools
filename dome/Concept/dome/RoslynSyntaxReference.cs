using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// Roslyn 语法节点参考指南 (L1-75)
    /// 包含每个节点的中文解释及对应的 C# 代码示例
    /// </summary>
    public class RoslynSyntaxReference
    {
        /* 
         * 1. AccessorDeclarationSyntax
         * 代表属性或索引器的访问器声明（get 或 set）。
         * 例子: get; 或 set { _value = value; }
         */
        public int PropertyWithAccessors { get; set; }

        /* 
         * 2. AccessorListSyntax
         * 代表属性或索引器的访问器列表。
         * 例子: { get; set; }
         */
        public string Name { get; set; }

        /* 
         * 3. AllowsConstraintSyntax (C# 12+)
         * 代表泛型约束中的 allows 关键字，用于 ref struct 等。
         * 例子: where T : allows ref struct
         */
        // public void M<T>() where T : allows ref struct { }

        /* 
         * 4. AnonymousObjectMemberDeclaratorSyntax
         * 代表匿名对象初始化中的成员声明。
         * 例子: Name = "John" (在 new { Name = "John" } 中)
         */
        public object AnonymousObj = new { Name = "John" };

        /* 
         * 5. ArgumentSyntax
         * 代表方法调用或索引器访问中的参数。
         * 例子: "Hello" (在 Console.WriteLine("Hello") 中)
         */
        public void CallMethod() => Console.WriteLine("Hello");

        /* 
         * 6. ArrayRankSpecifierSyntax
         * 代表数组声明中的维度说明符。
         * 例子: [,,] (代表三维数组)
         */
        public int[,,] ThreeDimensionalArray;

        /* 
         * 7. ArrowExpressionClauseSyntax
         * 代表表达式主体定义（箭头函数）。
         * 例子: => x + 1
         */
        public int AddOne(int x) => x + 1;

        /* 
         * 8. AttributeArgumentListSyntax
         * 代表特性（Attribute）中的参数列表。
         * 例子: (1, Name = "Test") (在 [MyAttr(1, Name = "Test")] 中)
         */
        [Obsolete("Use NewMethod instead", false)]
        public void OldMethod() { }

        /* 
         * 9. AttributeArgumentSyntax
         * 代表特性中的单个参数。
         * 例子: "Use NewMethod instead"
         */

        /* 
         * 10. AttributeListSyntax
         * 代表应用在某个语法元素上的特性列表（方括号部分）。
         * 例子: [Serializable, Obsolete]
         */
        [Serializable, Obsolete]
        public class AttributedClass { }

        /* 
         * 11. AttributeSyntax
         * 代表单个特性。
         * 例子: Serializable
         */

        /* 
         * 12. AttributeTargetSpecifierSyntax
         * 代表特性的目标说明符。
         * 例子: assembly:, module:, method:
         */
        // [method: MyAttribute]

        /* 
         * 13. BaseArgumentListSyntax
         * ArgumentListSyntax 和 AttributeArgumentListSyntax 的基类。
         */

        /* 
         * 14. BaseCrefParameterListSyntax
         * XML 文档注释中 cref 引用成员的参数列表基类。
         */

        /* 
         * 15. BaseExpressionColonSyntax
         * 内部使用的基类，用于处理表达式后的冒号。
         */

        /* 
         * 16. BaseExpressionTypeClauseSyntax
         * 内部使用的基类，处理表达式类型子句。
         */

        /* 
         * 17. BaseListSyntax
         * 代表类或接口的基类/接口列表。
         * 例子: : IDisposable, ICloneable
         */
        public class MyDisposable : IDisposable { public void Dispose() { } }

        /* 
         * 18. BaseParameterListSyntax
         * ParameterListSyntax（方法参数列表）的基类。
         */

        /* 
         * 19. BaseParameterSyntax
         * ParameterSyntax（单个参数）的基类。
         */

        /* 
         * 20. BaseTypeSyntax
         * 所有类型引用的基类。
         */

        /* 
         * 21. CatchClauseSyntax
         * 代表 try-catch 中的 catch 块。
         * 例子: catch (Exception ex) { ... }
         */
        public void TryCatch() { try { } catch (Exception ex) { } }

        /* 
         * 22. CatchDeclarationSyntax
         * 代表 catch 子句中的异常声明部分。
         * 例子: (Exception ex)
         */

        /* 
         * 23. CatchFilterClauseSyntax
         * 代表 catch 子句中的 when 过滤条件。
         * 例子: when (ex.InnerException != null)
         */
        public void TryCatchFilter() { try { } catch (Exception ex) when (ex.Message != null) { } }

        /* 
         * 24. CollectionElementSyntax (C# 12+)
         * 代表集合表达式中的单个元素。
         * 例子: 1 (在 [1, 2, 3] 中)
         */
        // int[] arr = [1, 2, 3];

        /* 
         * 25. CompilationUnitSyntax
         * 代表整个源文件的根节点。
         */

        /* 
         * 26. ConstructorInitializerSyntax
         * 代表构造函数初始化器（base 或 this 调用）。
         * 例子: : base() 或 : this(10)
         */
        public class BaseClass { }
        public class DerivedClass : BaseClass { public DerivedClass() : base() { } }

        /* 
         * 27. CrefParameterSyntax
         * XML 注释中 cref 引用的单个参数。
         */

        /* 
         * 28. CrefSyntax
         * XML 注释中的成员引用（cref 属性）。
         * 例子: <see cref="MyMethod"/>
         */

        /* 
         * 29. ElseClauseSyntax
         * 代表 if 语句中的 else 部分。
         * 例子: else { ... }
         */
        public void IfElse(bool b) { if (b) { } else { } }

        /* 
         * 30. EqualsValueClauseSyntax
         * 代表赋值语句中的等号及后面的值部分。
         * 例子: = 10
         */
        public int X = 10;

        /* 
         * 31. ExplicitInterfaceSpecifierSyntax
         * 代表显式接口成员实现的接口名前缀。
         * 例子: IDisposable. (在 IDisposable.Dispose() 中)
         */
        public class ExplicitImpl : IDisposable { void IDisposable.Dispose() { } }

        /* 
         * 32. ExpressionOrPatternSyntax
         * 表达式或模式的通用基类。
         */

        /* 
         * 33. ExternAliasDirectiveSyntax
         * 代表外部别名指令。
         * 例子: extern alias GridV1;
         */

        /* 
         * 34. FinallyClauseSyntax
         * 代表 try 语句中的 finally 块。
         * 例子: finally { ... }
         */
        public void TryFinally() { try { } finally { } }

        /* 
         * 35. FunctionPointerCallingConventionSyntax
         * 函数指针的调用约定。
         * 例子: managed, cdecl
         */

        /* 
         * 36. FunctionPointerParameterListSyntax
         * 函数指针的参数类型列表。
         * 例子: delegate* <int, void> 中的 <int, void>
         */

        /* 
         * 37. FunctionPointerUnmanagedCallingConventionListSyntax
         * 函数指针的非托管调用约定列表。
         */

        /* 
         * 38. FunctionPointerUnmanagedCallingConventionSyntax
         * 函数指针的单个非托管调用约定。
         */

        /* 
         * 39. InterpolatedStringContentSyntax
         * 插值字符串中的内容（文本或插值部分）。
         */

        /* 
         * 40. InterpolationAlignmentClauseSyntax
         * 代表插值表达式中的对齐部分。
         * 例子: ,10 (在 $"{x,10}" 中)
         */
        public string Aligned = $"{10,10}";

        /* 
         * 41. InterpolationFormatClauseSyntax
         * 代表插值表达式中的格式化部分。
         * 例子: :C (在 $"{price:C}" 中)
         */
        public string Formatted = $"{12.34:C}";

        /* 
         * 42. JoinIntoClauseSyntax
         * LINQ 查询中的 join ... into 子句。
         * 例子: join b in bar on f.Id equals b.FId into grouped
         */

        /* 
         * 43. LineDirectivePositionSyntax
         * #line 指令中的位置信息。
         */

        /* 
         * 44. MemberDeclarationSyntax
         * 类、结构、接口中所有成员声明（字段、方法、属性等）的基类。
         */

        /* 
         * 45. NameEqualsSyntax
         * 代表带名称的赋值或初始化。
         * 例子: Name = (在特性或匿名对象中)
         */

        /* 
         * 46. OrderingSyntax
         * LINQ 查询中的排序方式。
         * 例子: ascending 或 descending
         */

        /* 
         * 47. PositionalPatternClauseSyntax
         * 代表位置模式匹配（解构模式）。
         * 例子: (int x, int y) (在 if (obj is (1, 2)) 中)
         */

        /* 
         * 48. PropertyPatternClauseSyntax
         * 代表属性模式匹配。
         * 例子: { Length: 5 } (在 if (str is { Length: 5 }) 中)
         */
        public bool IsShort(string s) => s is { Length: < 5 };

        /* 
         * 49. QueryBodySyntax
         * 代表 LINQ 查询表达式的主体。
         */

        /* 
         * 50. QueryClauseSyntax
         * LINQ 查询中各个子句（from, where, select 等）的基类。
         */

        /* 
         * 51. QueryContinuationSyntax
         * 代表 LINQ 查询中的 into 延续部分。
         */

        /* 
         * 52. SelectOrGroupClauseSyntax
         * 代表 LINQ 中的 select 或 group 子句。
         */

        /* 
         * 53. StatementSyntax
         * 所有语句（if, for, return 等）的基类。
         */

        /* 
         * 54. StructuredTriviaSyntax
         * 代表具有内部结构的琐碎内容（如预处理器指令、XML 注释）。
         */

        /* 
         * 55. SubpatternSyntax
         * 递归模式中的子模式。
         * 例子: Length: 5
         */

        /* 
         * 56. SwitchExpressionArmSyntax
         * 代表 switch 表达式中的一个分支。
         * 例子: 1 => "one",
         */
        public string Match(int i) => i switch { 1 => "one", _ => "many" };

        /* 
         * 57. SwitchLabelSyntax
         * 代表 switch case 语句中的标签。
         * 例子: case 1: 或 default:
         */

        /* 
         * 58. SwitchSectionSyntax
         * 代表 switch 语句中的一个完整的小节（包含标签和语句）。
         * 例子: case 1: Console.WriteLine(1); break;
         */

        /* 
         * 59. TupleElementSyntax
         * 代表元组中的单个元素。
         * 例子: int count (在 (int count, string name) 中)
         */
        public (int Id, string Name) User = (1, "Admin");

        /* 
         * 60. TypeArgumentListSyntax
         * 代表泛型实例化时的类型参数列表。
         * 例子: <int, string> (在 List<int> 中)
         */
        public List<int> Numbers = new List<int>();

        /* 
         * 61. TypeParameterConstraintClauseSyntax
         * 代表泛型定义中的 where 约束子句。
         * 例子: where T : class
         */
        public class Constrained<T> where T : class { }

        /* 
         * 62. TypeParameterConstraintSyntax
         * 代表泛型定义中的单个约束条件。
         * 例子: class, new(), IDisposable
         */

        /* 
         * 63. TypeParameterListSyntax
         * 代表泛型定义中的类型参数列表。
         * 例子: <T, U>
         */

        /* 
         * 64. TypeParameterSyntax
         * 代表泛型定义中的单个类型参数。
         * 例子: T
         */

        /* 
         * 65. UsingDirectiveSyntax
         * 代表文件顶部的 using 指令。
         * 例子: using System;
         */

        /* 
         * 66. VariableDeclarationSyntax
         * 代表变量声明语句（包含类型和声明符）。
         * 例子: int x = 1, y = 2
         */
        public void Declare() { int x = 1, y = 2; }

        /* 
         * 67. VariableDeclaratorSyntax
         * 代表变量声明中的单个变量及其初始化。
         * 例子: x = 1
         */

        /* 
         * 68. VariableDesignationSyntax
         * 代表变量声明中的变量命名方式（如解构中的元组形式）。
         * 例子: (x, y) (在 var (x, y) = tuple 中)
         */

        /* 
         * 69. WhenClauseSyntax
         * 代表 switch 分支或 catch 块中的 when 条件。
         * 例子: when (x > 0)
         */

        /* 
         * 70. XmlAttributeSyntax
         * 代表 XML 注释中的属性。
         * 例子: name="test" (在 <param name="test"> 中)
         */

        /* 
         * 71. XmlElementEndTagSyntax
         * 代表 XML 注释中的结束标签。
         * 例子: </summary>
         */

        /* 
         * 72. XmlElementStartTagSyntax
         * 代表 XML 注释中的开始标签。
         * 例子: <summary>
         */

        /* 
         * 73. XmlNameSyntax
         * 代表 XML 注释中的标签名或属性名。
         * 例子: summary
         */

        /* 
         * 74. XmlNodeSyntax
         * XML 注释中所有节点（文本、标签等）的基类。
         */

        /* 
         * 75. XmlPrefixSyntax
         * 代表 XML 注释中的前缀。
         * 例子: xsi:
         */
    }
}
