using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.Services;
using System.Threading.Tasks;

namespace Example
{
    /// <summary>
    /// 演示如何创建自定义的 CSharpSyntaxRewriter 来执行特定的代码转换。
    /// 这个例子展示了一个简单的 "敏感信息脱敏" 重写器。
    /// </summary>
    public class CustomRewriterExample : ITool
    {
        public string Name => "自定义重写器";
        public string Description => "演示自定义 CSharpSyntaxRewriter（如敏感信息脱敏）。";

        public Task RunAsync(string? path = null)
        {
            Run();
            return Task.CompletedTask;
        }

        public void Run()
        {
            string sourceCode = @"
using System;

class UserData {
    private string password = ""Secret123"";
    private string apiKey = ""AK-47-8888-9999"";
    
    public void Display() {
        Console.WriteLine(""User password is: "" + password);
        string token = ""ghp_abcdef123456"";
    }
}";

            Console.WriteLine("=== 原始代码 ===");
            Console.WriteLine(sourceCode);

            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
            SyntaxNode root = tree.GetRoot();

            // 实例化并执行自定义重写器
            var rewriter = new StringRedactor();
            SyntaxNode newRoot = rewriter.Visit(root);

            Console.WriteLine("\n=== 脱敏后的代码 ===");
            Console.WriteLine(newRoot.ToFullString());
        }

        /// <summary>
        /// 自定义重写器：将所有字符串字面量替换为 "[REDACTED]"
        /// </summary>
        private class StringRedactor : CSharpSyntaxRewriter
        {
            public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                // 检查是否是字符串字面量
                if (node.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    string value = node.Token.ValueText;
                    
                    // 简单的过滤逻辑：如果字符串看起来像敏感信息（长度>5），则替换
                    // 在实际场景中，这里可以使用正则匹配 API Key 等
                    if (value.Length > 5 && !value.Contains("User password is")) 
                    {
                        // 创建一个新的字符串字面量节点
                        return SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal("[REDACTED]")
                        ).WithTriviaFrom(node); // 保留原始的空白和注释
                    }
                }

                return base.VisitLiteralExpression(node);
            }
        }
    }
}
