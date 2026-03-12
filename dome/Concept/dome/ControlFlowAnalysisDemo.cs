using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// 控制流分析 (Control Flow Analysis) 演示
    /// 展示 SemanticModel.AnalyzeControlFlow 的作用。
    /// </summary>
    public static class ControlFlowAnalysisDemo
    {
        public static void Run()
        {
            Console.WriteLine("=== 启动 ControlFlowAnalysis Demo ===");

            string sourceCode = @"
using System;
namespace Demo
{
    public class Processor
    {
        public int Calculate(bool condition)
        {
            int x = 0;
            if (condition)
            {
                x = 10;
                return x;
            }
            else
            {
                x = 20;
            }
            return x; // 结束点
        }

        public void UnreachableCode()
        {
            return;
            Console.WriteLine(""Unreachable"");
        }
    }
}";
            var tree = SyntaxFactory.ParseSyntaxTree(sourceCode);
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.Create("ControlFlowDemo")
                .AddReferences(mscorlib)
                .AddSyntaxTrees(tree);
            var model = compilation.GetSemanticModel(tree);

            var root = tree.GetRoot();

            // 1. 分析 if 语句块的控制流
            var ifStatement = root.DescendantNodes().OfType<IfStatementSyntax>().First();
            Console.WriteLine(">>> 分析 If 语句块:");
            AnalyzeBlock(model, ifStatement);

            // 2. 分析不可达代码
            var unreachableMethod = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .First(m => m.Identifier.Text == "UnreachableCode");
            var returnStmt = unreachableMethod.Body.Statements.First();
            var writeStmt = unreachableMethod.Body.Statements.Last();

            Console.WriteLine("\n>>> 分析不可达代码块:");
            // 分析从 return 到方法结束的区域
            var analysis = model.AnalyzeControlFlow(returnStmt, writeStmt);
            
            Console.WriteLine($"起点可达性 (StartPointIsReachable): {analysis.StartPointIsReachable}");
            Console.WriteLine($"终点可达性 (EndPointIsReachable): {analysis.EndPointIsReachable}");
            
            if (!analysis.EndPointIsReachable)
            {
                Console.WriteLine("  -> 检测到不可达代码！");
            }

            Console.WriteLine("=== ControlFlowAnalysis 演示结束 ===");
        }

        private static void AnalyzeBlock(SemanticModel model, SyntaxNode node)
        {
            var analysis = model.AnalyzeControlFlow(node);

            Console.WriteLine($"节点: {node.Kind()}");
            Console.WriteLine($"  - 起点可达: {analysis.StartPointIsReachable}");
            Console.WriteLine($"  - 终点可达: {analysis.EndPointIsReachable}");
            
            Console.WriteLine("  - 退出点 (ExitPoints):");
            foreach (var exit in analysis.ExitPoints)
            {
                Console.WriteLine($"    * {exit.Kind()} at line {exit.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
            }

            Console.WriteLine("  - 返回语句 (ReturnStatements):");
            foreach (var ret in analysis.ReturnStatements)
            {
                Console.WriteLine($"    * {ret} at line {ret.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
            }
        }
    }
}
