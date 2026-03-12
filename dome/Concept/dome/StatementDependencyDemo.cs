using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// 演示如何使用 Roslyn 的 AnalyzeDataFlow API 来分析语句间的依赖关系。
    /// </summary>
    public class StatementDependencyDemo
    {
        public static void Run()
        {
            RunDemo();
        }

        public static void RunDemo()
        {
            // 1. 准备待分析的源代码片段
            string code = @"
using System;

namespace Example
{
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
}";

            // 2. 将代码解析为语法树并创建编译对象
            SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.Create("Demo")
                                               .AddReferences(mscorlib)
                                               .AddSyntaxTrees(tree);

            // 3. 获取语义模型 (SemanticModel)
            SemanticModel model = compilation.GetSemanticModel(tree);

            // 4. 查找目标方法节点
            var methodNode = tree.GetRoot().DescendantNodes()
                                 .OfType<MethodDeclarationSyntax>()
                                 .FirstOrDefault(m => m.Identifier.Text == "Calculate");

            if (methodNode == null || methodNode.Body == null) return;

            Console.WriteLine($"=== 分析方法: {methodNode.Identifier.Text} 中的语句依赖 ===\n");

            // 5. 遍历方法体内的所有语句
            var statements = methodNode.Body.Statements;

            // 为了演示方便，我们用一个字典来记录哪些变量是由哪个语句定义的
            var variableToDefiningStatement = new Dictionary<ISymbol, StatementSyntax>(SymbolEqualityComparer.Default);

            for (int i = 0; i < statements.Count; i++)
            {
                var currentStmt = statements[i];
                string stmtCode = currentStmt.ToString().Trim();
                Console.WriteLine($"分析语句 {i + 1}: {stmtCode}");

                // --- 核心步骤：调用 AnalyzeDataFlow ---
                DataFlowAnalysis dataFlow = model.AnalyzeDataFlow(currentStmt);

                // A. 分析该语句读取了哪些变量（输入依赖）
                if (dataFlow.ReadInside.Any())
                {
                    Console.WriteLine("  -> 读取变量 (Dependencies):");
                    foreach (var symbol in dataFlow.ReadInside)
                    {
                        string sourceInfo = "";
                        if (variableToDefiningStatement.TryGetValue(symbol, out var sourceStmt))
                        {
                            sourceInfo = $" [来自语句: {sourceStmt.ToString().Trim()}]";
                        }
                        else if (symbol.Kind == SymbolKind.Parameter)
                        {
                            sourceInfo = " [来自函数参数]";
                        }

                        Console.WriteLine($"     - {symbol.Name}{sourceInfo}");
                    }
                }

                // B. 分析该语句写入了哪些变量（输出影响）
                if (dataFlow.WrittenInside.Any())
                {
                    Console.WriteLine("  -> 写入变量 (Outputs):");
                    foreach (var symbol in dataFlow.WrittenInside)
                    {
                        Console.WriteLine($"     - {symbol.Name}");
                        // 记录该变量是由当前语句定义的，供后续语句查询依赖
                        variableToDefiningStatement[symbol] = currentStmt;
                    }
                }

                Console.WriteLine();
            }

            Console.WriteLine("=== 分析结束 ===");
        }
    }
}
