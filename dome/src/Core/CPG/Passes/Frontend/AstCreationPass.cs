using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class AstCreationPass(CpgContext context, RoslynFrontendContext frontendContext) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        foreach (MethodDeclarationSyntax method in frontendContext.SyntaxTree.GetRoot()
                     .DescendantNodes()
                     .OfType<MethodDeclarationSyntax>())
        {
            string ownerMethodName = method.Identifier.ValueText;
            IMethodSymbol? ownerMethodSymbol = frontendContext.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            string? containingTypeName = RoslynSymbolNameFormatter.GetFullName(ownerMethodSymbol?.ContainingType)
                ?? method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
            if (method.Body is not null)
            {
                diff.AddNode(new BlockNode(NodeIdFactory.Block(containingTypeName, ownerMethodName), ownerMethodName, containingTypeName));
                AddStructuredStatementNodes(diff, method, ownerMethodName, containingTypeName);
            }

            InvocationExpressionSyntax[] invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();

            for (int index = 0; index < invocations.Length; index++)
            {
                InvocationExpressionSyntax invocation = invocations[index];
                string? targetMethodName = invocation.Expression.ToString().Split('.').LastOrDefault();
                TypeInfo invocationTypeInfo = frontendContext.SemanticModel.GetTypeInfo(invocation);
                IMethodSymbol? symbol = frontendContext.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                string? typeFullName = RoslynSymbolNameFormatter.GetTypeFullName(invocationTypeInfo.Type, RoslynSymbolNameFormatter.GetTypeFullName(symbol?.ReturnType));
                string? methodFullName = RoslynSymbolNameFormatter.GetFullName(symbol) ?? targetMethodName;
                string? resolvedTargetMethodId = targetMethodName is null
                    ? null
                    : NodeIdFactory.Method(RoslynSymbolNameFormatter.GetFullName(symbol?.ContainingType), targetMethodName);
                string callNodeId = NodeIdFactory.Call(containingTypeName, ownerMethodName, targetMethodName, index);
                diff.AddNode(
                    new CallNode(
                        callNodeId,
                        ownerMethodName,
                        targetMethodName,
                        index,
                        containingTypeName,
                        typeFullName,
                        resolvedTargetMethodId,
                        methodFullName));

                string? receiverNodeId = CreateReceiverNode(
                    diff,
                    invocation.Expression,
                    containingTypeName,
                    ownerMethodName,
                    targetMethodName,
                    index,
                    frontendContext);
                if (!string.IsNullOrWhiteSpace(receiverNodeId))
                {
                    diff.AddEdge(new CpgEdge("RECEIVER", callNodeId, receiverNodeId));
                }

                ArgumentSyntax[] arguments = invocation.ArgumentList.Arguments.ToArray();
                for (int argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
                {
                    ArgumentSyntax argument = arguments[argumentIndex];
                    ExpressionSyntax expression = argument.Expression;
                    string argumentId = CreateArgumentNode(
                        diff,
                        expression,
                        containingTypeName,
                        ownerMethodName,
                        targetMethodName,
                        index,
                        argumentIndex,
                        frontendContext);
                    diff.AddEdge(new CpgEdge("ARGUMENT", callNodeId, argumentId));
                }
            }
        }
    }

    private static void AddStructuredStatementNodes(
        DiffGraph diff,
        MethodDeclarationSyntax method,
        string ownerMethodName,
        string? containingTypeName)
    {
        if (method.Body is null)
        {
            return;
        }

        ControlFlowSyntaxFacts.StructuredNodeIds structuredNodeIds =
            ControlFlowSyntaxFacts.BuildStructuredNodeIds(method, containingTypeName, ownerMethodName);
        foreach ((IfStatementSyntax ifStatement, string controlStructureId) in structuredNodeIds.ControlStructureIds)
        {
            _ = ifStatement;
            int order = ParseTrailingOrder(controlStructureId);
            diff.AddNode(
                new ControlStructureNode(
                    controlStructureId,
                    ownerMethodName,
                    "IF",
                    order,
                    containingTypeName));
        }

        foreach ((ReturnStatementSyntax returnStatement, string returnId) in structuredNodeIds.ReturnIds)
        {
            _ = returnStatement;
            int order = ParseTrailingOrder(returnId);
            diff.AddNode(new ReturnNode(returnId, ownerMethodName, order, containingTypeName));
        }
    }

    private static string CreateArgumentNode(
        DiffGraph diff,
        ExpressionSyntax expression,
        string? containingTypeName,
        string ownerMethodName,
        string? targetMethodName,
        int callIndex,
        int argumentIndex,
        RoslynFrontendContext frontendContext)
    {
        string resolvedTarget = targetMethodName ?? "unknown";
        TypeInfo typeInfo = frontendContext.SemanticModel.GetTypeInfo(expression);
        string? typeFullName = RoslynSymbolNameFormatter.GetTypeFullName(typeInfo.Type);
        ISymbol? symbol = frontendContext.SemanticModel.GetSymbolInfo(expression).Symbol;

        if (expression is IdentifierNameSyntax methodIdentifier &&
            symbol is IMethodSymbol)
        {
            string id = $"method-ref:{ownerMethodName}:{resolvedTarget}:{callIndex}:{argumentIndex}";
            diff.AddNode(new MethodRefNode(id, methodIdentifier.Identifier.ValueText, typeFullName));
            return id;
        }

        if (expression is IdentifierNameSyntax identifier)
        {
            string id = $"identifier:{ownerMethodName}:{resolvedTarget}:{callIndex}:{argumentIndex}";
            diff.AddNode(new IdentifierNode(id, identifier.Identifier.ValueText, argumentIndex, typeFullName));
            return id;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name is IdentifierNameSyntax fieldIdentifier)
        {
            string id = NodeIdFactory.FieldIdentifier(containingTypeName, ownerMethodName, resolvedTarget, callIndex, argumentIndex);
            diff.AddNode(new FieldIdentifierNode(id, fieldIdentifier.Identifier.ValueText, typeFullName));
            return id;
        }

        string literalId = $"literal:{ownerMethodName}:{resolvedTarget}:{callIndex}:{argumentIndex}";
        diff.AddNode(new LiteralNode(literalId, expression.ToString(), argumentIndex, typeFullName));
        return literalId;
    }

    private static string? CreateReceiverNode(
        DiffGraph diff,
        ExpressionSyntax invocationExpression,
        string? containingTypeName,
        string ownerMethodName,
        string? targetMethodName,
        int callIndex,
        RoslynFrontendContext frontendContext)
    {
        if (invocationExpression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        string resolvedTarget = targetMethodName ?? "unknown";
        ExpressionSyntax receiverExpression = memberAccess.Expression;
        TypeInfo typeInfo = frontendContext.SemanticModel.GetTypeInfo(receiverExpression);
        string? typeFullName = RoslynSymbolNameFormatter.GetTypeFullName(typeInfo.Type);

        if (receiverExpression is IdentifierNameSyntax identifier)
        {
            string id = $"identifier-receiver:{ownerMethodName}:{resolvedTarget}:{callIndex}";
            diff.AddNode(new IdentifierNode(id, identifier.Identifier.ValueText, -1, typeFullName));
            return id;
        }

        if (receiverExpression is MemberAccessExpressionSyntax receiverMemberAccess &&
            receiverMemberAccess.Name is IdentifierNameSyntax fieldIdentifier)
        {
            string id = NodeIdFactory.FieldIdentifierReceiver(containingTypeName, ownerMethodName, resolvedTarget, callIndex);
            diff.AddNode(new FieldIdentifierNode(id, fieldIdentifier.Identifier.ValueText, typeFullName));
            return id;
        }

        return null;
    }

    private static int ParseTrailingOrder(string nodeId)
    {
        string? trailingSegment = nodeId.Split(':').LastOrDefault();
        return int.TryParse(trailingSegment, out int order) ? order : -1;
    }
}
