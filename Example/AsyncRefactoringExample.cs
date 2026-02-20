using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions;

namespace Example
{
    /// <summary>
    /// 演示如何安全地处理异步代码的重构。
    /// 场景：将同步方法调用转换为异步，或者移除不需要的异步等待。
    /// </summary>
    public class AsyncRefactoringExample
    {
        public void Run()
        {
            string sourceCode = @"
using System;
using System.Threading.Tasks;

public class DataService
{
    // 场景 1: 我们想移除对 LogAsync 的等待调用，因为日志记录不应该阻塞
    public async Task ProcessDataAsync()
    {
        Console.WriteLine(""Starting..."");
        await LogAsync(""Processing started""); 
        
        var data = await FetchDataAsync();
        
        // 场景 2: 如果移除 FetchDataAsync，这里的 data 变量也会失效
        // 工具应该能处理这种情况，或者生成占位符
        Console.WriteLine(data);
    }

    private async Task LogAsync(string message) 
    {
        await Task.Delay(100);
        Console.WriteLine(message);
    }

    private async Task<string> FetchDataAsync()
    {
        await Task.Delay(500);
        return ""Result"";
    }
}";

            Console.WriteLine("=== 原始异步代码 ===");
            Console.WriteLine(sourceCode);

            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetRoot();

            // 定义移除规则：移除所有名为 "LogAsync" 的调用
            Func<SyntaxNode, bool> shouldRemove = node =>
            {
                if (node is AwaitExpressionSyntax awaitExpr && 
                    awaitExpr.Expression is InvocationExpressionSyntax inv &&
                    inv.Expression.ToString() == "LogAsync")
                {
                    return true;
                }
                return false;
            };

            // 使用 ExpressionProcessor 进行处理
            // 注意：ExpressionProcessor 会自动处理 await 关键字的上下文
            var newRoot = ExpressionProcessor.RemoveParts(root, shouldRemove);

            Console.WriteLine("\n=== 重构后的代码 (移除了 LogAsync) ===");
            Console.WriteLine(newRoot?.ToFullString());
            
            Console.WriteLine("\n[说明] ExpressionProcessor 能够智能识别 await 表达式。");
            Console.WriteLine("当 await LogAsync(...) 被移除时，整个 await 语句都会被清理，");
            Console.WriteLine("而不会留下悬空的 await 关键字。");
        }
    }
}
