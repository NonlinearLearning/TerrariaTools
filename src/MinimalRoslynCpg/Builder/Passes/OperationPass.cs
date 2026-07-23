using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using MinimalRoslynCpg.Contracts;
using MinimalRoslynCpg.Model;

namespace MinimalRoslynCpg.Builder.Passes
{
  internal sealed class OperationPass : IRoslynCpgPass
  {
    internal static OperationPass Instance { get; } = new();

    private OperationPass()
    {
    }

    public string Name => nameof(OperationPass);

    public void Run(RoslynCpgBuilder builder, RoslynCpgBuildContext context)
    {
      builder.RunOperationPass(context);
    }
  }
}

namespace MinimalRoslynCpg.Builder
{
  public sealed partial class RoslynCpgBuilder
  {
    internal void RunOperationPass(RoslynCpgBuildContext context)
    {
      VisitOperationRoots(GetOperationRootPlans(context.Root, context.SemanticModel), context.Graph, context.SemanticModel);
      CompleteOperationBackedSyntaxTypes(context);
    }

    private void VisitOperationRoots(
      IReadOnlyList<OperationRootPlan> operationRoots,
      RoslynCpgGraph graph,
      SemanticModel semanticModel)
    {
      foreach (var operationRoot in operationRoots)
      {
        AddOperationTree(
          semanticModel.GetOperation(operationRoot.BodySyntax),
          parentOperation: null,
          owningMethod: operationRoot.OwningMethod,
          graph);
      }
    }

    private static IReadOnlyList<OperationRootPlan> GetOperationRootPlans(SyntaxNode root, SemanticModel semanticModel)
    {
      var operationRoots = new List<OperationRootPlan>();
      var order = 0;

      foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
      {
        var methodSymbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
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

        operationRoots.Add(new OperationRootPlan(bodySyntax, methodSymbol, order));
        order += 1;
      }

      foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
      {
        foreach (var accessor in property.AccessorList?.Accessors ?? Enumerable.Empty<AccessorDeclarationSyntax>())
        {
          var accessorSymbol = semanticModel.GetDeclaredSymbol(accessor) as IMethodSymbol;
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

    private void AddOperationTree(IOperation? operation, IOperation? parentOperation, IMethodSymbol? owningMethod, RoslynCpgGraph graph)
    {
      if (operation is null)
      {
        return;
      }

      var operationNode = GetOrCreateOperationNode(operation, graph);
      if (parentOperation is not null)
      {
        var parentNode = GetOrCreateOperationNode(parentOperation, graph);
        graph.AddEdge(parentNode, operationNode, SelectOperationEdge(parentOperation, operation));
      }

      if (_syntaxNodes.TryGetValue(operation.Syntax, out var syntaxNode))
      {
        graph.AddEdge(syntaxNode, operationNode, RoslynCpgEdgeKind.SyntaxHasOperation);
        graph.AddEdge(operationNode, syntaxNode, RoslynCpgEdgeKind.OpHasSyntax);
      }

      AddTypeEdges(operationNode, operation.Type, graph);
      AddEvalTypeEdge(operationNode, operation.Type, graph);
      AddOperationBackedSyntaxTypeEdge(operation, graph);

      var resolvedSymbol = ResolveOperationSymbol(operation);
      if (resolvedSymbol is not null)
      {
        var symbolNode = GetOrCreateSymbolNode(resolvedSymbol, graph);
        graph.AddEdge(operationNode, symbolNode, RoslynCpgEdgeKind.OpResolvesToSymbol);
      }

      foreach (var child in operation.ChildOperations)
      {
        AddOperationTree(child, operation, owningMethod, graph);
      }
    }
  }
}
