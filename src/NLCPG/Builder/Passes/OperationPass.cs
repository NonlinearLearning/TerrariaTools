using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NLCPG.Contracts;
using NLCPG.Model;

namespace NLCPG.Builder.Passes
{
    internal sealed class OperationPass : INLCPGPass
    {
        internal static OperationPass Instance { get; } = new();

        private OperationPass()
        {
        }

        public string Name => nameof(OperationPass);

        // 触发操作树 pass，把 Roslyn IOperation 树落成图节点与结构边。
        public void Run(NLCPGBuilder builder, NLCPGBuildContext context)
        {
            // Pass 入口只负责转发，真正的 Operation 图构建与回填在 builder 内完成。
            builder.RunOperationPass(context);
        }
    }
}

namespace NLCPG.Builder
{
    public sealed partial class NLCPGBuilder
    {
        internal void RunOperationPass(NLCPGBuildContext context)
        {
            VisitOperationRoots(
              GetOperationRootPlans(context.Root, context.SemanticModel),
              context,
              context.Graph,
              context.SemanticModel);
            CompleteOperationBackedSyntaxTypes(context);
        }

        private void VisitOperationRoots(IReadOnlyList<OperationRootPlan> operationRoots, NLCPGBuildContext context, NLCPGGraph graph, SemanticModel semanticModel)
        {
            foreach (var operationRoot in operationRoots)
            {
                // 根节点缺失时 AddOperationTree 会直接退出，因此这里可以统一走同一入口。
                AddOperationTree(
                  semanticModel.GetOperation(operationRoot.BodySyntax),
                  parentOperation: null,
                  owningMethod: operationRoot.OwningMethod,
                  graph,
                  context);
            }
        }

        private static IReadOnlyList<OperationRootPlan> GetOperationRootPlans(SyntaxNode root, SemanticModel semanticModel)
        {
            var operationRoots = new List<OperationRootPlan>();
            var order = 0;

            foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
                // 普通方法和构造函数都允许 block body 或表达式体作为 operation 根。
                SyntaxNode? bodySyntax = method switch
                {
                    MethodDeclarationSyntax x when x.Body is not null => x.Body,
                    MethodDeclarationSyntax x when x.ExpressionBody is not null => x.ExpressionBody.Expression,
                    ConstructorDeclarationSyntax x when x.Body is not null => x.Body,
                    ConstructorDeclarationSyntax x when x.ExpressionBody is not null => x.ExpressionBody.Expression,
                    _ => null,
                };
                if (bodySyntax is null)
                {
                    continue;
                }

                // 这里记录的顺序会被后续串行或分区提交路径共同复用。
                operationRoots.Add(new OperationRootPlan(bodySyntax, methodSymbol, order));
                order += 1;
            }

            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                foreach (var accessor in property.AccessorList?.Accessors ?? Enumerable.Empty<AccessorDeclarationSyntax>())
                {
                    var accessorSymbol = semanticModel.GetDeclaredSymbol(accessor) as IMethodSymbol;
                    // 访问器和方法统一走 IMethodSymbol，后续 Method/Call 语义可以共用同一套键。
                    SyntaxNode? bodySyntax = accessor switch
                    {
                        { Body: not null } x => x.Body,
                        { ExpressionBody: not null } x => x.ExpressionBody.Expression,
                        _ => null,
                    };
                    if (bodySyntax is null)
                    {
                        continue;
                    }

                    operationRoots.Add(new OperationRootPlan(bodySyntax, accessorSymbol, order));
                    order += 1;
                }
            }

            foreach (var statement in root.DescendantNodes().OfType<GlobalStatementSyntax>())
            {
                operationRoots.Add(new OperationRootPlan(statement.Statement, OwningMethod: null, order));
                order += 1;
            }

            return operationRoots;
        }

        private void AddOperationTree(IOperation? operation, IOperation? parentOperation, IMethodSymbol? owningMethod, NLCPGGraph graph, NLCPGBuildContext context)
        {
            if (operation is null)
            {
                return;
            }

            context.AddOperationInventoryEntry(operation, owningMethod, parentOperation is null);
            var operationNode = GetOrCreateOperationNode(operation, graph);
            if (parentOperation is not null)
            {
                // 非根操作通过父 operation 决定边类型，保留 Roslyn operation 树的结构关系。
                var parentNode = GetOrCreateOperationNode(parentOperation, graph);
                graph.AddEdge(parentNode, operationNode, SelectOperationEdge(parentOperation, operation));
            }

            if (_syntaxNodes.TryGetValue(operation.Syntax, out var syntaxNode))
            {
                // syntax 与 operation 保持双向映射，方便后续从任一视角回到另一侧。
                graph.AddEdge(syntaxNode, operationNode, NLCPGEdgeKind.SyntaxHasOperation);
                graph.AddEdge(operationNode, syntaxNode, NLCPGEdgeKind.OpHasSyntax);
            }

            // operation 节点自身补类型和求值类型；部分 syntax 类型边也从这里回填。
            AddTypeEdges(operationNode, operation.Type, graph);
            AddEvalTypeEdge(operationNode, operation.Type, graph);
            AddOperationBackedSyntaxTypeEdge(operation, graph);

            var resolvedSymbol = ResolveOperationSymbol(operation);
            if (resolvedSymbol is not null)
            {
                // 只有可解析到符号的 operation 才补解析边，避免制造空壳 symbol 节点。
                var symbolNode = GetOrCreateSymbolNode(resolvedSymbol, graph);
                graph.AddEdge(operationNode, symbolNode, NLCPGEdgeKind.OpResolvesToSymbol);
            }

            foreach (var child in operation.ChildOperations)
            {
                // 深度优先递归继续展开子操作树，维持与 Roslyn ChildOperations 一致的顺序。
                AddOperationTree(child, operation, owningMethod, graph, context);
            }
        }
    }
}
