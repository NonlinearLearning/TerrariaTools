using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuikGraph;
using QuikGraph.Algorithms;
using QuikGraph.Algorithms.ConnectedComponents;

namespace TerrariaTools.Dome
{
    /// <summary>
    /// 函数调用依赖分析 Demo
    /// 功能：
    /// 1. 识别代码中的函数调用关系
    /// 2. 构建函数调用图
    /// 3. 检测循环调用 (直接递归和间接递归)
    /// </summary>
    public static class MethodCallAnalysisDemo
    {
        public static void Run()
        {
            Console.WriteLine("=== 启动函数调用依赖与循环检测 Demo ===");

            // 1. 构建包含循环调用的示例代码
            string sourceCode = @"
using System;

namespace CycleCheck
{
    public class Processor
    {
        public void EntryPoint()
        {
            Console.WriteLine(""Start"");
            MethodA();
        }

        public void MethodA()
        {
            Console.WriteLine(""In A"");
            MethodB(); // A -> B
        }

        public void MethodB()
        {
            Console.WriteLine(""In B"");
            MethodC(); // B -> C
        }

        public void MethodC()
        {
            Console.WriteLine(""In C"");
            // 构成间接循环: C -> A
            MethodA();
        }

        public void RecursiveMethod()
        {
            // 直接递归
            RecursiveMethod();
        }

        public void SafeMethod()
        {
            Console.WriteLine(""No Cycle Here"");
        }
    }
}";
            Console.WriteLine("正在解析示例代码...");
            SyntaxTree tree = SyntaxFactory.ParseSyntaxTree(sourceCode);

            // 2. 获取语义模型
            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var systemCore = MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location);
            var compilation = CSharpCompilation.Create("MethodCallAnalysis")
                .AddReferences(mscorlib, systemCore)
                .AddSyntaxTrees(tree);
            var model = compilation.GetSemanticModel(tree);

            // 3. 构建调用图
            var graph = BuildCallGraph(tree, model);

            // 4. 输出图的基本信息
            PrintGraphInfo(graph);

            // 5. 检测循环调用
            DetectCycles(graph);
        }

        /// <summary>
        /// 构建调用图
        /// </summary>
        private static AdjacencyGraph<MethodVertex, CallEdge> BuildCallGraph(SyntaxTree tree, SemanticModel model)
        {
            var graph = new AdjacencyGraph<MethodVertex, CallEdge>();

            // 缓存：Symbol -> Vertex
            var symbolMap = new Dictionary<ISymbol, MethodVertex>(SymbolEqualityComparer.Default);

            MethodVertex GetOrCreateVertex(IMethodSymbol symbol)
            {
                if (!symbolMap.TryGetValue(symbol, out var vertex))
                {
                    // 仅当符号非空时创建
                    vertex = new MethodVertex(symbol);
                    symbolMap[symbol] = vertex;
                    graph.AddVertex(vertex);
                }
                return vertex;
            }

            // 查找所有的方法声明
            var methodDecls = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDecls)
            {
                var callerSymbol = model.GetDeclaredSymbol(methodDecl);
                if (callerSymbol == null) continue;

                var callerVertex = GetOrCreateVertex(callerSymbol);

                // 查找该方法内部的所有调用
                var invocations = methodDecl.DescendantNodes().OfType<InvocationExpressionSyntax>();

                foreach (var invocation in invocations)
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    var calleeSymbol = symbolInfo.Symbol as IMethodSymbol;

                    // 如果找不到具体的被调用符号（可能是解析失败或动态调用），则跳过
                    if (calleeSymbol == null) continue;

                    var calleeVertex = GetOrCreateVertex(calleeSymbol);

                    // 确定调用类型
                    CallType callType = CallType.Direct;
                    if (calleeSymbol.IsVirtual || calleeSymbol.IsAbstract || calleeSymbol.IsOverride)
                    {
                        callType = CallType.Virtual;
                    }
                    if (calleeSymbol.MethodKind == MethodKind.DelegateInvoke)
                    {
                        callType = CallType.Delegate;
                    }

                    // 获取调用行号
                    int callLine = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                    // 添加边：Caller -> Callee
                    var edge = new CallEdge(callerVertex, calleeVertex, callLine, callType);

                    // 避免添加重复的边
                    bool edgeExists = graph.OutEdges(callerVertex).Any(e => e.Target.Equals(calleeVertex) && e.CallLine == callLine);
                    if (!edgeExists)
                    {
                        graph.AddEdge(edge);
                    }
                }
            }

            return graph;
        }

        /// <summary>
        /// 检测图中的循环 (强连通分量算法)
        /// </summary>
        private static void DetectCycles(AdjacencyGraph<MethodVertex, CallEdge> graph)
        {
            Console.WriteLine("\n=== 开始检测循环调用 ===");

            // 使用 Tarjan 强连通分量算法
            // 一个强连通分量如果包含多个顶点，或者包含一个顶点且该顶点有自环，则表示存在循环
            var algorithm = new StronglyConnectedComponentsAlgorithm<MethodVertex, CallEdge>(graph);
            algorithm.Compute();

            var components = algorithm.Components;
            // Components map: Vertex -> ComponentID (int)

            // 反转字典：ComponentID -> List<Vertex>
            var componentGroups = new Dictionary<int, List<MethodVertex>>();

            foreach (var kvp in components)
            {
                if (!componentGroups.ContainsKey(kvp.Value))
                {
                    componentGroups[kvp.Value] = new List<MethodVertex>();
                }
                componentGroups[kvp.Value].Add(kvp.Key);
            }

            bool foundCycle = false;

            foreach (var group in componentGroups.Values)
            {
                // 情况1: 多个节点组成的环 (A->B->A)
                if (group.Count > 1)
                {
                    foundCycle = true;
                    Console.WriteLine($"[发现间接循环] 涉及 {group.Count} 个方法:");
                    foreach (var node in group)
                    {
                        Console.WriteLine($"  - {node.Name} (在 {node.ContainingType})");
                    }
                }
                // 情况2: 单个节点的自环 (A->A)
                else if (group.Count == 1)
                {
                    var node = group[0];
                    // 检查是否存在指向自己的边
                    // QuikGraph 的 OutEdges 包含所有出边
                    if (graph.OutEdges(node).Any(e => e.Target.Equals(node)))
                    {
                        foundCycle = true;
                        Console.WriteLine($"[发现直接递归] 方法 {node.Name} (在 {node.ContainingType}) 直接调用了自己");
                    }
                }
            }

            if (!foundCycle)
            {
                Console.WriteLine("未检测到任何循环调用。");
            }
            Console.WriteLine("========================");
        }

        // ==========================================
        // 辅助方法
        // ==========================================

        private static void PrintGraphInfo(AdjacencyGraph<MethodVertex, CallEdge> graph)
        {
            Console.WriteLine($"\n[图构建完成] {graph.VertexCount} 个方法节点, {graph.EdgeCount} 条调用边");

            // 过滤系统方法，只显示源码中的方法
            var sourceMethods = graph.Vertices.Where(v => !v.IsSystemMethod).ToList();
            Console.WriteLine($"源码方法数: {sourceMethods.Count}");

            foreach (var vertex in sourceMethods)
            {
                Console.WriteLine($"方法: {vertex.Name}");
                Console.WriteLine($"  - 签名: {vertex.Id}");
                Console.WriteLine($"  - 位置: {vertex.Location}");
                Console.WriteLine($"  - 访问性: {vertex.Accessibility}");
                Console.WriteLine($"  - 返回值: {vertex.ReturnType}");
                Console.WriteLine($"  - 参数: {vertex.Parameters}");

                foreach (var edge in graph.OutEdges(vertex))
                {
                    Console.WriteLine($"  -> 调用 -> {edge.Target.Name} (行: {edge.CallLine}, 类型: {edge.CallType})");
                }
                Console.WriteLine();
            }
        }
    }

    // ==========================================
    // 数据结构定义 (建议保存的信息)
    // ==========================================

    /// <summary>
    /// 方法节点信息
    /// 包含：唯一标识、所属类型、源码位置、修饰符、访问性、返回类型、参数列表
    /// </summary>
    public class MethodVertex
    {
        public string Id { get; }            // 唯一签名
        public string Name { get; }          // 简短名称
        public string ContainingType { get; }// 所属类名
        public bool IsStatic { get; }        // 是否静态
        public bool IsSystemMethod { get; }  // 是否系统库方法(非源码)
        public string Location { get; }      // 源码位置

        // 新增建议字段
        public string Accessibility { get; } // 访问修饰符 (Public/Private/...)
        public string ReturnType { get; }    // 返回值类型
        public string Parameters { get; }    // 参数列表 (格式化字符串)

        public MethodVertex(IMethodSymbol symbol)
        {
            // 使用全名作为唯一ID，防止重载混淆
            Id = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            Name = symbol.Name;
            ContainingType = symbol.ContainingType?.Name ?? "Global";
            IsStatic = symbol.IsStatic;

            // 判断是否为源码定义 (Locations 列表不为空且在源码树中)
            IsSystemMethod = !symbol.Locations.Any(l => l.IsInSource);

            var span = symbol.Locations.FirstOrDefault()?.GetLineSpan();
            Location = (span != null && span.Value.Path != "")
                ? $"{span.Value.StartLinePosition.Line + 1}"
                : "External";

            // 填充新增字段
            Accessibility = symbol.DeclaredAccessibility.ToString();
            ReturnType = symbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            Parameters = string.Join(", ", symbol.Parameters.Select(p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"));
        }

        // 重写 Equals 和 GetHashCode 确保图节点唯一性
        public override bool Equals(object? obj)
        {
            return obj is MethodVertex other && Id == other.Id;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public override string ToString() => $"{ContainingType}.{Name}";
    }

    /// <summary>
    /// 调用类型枚举
    /// </summary>
    public enum CallType
    {
        Direct,   // 直接调用
        Virtual,  // 虚方法调用
        Delegate, // 委托调用
        Unknown   // 未知
    }

    /// <summary>
    /// 调用边信息
    /// 包含：调用位置、调用类型
    /// </summary>
    public class CallEdge : IEdge<MethodVertex>
    {
        public MethodVertex Source { get; }
        public MethodVertex Target { get; }

        // 新增建议字段
        public int CallLine { get; }         // 调用发生的行号
        public CallType CallType { get; }    // 调用类型

        public CallEdge(MethodVertex source, MethodVertex target, int callLine, CallType callType)
        {
            Source = source;
            Target = target;
            CallLine = callLine;
            CallType = callType;
        }
    }
}