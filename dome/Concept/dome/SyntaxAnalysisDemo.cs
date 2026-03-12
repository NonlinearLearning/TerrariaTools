using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// 语法分析示例：查找函数节点并列出其所有 Tokens
    /// </summary>
    public class SyntaxAnalysisDemo
    {
        public static void Run()
        {
            RunDemo();
        }

        public static void RunDemo()
        {
            // 待分析的源代码
            string code = @"
using System;

namespace Example
{
    public class TestClass
    {
        public void SayHello(string name)
        {
            int count = name.Length;
            if (count > 0)
            {
                for (int i = 0; i < 1; i++)
                {
                    Console.WriteLine(""Hello, "" + name + ""! Length: "" + count);
                }
            }
            else
            {
                Console.WriteLine(""Hello, stranger!"");
            }
        }
    }
}";

            // 1. 将代码文本解析为语法树 (SyntaxTree)
            SyntaxTree tree = CSharpSyntaxTree.ParseText(code);

            // 2. 获取语法树的根节点 (CompilationUnitSyntax)
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

            // 3. 查找函数节点 (MethodDeclarationSyntax)
            // 我们查找名为 'SayHello' 的方法
            var methodNode = root.DescendantNodes()
                                 .OfType<MethodDeclarationSyntax>()
                                 .FirstOrDefault(m => m.Identifier.Text == "SayHello");

            if (methodNode != null)
            {
                Console.WriteLine($"--- 找到目标函数节点 ---");
                Console.WriteLine($"函数名称: {methodNode.Identifier.Text}");
                Console.WriteLine($"返回类型: {methodNode.ReturnType}");
                Console.WriteLine();

                // 4. 查找该函数节点下的所有语句 (StatementSyntax)，过滤掉 Block 类型的语句
                var statements = methodNode.DescendantNodes()
                                            .OfType<StatementSyntax>()
                                            .Where(s => !s.IsKind(SyntaxKind.Block));

                Console.WriteLine($"--- 函数包含的 Statements 列表 ---");
                foreach (var stmt in statements)
                {
                    // 将语句内容压缩为单行（替换换行符和多余空格）
                    string compressedCode = System.Text.RegularExpressions.Regex.Replace(stmt.ToString(), @"\s+", " ").Trim();

                    // 输出语句的类型和压缩后的代码片段
                    Console.WriteLine($"类型: [{stmt.Kind().ToString().PadRight(25)}] | 代码: {compressedCode}");
                }
                Console.WriteLine();

                // 5. 查找该函数节点下的所有 Tokens
                // DescendantTokens() 会返回该节点包含的所有最小语法单位
                var tokens = methodNode.DescendantTokens();

                Console.WriteLine($"--- 函数包含的 Tokens 列表 ---");
                foreach (var token in tokens)
                {
                    // 输出 Token 的种类 (Kind) 和 文本内容 (Text)
                    // 注意：Token 不包含空格和换行，但可以通过 LeadingTrivia 获取
                    Console.WriteLine($"类型: [{token.Kind().ToString().PadRight(25)}] | 文本: \"{token.Text}\"");
                }
            }
            else
            {
                Console.WriteLine("未找到指定的函数节点。");
            }
        }
    }
}
