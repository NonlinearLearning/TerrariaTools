using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// Roslyn 三级依赖分析指南
    /// 涵盖：类间依赖、函数间依赖、语句间依赖
    /// </summary>
    public class DependencyAnalysisGuide
    {
        /*
         * 1. 类间依赖分析 (Class-to-Class Dependency)
         * --------------------------------------------------
         * 描述：分析一个类是否引用了另一个类。
         * 维度：字段类型、属性类型、基类/接口、构造函数中的实例化。
         * Roslyn 实现：
         *   - 使用 SemanticModel.GetDeclaredSymbol 获取类的 ITypeSymbol。
         *   - 检查类的成员（Fields, Properties）的 TypeSymbol。
         */
        public class ClassLevelDemo
        {
            // ClassLevelDemo 依赖于 ClassB (通过字段)
            private ClassB _b = new ClassB();

            // ClassLevelDemo 依赖于 ClassC (通过属性)
            public ClassC C { get; set; }
        }

        /*
         * 2. 函数间依赖分析 (Method-to-Method Dependency / Call Graph)
         * --------------------------------------------------
         * 描述：分析一个方法内部调用了哪些其他方法。
         * Roslyn 实现：
         *   - 遍历方法体内的 InvocationExpressionSyntax (方法调用) 或 ObjectCreationExpressionSyntax (构造调用)。
         *   - 使用 SemanticModel.GetSymbolInfo(node).Symbol 获取被调用方的 IMethodSymbol。
         */
        public class FunctionLevelDemo
        {
            public void MethodA()
            {
                var b = new ClassB();
                int result = b.add(); // MethodA 依赖于 ClassB.add()
                StaticHelper.Log(result); // MethodA 依赖于 StaticHelper.Log()
            }
        }

        /*
         * 3. 语句间依赖分析 (Statement-to-Statement Dependency / Data Flow)
         * --------------------------------------------------
         * 描述：分析一条语句是否依赖于之前语句定义的变量（数据流分析）。
         * Roslyn 实现：
         *   - 使用 SemanticModel.AnalyzeDataFlow(statement) 接口。
         *   - 检查 DataFlowAnalysis.DataFlowsIn (流入的数据) 和 DataFlowsOut (流出的数据)。
         */
        public class StatementLevelDemo
        {
            public int Calculate(int input)
            {
                int a = input + 10;     // 语句 1
                int b = 20;             // 语句 2
                int c = a + b;          // 语句 3：依赖于 语句 1 (a) 和 语句 2 (b)
                return c;               // 语句 4：依赖于 语句 3 (c)
            }
        }

        // 辅助类用于示例
        public class ClassB { public int add() => 10; }
        public class ClassC { }
        public static class StaticHelper { public static void Log(int msg) => Console.WriteLine(msg); }
    }

    /// <summary>
    /// 演示如何使用 Roslyn API 进行分析的代码逻辑
    /// </summary>
    public class RoslynDependencyAnalyzer
    {
        public void Analyze(SemanticModel model, MethodDeclarationSyntax method)
        {
            // --- 示例：分析语句间依赖 (Data Flow) ---
            // 假设我们要分析方法体内的最后一条 return 语句
            var lastStatement = method.Body.Statements.Last();
            var dataFlow = model.AnalyzeDataFlow(lastStatement);

            // 哪些局部变量被这条语句读取了？（这就是它的依赖变量）
            foreach (var symbol in dataFlow.ReadInside)
            {
                Console.WriteLine($"语句依赖于变量: {symbol.Name}");
            }

            // --- 示例：分析函数间依赖 (Call Graph) ---
            var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                var methodSymbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (methodSymbol != null)
                {
                    Console.WriteLine($"函数依赖于调用: {methodSymbol.ContainingType.Name}.{methodSymbol.Name}");
                }
            }
        }
    }
}
