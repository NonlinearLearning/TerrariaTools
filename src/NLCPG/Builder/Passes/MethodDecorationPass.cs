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
    public sealed partial class NLCPGBuilder
    {
        internal void RunMethodDecorationPass(NLCPGBuildContext context)
        {
            var methodDeclarations = context.Root.DescendantNodes()
              .OfType<BaseMethodDeclarationSyntax>()
              .Cast<SyntaxNode>()
              .Concat(context.Root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
              .Concat(context.Root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
              .ToArray();
            foreach (var syntax in methodDeclarations)
            {
                if (!_syntaxNodes.TryGetValue(syntax, out var syntaxNode) ||
                    context.SemanticModel.GetDeclaredSymbol(syntax) is not IMethodSymbol methodSymbol)
                {
                    // 只有已建 syntax 节点且可解析出方法符号的声明才参与方法抽象补建。
                    continue;
                }

                AddMethodAbstractions(syntaxNode, methodSymbol, context.Graph);
            }
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
            if (_methodNodes.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var methodNode = graph.AddNode(new NLCPGNode(
              Kind: NLCPGNodeKind.Method,
              DisplayKind: nameof(NLCPGNodeKind.Method),
              Name: ComposeMethodName(methodSymbol),
              FullName: ComposeMethodFullName(methodSymbol),
              Signature: ComposeMethodSignature(methodSymbol),
              DispatchKind: ComposeMethodDispatchKind(methodSymbol),
              TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
              FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
              SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
              SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
            _symbolKeysByNode[methodNode] = methodSymbolKey;
            _methodNodes[key] = methodNode;
            return methodNode;
        }

        private NLCPGNode GetOrCreateMethodParameterNode(IMethodSymbol methodSymbol, IParameterSymbol parameterSymbol, NLCPGGraph graph)
        {
            methodSymbol = CanonicalMethodSymbol(methodSymbol);
            var key = $"methodparam:{SymbolId(methodSymbol)}:{parameterSymbol.Ordinal}";
            var methodSymbolKey = SymbolId(methodSymbol);
            if (_methodParameterNodes.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var parameterNode = graph.AddNode(new NLCPGNode(
              Kind: NLCPGNodeKind.MethodParameter,
              DisplayKind: nameof(NLCPGNodeKind.MethodParameter),
              Name: parameterSymbol.Name,
              FullName: $"{ComposeMethodFullName(methodSymbol)}#{parameterSymbol.Ordinal}:{parameterSymbol.Name}",
              Signature: ComposeTypeFullName(parameterSymbol.Type),
              TypeFullName: ComposeTypeFullName(parameterSymbol.Type),
              FilePath: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
              SpanStart: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
              SpanEnd: parameterSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
            _methodOwnerSymbolKeysByBoundaryNode[parameterNode] = methodSymbolKey;
            _methodParameterOrdinalsByNode[parameterNode] = parameterSymbol.Ordinal;
            _methodParameterNodes[key] = parameterNode;

            var parameterSymbolNode = GetOrCreateSymbolNode(parameterSymbol, graph);
            graph.AddEdge(parameterNode, parameterSymbolNode, NLCPGEdgeKind.Ref);
            AddEvalTypeEdge(parameterNode, parameterSymbol.Type, graph);
            return parameterNode;
        }

        private NLCPGNode GetOrCreateMethodReturnNode(IMethodSymbol methodSymbol, NLCPGGraph graph)
        {
            methodSymbol = CanonicalMethodSymbol(methodSymbol);
            var key = $"methodreturn:{SymbolId(methodSymbol)}";
            var methodSymbolKey = SymbolId(methodSymbol);
            if (_methodReturnNodes.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var returnNode = graph.AddNode(new NLCPGNode(
              Kind: NLCPGNodeKind.MethodReturn,
              DisplayKind: nameof(NLCPGNodeKind.MethodReturn),
              Name: $"{ComposeMethodName(methodSymbol)}:return",
              FullName: $"{ComposeMethodFullName(methodSymbol)}:return",
              Signature: ComposeTypeFullName(methodSymbol.ReturnType),
              TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
              FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
              SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
              SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
            _methodOwnerSymbolKeysByBoundaryNode[returnNode] = methodSymbolKey;
            _methodReturnNodes[key] = returnNode;
            return returnNode;
        }

        private NLCPGNode GetOrCreateMethodEntryNode(IMethodSymbol methodSymbol, NLCPGGraph graph)
        {
            methodSymbol = CanonicalMethodSymbol(methodSymbol);
            var key = $"methodentry:{SymbolId(methodSymbol)}";
            if (_methodEntryNodes.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var entryNode = graph.AddNode(new NLCPGNode(
              Kind: NLCPGNodeKind.MethodEntry,
              DisplayKind: nameof(NLCPGNodeKind.MethodEntry),
              Name: $"{ComposeMethodName(methodSymbol)}:entry",
              FullName: $"{ComposeMethodFullName(methodSymbol)}:entry",
              Signature: ComposeMethodSignature(methodSymbol),
              TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
              FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
              SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start,
              SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start));
            _methodEntryNodes[key] = entryNode;
            return entryNode;
        }

        private NLCPGNode GetOrCreateMethodExitNode(IMethodSymbol methodSymbol, NLCPGGraph graph)
        {
            methodSymbol = CanonicalMethodSymbol(methodSymbol);
            var key = $"methodexit:{SymbolId(methodSymbol)}";
            if (_methodExitNodes.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var exitNode = graph.AddNode(new NLCPGNode(
              Kind: NLCPGNodeKind.MethodExit,
              DisplayKind: nameof(NLCPGNodeKind.MethodExit),
              Name: $"{ComposeMethodName(methodSymbol)}:exit",
              FullName: $"{ComposeMethodFullName(methodSymbol)}:exit",
              Signature: ComposeMethodSignature(methodSymbol),
              TypeFullName: ComposeTypeFullName(methodSymbol.ReturnType),
              FilePath: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree?.FilePath,
              SpanStart: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End,
              SpanEnd: methodSymbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.End));
            _methodExitNodes[key] = exitNode;
            return exitNode;
        }
    }
}
