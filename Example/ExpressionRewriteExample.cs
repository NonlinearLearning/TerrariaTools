using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;
using TerrariaTools.Services;
using System.Threading.Tasks;

namespace Example
{
    /// <summary>
    /// 演示如何使用 ExpressionProcessor 和 ExpressionSimplifier 进行细粒度的代码重写。
    /// </summary>
    public class ExpressionRewriteExample : ITool
    {
        public string Name => "表达式重写";
        public string Description => "演示细粒度的表达式重写（如移除特定方法调用）。";

        public Task RunAsync(string? path = null)
        {
            Run();
            return Task.CompletedTask;
        }

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

            // ---------------------------------------------------------
            // 场景 2: 复杂表达式中的占位符替换
            // ---------------------------------------------------------
            Console.WriteLine("\n=== 场景 2: 复杂表达式中的占位符替换 ===");
            string complexSource = @"
class Logic {
    bool Check() {
        // Console.WriteLine 返回 void，但如果这里是某个返回 bool 的 LogAndReturn() 方法被移除
        // 工具应该将其替换为 false 或 true
        return (IsValid() && ShouldLog());
    }

    bool IsValid() => true;
    bool ShouldLog() => false; // 假设我们要移除这个方法调用
}";
            Console.WriteLine("原始代码:");
            Console.WriteLine(complexSource);

            SyntaxTree complexTree = CSharpSyntaxTree.ParseText(complexSource);
            var complexRoot = complexTree.GetCompilationUnitRoot();

            // 标记移除 ShouldLog() 方法调用
            Func<SyntaxNode, bool> removeShouldLog = node =>
                node is InvocationExpressionSyntax inv &&
                inv.Expression.ToString() == "ShouldLog";

            var complexResult = ExpressionProcessor.RemoveParts(complexRoot, removeShouldLog);
            Console.WriteLine("\n重写后 (ShouldLog 被移除，自动填充默认值):");
            Console.WriteLine(complexResult?.ToFullString());
            Console.WriteLine("// 注意: && 右侧被移除时，通常会保留左侧或替换为默认值，具体取决于实现策略。");
        }
    }
}
