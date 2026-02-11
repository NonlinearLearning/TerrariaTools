using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;

namespace Example
{
    /// <summary>
    /// 演示如何使用 ExpressionProcessor 和 ExpressionSimplifier 进行细粒度的代码重写。
    /// </summary>
    public class ExpressionRewriteExample
    {
        public void Run()
        {
            // 示例源代码：包含一些我们想要移除的部分
            string source = @"
using System;

class Calculator {
    public int Add(int a, int b) {
        Console.WriteLine($""Adding {a} and {b}"");
        return a + b;
    }

    public void Process(bool condition, int value) {
        if (condition && value > 0) {
            Console.WriteLine(""Processing..."");
        }
    }
}";

            // 1. 解析语法树
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            // 2. 模拟标记过程：假设我们要移除所有的 Console.WriteLine 调用
            // 在实际项目中，这通常是通过语义分析找到特定符号的引用
            Func<SyntaxNode, bool> shouldRemove = node =>
                node is InvocationExpressionSyntax invocation &&
                invocation.Expression.ToString().Contains("Console.WriteLine");

            Console.WriteLine("=== 原始代码 ===");
            Console.WriteLine(source);

            // 3. 使用 ExpressionProcessor 进行两阶段重写
            // 第一阶段：标记目标节点并根据结构和语义进行传播
            // 第二阶段：执行重写，自动生成必要的占位符（例如在 if 语句中生成 true/false）
            SyntaxNode? result = ExpressionProcessor.RemoveParts(root, shouldRemove);

            Console.WriteLine("\n=== 重写后的代码 (已移除 Console.WriteLine) ===");
            Console.WriteLine(result?.ToFullString());

            /*
             * 预期输出：
             * Calculator 类中的 Console.WriteLine 调用将被移除。
             * 注意：由于 Console.WriteLine(...) 是一个表达式语句，它会被直接移除而不需要占位符。
             * 如果是在 if (condition && Console.WriteLine(...)) 这种需要值的上下文中，
             * 它会被替换为类型匹配的占位符（如 false）。
             */
        }
    }
}
