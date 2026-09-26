using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes
{
    internal sealed class MethodDecorationPass : INLCPGPass
    {
        internal static MethodDecorationPass Instance { get; } = new();

        private MethodDecorationPass()
        {
        }

        public string Name => nameof(MethodDecorationPass);

        // 触发方法装饰 pass，补方法节点及其边界抽象。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            // Pass 入口只负责分发，方法节点与边界节点的创建都集中在 builder 中处理。
            builder.RunMethodDecorationPass(context);
        }
    }
}

namespace NLCPG.Builder
{
    /// <summary>
    /// <c>MethodDecorationPass</c> 的专用遍历/查询遥测。
    /// <para>
    /// 三个声明计数之和是<b>单遍遍历的实际拜访量</b>；<see cref="DeclaredSymbolQueryCount"/>
    /// 刻意只统计"已建 syntax 节点"的声明（未落图的声明在旧实现里也不会发起查询），
    /// 故它通常小于三计数之和，差额即被跳过的声明数。
    /// </para>
    /// </summary>
    /// <param name="TraversalElapsedMilliseconds">
    /// 仅单遍遍历本身的耗时，不含建节点/边。与 <paramref name="DecorationElapsedMilliseconds"/>
    /// 分开，是为了让"三遍遍历合并为一遍"这一改动可被单独观测，而不是混进装饰总时长。
    /// </param>
    internal readonly record struct MethodDecorationTelemetry(
      int MethodDeclarationCount,
      int LocalFunctionCount,
      int AccessorDeclarationCount,
      int DeclaredSymbolQueryCount,
      int DeclaredSymbolResolvedCount,
      int AbstractionCount,
      long TraversalElapsedMilliseconds,
      long DecorationElapsedMilliseconds)
    {
        /// <summary>单遍遍历拜访的方法类声明总数。</summary>
        internal int MethodLikeDeclarationCount =>
          MethodDeclarationCount + LocalFunctionCount + AccessorDeclarationCount;

        /// <summary>
        /// 已建 syntax 节点但未解析出方法符号的声明数——即真正被跳过的工作量。
        /// </summary>
        internal int UnresolvedDeclarationCount =>
          DeclaredSymbolQueryCount - DeclaredSymbolResolvedCount;
    }

    public sealed partial class NLCPGBuilder
    {
        /// <summary>
        /// <c>MethodDecorationPass</c> 最近一次运行的**专用遍历/查询遥测**。
        /// <para>
        /// 与 <c>SyntaxPass</c> 的 <c>DeclaredSymbolQueryCount</c> 是<b>两个独立的观测点</b>：
        /// 后者统计语法落图阶段的声明符号查询，本字段只统计方法装饰阶段。
        /// 该阶段在 <c>RequiresMethodModel</c> 为假时根本不执行，此时保持全零默认值——
        /// 全零因此是"未运行"，而不是"运行了但什么都没找到"。
        /// </para>
        /// </summary>
        internal MethodDecorationTelemetry LastMethodDecorationTelemetry { get; private set; }

        internal void RunMethodDecorationPass(NLCPGBuildContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            // 单遍遍历 + 三个桶：旧实现对整个语法树做了三遍 DescendantNodes（方法、局部函数、
            // 访问器各一遍），这里合并为一次遍历后按类型分桶。
            //
            // ⚠️ 桶序（全部方法声明 → 全部局部函数 → 全部访问器）是**既有可观测不变量**：
            // 它决定 AddMethodAbstractions 建节点/边的先后，进而决定 NodeId 的分配序。
            // 因此绝不能退化成"按文档序交错"的一次性处理——那会在方法内嵌套局部函数或
            // 属性访问器时改变 NodeId，破坏既有的节点/边序契约。
            var methodDeclarations = new List<SyntaxNode>();
            var localFunctionDeclarations = new List<SyntaxNode>();
            var accessorDeclarations = new List<SyntaxNode>();
            foreach (var syntax in context.Root.DescendantNodes())
            {
                switch (syntax)
                {
                    case BaseMethodDeclarationSyntax:
                        methodDeclarations.Add(syntax);
                        break;
                    case LocalFunctionStatementSyntax:
                        localFunctionDeclarations.Add(syntax);
                        break;
                    case AccessorDeclarationSyntax:
                        accessorDeclarations.Add(syntax);
                        break;
                }
            }

            var traversalElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            var orderedDeclarations = new List<SyntaxNode>(
              methodDeclarations.Count + localFunctionDeclarations.Count + accessorDeclarations.Count);
            orderedDeclarations.AddRange(methodDeclarations);
            orderedDeclarations.AddRange(localFunctionDeclarations);
            orderedDeclarations.AddRange(accessorDeclarations);

            var declaredSymbolQueryCount = 0;
            var declaredSymbolResolvedCount = 0;
            var abstractionCount = 0;
            stopwatch.Restart();
            foreach (var syntax in orderedDeclarations)
            {
                if (!_syntaxNodes.TryGetValue(syntax, out var syntaxNode))
                {
                    // 只有已建 syntax 节点的方法类声明才值得发起声明符号查询。
                    continue;
                }

                declaredSymbolQueryCount++;
                if (context.SemanticModel.GetDeclaredSymbol(syntax) is not IMethodSymbol methodSymbol)
                {
                    continue;
                }

                declaredSymbolResolvedCount++;
                abstractionCount++;
                AddMethodAbstractions(syntaxNode, methodSymbol, context.Graph);
            }

            LastMethodDecorationTelemetry = new MethodDecorationTelemetry(
              MethodDeclarationCount: methodDeclarations.Count,
              LocalFunctionCount: localFunctionDeclarations.Count,
              AccessorDeclarationCount: accessorDeclarations.Count,
              DeclaredSymbolQueryCount: declaredSymbolQueryCount,
              DeclaredSymbolResolvedCount: declaredSymbolResolvedCount,
              AbstractionCount: abstractionCount,
              TraversalElapsedMilliseconds: traversalElapsedMilliseconds,
              DecorationElapsedMilliseconds: stopwatch.ElapsedMilliseconds);
        }

        private void AddMethodAbstractions(NLCPGNode syntaxNode, IMethodSymbol methodSymbol, NLCPGGraph graph)
        {
            var methodNode = GetOrCreateMethodNode(methodSymbol, graph);
            var methodSymbolNode = GetOrCreateSymbolNode(methodSymbol, graph);
            graph.AddEdge(syntaxNode, methodNode, NLCPGEdgeKind.SyntaxChild);
            graph.AddEdge(methodNode, methodSymbolNode, NLCPGEdgeKind.DeclaresSymbol);

            if (methodSymbol.ReturnType is not null)
            {
                // 返回类型存在时补 ReturnsType 和 MethodReturn 抽象，后续调用摘要可直接挂在这里。
                var returnTypeNode = GetOrCreateSymbolNode(methodSymbol.ReturnType, graph);
                graph.AddEdge(methodNode, returnTypeNode, NLCPGEdgeKind.ReturnsType);
                AddEvalTypeEdge(methodNode, methodSymbol.ReturnType, graph);

                var methodReturnNode = GetOrCreateMethodReturnNode(methodSymbol, graph);
                graph.AddEdge(methodNode, methodReturnNode, NLCPGEdgeKind.ParameterLink);
                graph.AddEdge(methodReturnNode, returnTypeNode, NLCPGEdgeKind.EvalType);
            }

            var entryNode = GetOrCreateMethodEntryNode(methodSymbol, graph);
            var exitNode = GetOrCreateMethodExitNode(methodSymbol, graph);
            graph.AddEdge(methodNode, entryNode, NLCPGEdgeKind.SyntaxChild);
            graph.AddEdge(methodNode, exitNode, NLCPGEdgeKind.SyntaxChild);

            foreach (var parameter in methodSymbol.Parameters)
            {
                // 参数节点统一从 Method 出发连接，保持参数顺序和方法边界归属可追踪。
                var parameterNode = GetOrCreateMethodParameterNode(methodSymbol, parameter, graph);
                graph.AddEdge(methodNode, parameterNode, NLCPGEdgeKind.ParameterLink);
            }
        }

        private NLCPGNode GetOrCreateMethodNode(IMethodSymbol methodSymbol, NLCPGGraph graph)
        {
            methodSymbol = CanonicalMethodSymbol(methodSymbol);
            var key = $"method:{SymbolId(methodSymbol)}";
            var methodSymbolKey = SymbolId(methodSymbol);
            lock (_cacheGate)
            {
                if (_methodNodes.TryGetValue(graph, key, out var existing))
                {
                    return existing;
                }

                var methodNode = graph.AddNode(new NLCPGNodeDraft(
                  Kind: NLCPGNodeKind.Method,
                  Name: ComposeMethodName(methodSymbol),
                  FullName: ComposeMethodFullName(methodSymbol),
                  Signature: ComposeMethodSignature(methodSymbol),
                  DispatchKind: ComposeMethodDispatchKind(methodSymbol),
                  TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
                  FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
                  SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
                  SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
                _symbolKeysByNode[methodNode] = methodSymbolKey;
                _methodNodes.Set(graph, key, methodNode);
                return methodNode;
            }
        }

        private NLCPGNode GetOrCreateMethodParameterNode(IMethodSymbol methodSymbol, IParameterSymbol parameterSymbol, NLCPGGraph graph)
        {
            methodSymbol = CanonicalMethodSymbol(methodSymbol);
            var key = $"methodparam:{SymbolId(methodSymbol)}:{parameterSymbol.Ordinal}";
            var methodSymbolKey = SymbolId(methodSymbol);
            lock (_cacheGate)
            {
                if (_methodParameterNodes.TryGetValue(graph, key, out var existing))
                {
                    return existing;
                }

                var parameterNode = graph.AddNode(new NLCPGNodeDraft(
                  Kind: NLCPGNodeKind.MethodParameter,
                  Name: parameterSymbol.Name,
                  FullName: $"{ComposeMethodFullName(methodSymbol)}#{parameterSymbol.Ordinal}:{parameterSymbol.Name}",
                  Signature: ComposeTypeFullName(parameterSymbol.Type),
                  TypeFullName: ComposeTypeFullName(parameterSymbol.Type),
                  FilePath: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
                  SpanStart: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
                  SpanEnd: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
                _methodOwnerSymbolKeysByBoundaryNode[parameterNode] = methodSymbolKey;
                _methodParameterOrdinalsByNode[parameterNode] = parameterSymbol.Ordinal;
                _methodParameterNodes.Set(graph, key, parameterNode);

                var parameterSymbolNode = GetOrCreateSymbolNode(parameterSymbol, graph);
                graph.AddEdge(parameterNode, parameterSymbolNode, NLCPGEdgeKind.Ref);
                AddEvalTypeEdge(parameterNode, parameterSymbol.Type, graph);
                return parameterNode;
            }
        }

        private NLCPGNode GetOrCreateMethodReturnNode(IMethodSymbol methodSymbol, NLCPGGraph graph)
        {
            methodSymbol = CanonicalMethodSymbol(methodSymbol);
            var key = $"methodreturn:{SymbolId(methodSymbol)}";
            var methodSymbolKey = SymbolId(methodSymbol);
            lock (_cacheGate)
            {
                if (_methodReturnNodes.TryGetValue(graph, key, out var existing))
                {
                    return existing;
                }

                var returnNode = graph.AddNode(new NLCPGNodeDraft(
                  Kind: NLCPGNodeKind.MethodReturn,
                  Name: $"{ComposeMethodName(methodSymbol)}:return",
                  FullName: $"{ComposeMethodFullName(methodSymbol)}:return",
                  Signature: ComposeTypeFullName(methodSymbol.ReturnType),
                  TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
                  FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
                  SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
                  SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
                _methodOwnerSymbolKeysByBoundaryNode[returnNode] = methodSymbolKey;
                _methodReturnNodes.Set(graph, key, returnNode);
                return returnNode;
            }
        }

        private NLCPGNode GetOrCreateMethodEntryNode(IMethodSymbol methodSymbol, NLCPGGraph graph)
        {
            methodSymbol = CanonicalMethodSymbol(methodSymbol);
            var key = $"methodentry:{SymbolId(methodSymbol)}";
            lock (_cacheGate)
            {
                if (_methodEntryNodes.TryGetValue(graph, key, out var existing))
                {
                    return existing;
                }

                var entryNode = graph.AddNode(new NLCPGNodeDraft(
                  Kind: NLCPGNodeKind.MethodEntry,
                  Name: $"{ComposeMethodName(methodSymbol)}:entry",
                  FullName: $"{ComposeMethodFullName(methodSymbol)}:entry",
                  Signature: ComposeMethodSignature(methodSymbol),
                  TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
                  FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
                  SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
                  SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start));
                _methodEntryNodes.Set(graph, key, entryNode);
                return entryNode;
            }
        }

        private NLCPGNode GetOrCreateMethodExitNode(IMethodSymbol methodSymbol, NLCPGGraph graph)
        {
            methodSymbol = CanonicalMethodSymbol(methodSymbol);
            var key = $"methodexit:{SymbolId(methodSymbol)}";
            lock (_cacheGate)
            {
                if (_methodExitNodes.TryGetValue(graph, key, out var existing))
                {
                    return existing;
                }

                var exitNode = graph.AddNode(new NLCPGNodeDraft(
                  Kind: NLCPGNodeKind.MethodExit,
                  Name: $"{ComposeMethodName(methodSymbol)}:exit",
                  FullName: $"{ComposeMethodFullName(methodSymbol)}:exit",
                  Signature: ComposeMethodSignature(methodSymbol),
                  TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
                  FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
                  SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
                  SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
                _methodExitNodes.Set(graph, key, exitNode);
                return exitNode;
            }
        }
    }
}
