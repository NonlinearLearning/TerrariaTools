using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuikGraph;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// 语句级数据流依赖分析 Demo
    /// 基于 DependencyAnalysisGuide 实现，演示如何提取并保存语句间的依赖信息。
    /// </summary>
    public static class StatementDataFlowDemo
    {
        public static void Run()
        {
            Console.WriteLine("=== 启动语句级数据流依赖分析 Demo ===");

            // 1. 示例代码：包含数据依赖和控制流
            string sourceCode = @"
using System;

namespace AnalysisTarget
{
    public class Calculator
    {
        public int Compute(int input)
        {
            int a = input + 10;      // Line 10: 定义 a (依赖 input)
            int b = 20;              // Line 11: 定义 b

            if (a > 20)              // Line 13: 读取 a
            {
                b = b + 5;           // Line 15: 读取 b, 写入 b
            }

            int c = a + b;           // Line 18: 读取 a, b; 定义 c
            return c;                // Line 19: 读取 c
        }
    }
}";

            // 2. 解析与编译
            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.Create("DataFlowAnalysis")
                .AddReferences(mscorlib)
                .AddSyntaxTrees(tree);
            var model = compilation.GetSemanticModel(tree);

            // 3. 查找目标方法
            var root = tree.GetRoot();
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Compute");

            Console.WriteLine($"分析方法: {method.Identifier.Text}");

            // 4. 预处理语句节点
            var statements = method.Body.DescendantNodes().OfType<StatementSyntax>()
                .Where(s => !(s is BlockSyntax)) // 排除块本身
                .ToList();

            var nodes = statements.Select(s => new StatementNode(s, model)).ToList();

            // 5. 执行分析并构建依赖图
            var graph = BuildStatementDependencyGraph(nodes, model);

            // 6. 输出保存的信息
            PrintAnalysisResult(graph);
        }

        private static AdjacencyGraph<StatementNode, StatementDependencyEdge> BuildStatementDependencyGraph(List<StatementNode> nodes, SemanticModel model)
        {
            var graph = new AdjacencyGraph<StatementNode, StatementDependencyEdge>();

            // 添加所有顶点
            graph.AddVertexRange(nodes);

            // 映射：变量名 -> 定义该变量的最近语句节点
            // 注意：这里简化处理，只保留最近的一个定义点（线性扫描模拟控制流）
            var variableDefinitions = new Dictionary<string, StatementNode>();

            // 模拟线性执行流进行分析
            foreach (var currentNode in nodes)
            {
                // 1. 分析输入依赖 (Read): 当前语句读取了哪些变量？
                // 如果读取了变量 v，且 v 之前被语句 S 定义过，则 Current -> S (依赖关系)
                // 使用节点预先分析好的 ReadVariables (这里存储的是变量名)
                foreach (var readVarName in currentNode.ReadVariables)
                {
                    if (variableDefinitions.TryGetValue(readVarName, out var definerNode))
                    {
                        // 添加依赖边：Current (User) -> Definer (Provider)
                        // 避免自环（虽然某些语句如 i=i+1 可能存在自依赖，但在图示中通常省略或特殊处理）
                        if (definerNode != currentNode)
                        {
                            var edge = new StatementDependencyEdge(currentNode, definerNode, readVarName, StatementDependencyType.Data);
                            graph.AddEdge(edge);
                        }
                    }
                }

                // 2. 分析输出影响 (Write): 当前语句写入了哪些变量？
                // 更新变量的定义点为当前语句
                foreach (var writtenVarName in currentNode.WrittenVariables)
                {
                    variableDefinitions[writtenVarName] = currentNode;
                }
            }

            return graph;
        }

        private static void PrintAnalysisResult(AdjacencyGraph<StatementNode, StatementDependencyEdge> graph)
        {
            Console.WriteLine("\n=== 语句依赖分析结果 ===");
            foreach (var node in graph.Vertices.OrderBy(n => n.LineNumber))
            {
                Console.WriteLine($"[Line {node.LineNumber}] {node.ShortContent}");
                Console.WriteLine($"  - 类型: {node.StatementType}");
                Console.WriteLine($"  - 读取: {string.Join(", ", node.ReadVariables)}");
                Console.WriteLine($"  - 写入: {string.Join(", ", node.WrittenVariables)}");

                var dependencies = graph.OutEdges(node).ToList();
                if (dependencies.Any())
                {
                    Console.WriteLine("  - 依赖于:");
                    foreach (var dep in dependencies)
                    {
                        Console.WriteLine($"    -> [Line {dep.Target.LineNumber}] (变量: {dep.VariableName})");
                    }
                }
                Console.WriteLine();
            }
        }
    }

    // ==========================================
    // 数据结构定义 (建议保存的信息)
    // ==========================================

    /// <summary>
    /// 语句节点信息
    /// 建议保存：
    /// 1. 源码位置 (Location/Span)
    /// 2. 语句内容 (SourceText)
    /// 3. 语句类型 (SyntaxKind)
    /// 4. 读写变量集 (Read/Written Variables)
    /// 5. 所属块深度 (ScopeDepth - 用于分析作用域)
    /// </summary>
    public class StatementNode
    {
        public StatementSyntax Syntax { get; }
        public int LineNumber { get; }
        public string ShortContent { get; }
        public string StatementType { get; }

        // 数据流信息
        public List<string> ReadVariables { get; }
        public List<string> WrittenVariables { get; }

        public StatementNode(StatementSyntax stmt, SemanticModel model)
        {
            Syntax = stmt;
            var span = stmt.GetLocation().GetLineSpan();
            LineNumber = span.StartLinePosition.Line + 1;

            string content = stmt.ToString();
            ShortContent = content.Length > 30 ? content.Substring(0, 30) + "..." : content;

            StatementType = stmt.Kind().ToString();

            // 预先分析读写变量
            var dataFlow = model.AnalyzeDataFlow(stmt);
            ReadVariables = dataFlow.ReadInside.Select(s => s.Name).ToList();
            WrittenVariables = dataFlow.WrittenInside.Select(s => s.Name).ToList();
        }

        public override string ToString() => $"L{LineNumber}: {ShortContent}";
    }

    /// <summary>
    /// 语句依赖类型
    /// </summary>
    public enum StatementDependencyType
    {
        Data,    // 数据依赖 (读取了被修改的变量)
        Control  // 控制依赖 (位于 if/while 块内，受条件控制) - *本示例主要演示数据依赖*
    }

    /// <summary>
    /// 语句依赖边
    /// 建议保存：
    /// 1. 依赖变量名 (VariableName)
    /// 2. 依赖类型 (Data/Control)
    /// 3. 权重 (Weight - 可选，表示依赖强度)
    /// </summary>
    public class StatementDependencyEdge : IEdge<StatementNode>
    {
        public StatementNode Source { get; } // 依赖者 (User)
        public StatementNode Target { get; } // 被依赖者 (Definer)

        public string VariableName { get; }
        public StatementDependencyType Type { get; }

        public StatementDependencyEdge(StatementNode source, StatementNode target, string variableName, StatementDependencyType type)
        {
            Source = source;
            Target = target;
            VariableName = variableName;
            Type = type;
        }
    }
}
