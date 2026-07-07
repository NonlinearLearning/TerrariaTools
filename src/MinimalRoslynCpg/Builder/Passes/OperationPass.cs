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
      VisitOperationRoots(context.Root, context.Graph, context.SemanticModel);
    }

    private void VisitOperationRoots(SyntaxNode root, RoslynCpgGraph graph, SemanticModel semanticModel)
    {
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
        if (bodySyntax is not null)
        {
          AddOperationTree(semanticModel.GetOperation(bodySyntax), parentOperation: null, owningMethod: methodSymbol, graph);
        }
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
          if (bodySyntax is not null)
          {
            AddOperationTree(semanticModel.GetOperation(bodySyntax), parentOperation: null, owningMethod: accessorSymbol, graph);
          }
        }
      }

      foreach (var statement in root.DescendantNodes().OfType<GlobalStatementSyntax>())
      {
        AddOperationTree(semanticModel.GetOperation(statement.Statement), parentOperation: null, owningMethod: null, graph);
      }
    }

    private void AddOperationTree(IOperation? operation, IOperation? parentOperation, IMethodSymbol? owningMethod, RoslynCpgGraph graph)
    {
      if (operation is null)
      {
        return;
      }

      var operationNode = GetOrCreateOperationNode(operation, graph);
      if (owningMethod is not null && !_operationOwningMethods.ContainsKey(operation))
      {
        _operationOwningMethods[operation] = owningMethod;
      }

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
