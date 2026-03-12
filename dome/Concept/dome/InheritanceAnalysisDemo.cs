using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuikGraph;
using QuikGraph.Algorithms;
using TerrariaTools.Analysis.Dome;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// 综合演示：类继承关系分析与 QuikGraph 图构建
    /// 合并了原有的 ClassInheritanceAnalysisDemo 和 InheritanceGraphDemo
    /// 现已接入 InheritanceAnalyzer 实现逻辑复用。
    /// </summary>
    public static class InheritanceAnalysisDemo
    {
        public static void Run()
        {
            Console.WriteLine("=== 启动类继承关系综合分析 Demo (分析复用模式) ===");

            // 1. 构建示例代码
            string sourceCode = @"
using System;
using System.Collections;
using System.Collections.Generic;

namespace MyNamespace
{
    public interface IMyInterface { }
    public interface IAdvanced : IMyInterface { }
    public class MyBase { }
    public class MyDerived : MyBase, IAdvanced, IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
    }
    public class AnotherClass : MyDerived { }
}";
            Console.WriteLine("正在解析示例代码...");
            SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(sourceCode);

            // 2. 创建编译对象并获取语义模型
            var compilation = CSharpCompilation.Create("DemoCompilation")
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddSyntaxTrees(tree);
            var model = compilation.GetSemanticModel(tree);

            // 3. 执行直接分析
            Console.WriteLine("\n>>> 阶段 1: 直接语义分析 (复用分析逻辑)");
            AnalyzeInheritanceDirectly(tree, model);

            // 4. 构建并展示关系图
            Console.WriteLine("\n>>> 阶段 2: 关系图构建 (QuikGraph)");
            var graph = BuildGraph(tree, model);
            PrintGraphInfo(graph);
        }

        private static void AnalyzeInheritanceDirectly(SyntaxTree tree, SemanticModel model)
        {
            var typeDeclarations = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>();
            Console.WriteLine($"找到 {typeDeclarations.Count()} 个类型声明...\n");

            foreach (var typeDecl in typeDeclarations)
            {
                var symbol = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                if (symbol == null) continue;

                Console.WriteLine($"─────────────── 类型: {symbol.Name} ({symbol.TypeKind}) ───────────────");
                Console.WriteLine($"完整名称: {symbol.ToDisplayString()}");

                // 复用 InheritanceAnalyzer 获取基类
                var baseTypes = InheritanceAnalyzer.GetBaseTypes(symbol).ToList();
                if (baseTypes.Any())
                {
                    Console.WriteLine("继承链:");
                    foreach (var bt in baseTypes)
                        Console.WriteLine($"  - {bt.ToDisplayString()}");
                }

                if (symbol.AllInterfaces.Any())
                {
                    Console.WriteLine("实现接口:");
                    foreach (var i in symbol.AllInterfaces)
                        Console.WriteLine($"  - {i.ToDisplayString()}");
                }
            }
        }

        private static AdjacencyGraph<string, Edge<string>> BuildGraph(SyntaxTree tree, SemanticModel model)
        {
            var graph = new AdjacencyGraph<string, Edge<string>>();
            var typeDeclarations = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>();

            foreach (var typeDecl in typeDeclarations)
            {
                var symbol = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                if (symbol == null) continue;

                string typeName = symbol.Name;
                graph.AddVertex(typeName);

                // 添加基类边
                if (symbol.BaseType != null && symbol.BaseType.SpecialType != SpecialType.System_Object)
                {
                    string baseName = symbol.BaseType.Name;
                    graph.AddVertex(baseName);
                    graph.AddEdge(new Edge<string>(typeName, baseName));
                }

                // 添加接口边
                foreach (var @interface in symbol.Interfaces)
                {
                    string interfaceName = @interface.Name;
                    graph.AddVertex(interfaceName);
                    graph.AddEdge(new Edge<string>(typeName, interfaceName));
                }
            }

            return graph;
        }

        private static void PrintGraphInfo(AdjacencyGraph<string, Edge<string>> graph)
        {
            Console.WriteLine($"图构建完成: {graph.VertexCount} 个顶点, {graph.EdgeCount} 条边");
            foreach (var vertex in graph.Vertices)
            {
                var outEdges = graph.OutEdges(vertex);
                foreach (var edge in outEdges)
                    Console.WriteLine($"  [关系] {edge.Source} -> {edge.Target}");
            }
        }
    }
}
