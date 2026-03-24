using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class IdentifierRefPass(CpgContext context) : CpgPass(context)
{
    protected override void Apply(DiffGraph diff)
    {
        RoslynFrontendContext? frontendContext = Context.Cpg.FrontendContext;
        if (frontendContext is null)
        {
            return;
        }

        Dictionary<(string ContainingTypeName, string MethodName, string LocalName), string> localNodeIdsByName =
            CreateLocalNodeMap(frontendContext);

        foreach (MethodDeclarationSyntax method in frontendContext.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            string ownerMethodName = method.Identifier.ValueText;
            IMethodSymbol? ownerMethodSymbol = frontendContext.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            string? containingTypeName = RoslynSymbolNameFormatter.GetFullName(ownerMethodSymbol?.ContainingType)
                ?? method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
            InvocationExpressionSyntax[] invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();

            for (int callIndex = 0; callIndex < invocations.Length; callIndex++)
            {
                InvocationExpressionSyntax invocation = invocations[callIndex];
                string resolvedTarget = invocation.Expression.ToString().Split('.').LastOrDefault() ?? "unknown";
                ArgumentSyntax[] arguments = invocation.ArgumentList.Arguments.ToArray();

                if (invocation.Expression is MemberAccessExpressionSyntax receiverAccess)
                {
                    AddReceiverRefEdge(
                        diff,
                        frontendContext,
                        containingTypeName,
                        ownerMethodName,
                        resolvedTarget,
                        callIndex,
                        receiverAccess,
                        localNodeIdsByName);
                }

                for (int argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
                {
                    if (arguments[argumentIndex].Expression is IdentifierNameSyntax methodIdentifier)
                    {
                        ISymbol? methodGroupSymbol = frontendContext.SemanticModel.GetSymbolInfo(methodIdentifier).Symbol;
                        if (methodGroupSymbol is IMethodSymbol resolvedMethodSymbol)
                        {
                            string methodRefNodeId = $"method-ref:{ownerMethodName}:{resolvedTarget}:{callIndex}:{argumentIndex}";
                            string? targetContainingTypeName = RoslynSymbolNameFormatter.GetFullName(resolvedMethodSymbol.ContainingType);
                            string targetMethodNodeId = NodeIdFactory.Method(targetContainingTypeName, resolvedMethodSymbol.Name);
                            if (Context.Cpg.FindNodeById<StoredNode>(methodRefNodeId) is not null &&
                                Context.Cpg.FindNodeById<StoredNode>(targetMethodNodeId) is not null)
                            {
                                diff.AddEdge(new CpgEdge("REF", methodRefNodeId, targetMethodNodeId));
                            }

                            continue;
                        }
                    }

                    if (arguments[argumentIndex].Expression is MemberAccessExpressionSyntax memberAccess &&
                        memberAccess.Name is IdentifierNameSyntax fieldIdentifier)
                    {
                        string fieldIdentifierNodeId = NodeIdFactory.FieldIdentifier(
                            containingTypeName,
                            ownerMethodName,
                            resolvedTarget,
                            callIndex,
                            argumentIndex);
                        ISymbol? fieldSymbol = frontendContext.SemanticModel.GetSymbolInfo(memberAccess).Symbol;
                        if (fieldSymbol is IFieldSymbol resolvedFieldSymbol)
                        {
                            string? containingTypeFullName = RoslynSymbolNameFormatter.GetFullName(resolvedFieldSymbol.ContainingType);
                            string memberNodeId = $"member:{containingTypeFullName}:{fieldIdentifier.Identifier.ValueText}";
                            if (Context.Cpg.FindNodeById<StoredNode>(memberNodeId) is not null)
                            {
                                diff.AddEdge(new CpgEdge("REF", fieldIdentifierNodeId, memberNodeId));
                            }
                        }

                        continue;
                    }

                    if (arguments[argumentIndex].Expression is not IdentifierNameSyntax identifier)
                    {
                        continue;
                    }

                    string identifierNodeId = $"identifier:{ownerMethodName}:{resolvedTarget}:{callIndex}:{argumentIndex}";
                    ISymbol? symbol = frontendContext.SemanticModel.GetSymbolInfo(identifier).Symbol;
                    string? targetNodeId = symbol switch
                    {
                        ILocalSymbol localSymbol => ResolveLocalNodeId(containingTypeName, ownerMethodName, localSymbol.Name, localNodeIdsByName),
                        IParameterSymbol parameterSymbol => NodeIdFactory.MethodParameterIn(
                            containingTypeName,
                            ownerMethodName,
                            parameterSymbol.Name,
                            parameterSymbol.Ordinal + 1),
                        _ => null,
                    };

                    if (!string.IsNullOrWhiteSpace(targetNodeId) &&
                        Context.Cpg.FindNodeById<StoredNode>(targetNodeId) is not null)
                    {
                        diff.AddEdge(new CpgEdge("REF", identifierNodeId, targetNodeId));
                    }
                }
            }
        }
    }

    private static Dictionary<(string ContainingTypeName, string MethodName, string LocalName), string> CreateLocalNodeMap(
        RoslynFrontendContext frontendContext)
    {
        Dictionary<(string ContainingTypeName, string MethodName, string LocalName), string> localNodeIdsByName = new();

        foreach (MethodDeclarationSyntax method in frontendContext.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            IMethodSymbol? methodSymbol = frontendContext.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            string? containingTypeName = RoslynSymbolNameFormatter.GetFullName(methodSymbol?.ContainingType)
                ?? method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
            int localIndex = 0;
            foreach (LocalDeclarationStatementSyntax declaration in method.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
                {
                    localNodeIdsByName[(containingTypeName ?? string.Empty, method.Identifier.ValueText, variable.Identifier.ValueText)] =
                        NodeIdFactory.Local(containingTypeName, method.Identifier.ValueText, variable.Identifier.ValueText, localIndex);
                    localIndex++;
                }
            }
        }

        return localNodeIdsByName;
    }

    private static string? ResolveLocalNodeId(
        string? containingTypeName,
        string methodName,
        string localName,
        IReadOnlyDictionary<(string ContainingTypeName, string MethodName, string LocalName), string> localNodeIdsByName)
    {
        return localNodeIdsByName.TryGetValue((containingTypeName ?? string.Empty, methodName, localName), out string? localNodeId)
            ? localNodeId
            : null;
    }

    private void AddReceiverRefEdge(
        DiffGraph diff,
        RoslynFrontendContext frontendContext,
        string? containingTypeName,
        string ownerMethodName,
        string resolvedTarget,
        int callIndex,
        MemberAccessExpressionSyntax receiverAccess,
        IReadOnlyDictionary<(string ContainingTypeName, string MethodName, string LocalName), string> localNodeIdsByName)
    {
        if (receiverAccess.Expression is IdentifierNameSyntax identifier)
        {
            string receiverNodeId = $"identifier-receiver:{ownerMethodName}:{resolvedTarget}:{callIndex}";
            ISymbol? symbol = frontendContext.SemanticModel.GetSymbolInfo(identifier).Symbol;
            string? targetNodeId = symbol switch
            {
                ILocalSymbol localSymbol => ResolveLocalNodeId(containingTypeName, ownerMethodName, localSymbol.Name, localNodeIdsByName),
                IParameterSymbol parameterSymbol => NodeIdFactory.MethodParameterIn(
                    containingTypeName,
                    ownerMethodName,
                    parameterSymbol.Name,
                    parameterSymbol.Ordinal + 1),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(targetNodeId) &&
                Context.Cpg.FindNodeById<StoredNode>(receiverNodeId) is not null &&
                Context.Cpg.FindNodeById<StoredNode>(targetNodeId) is not null)
            {
                diff.AddEdge(new CpgEdge("REF", receiverNodeId, targetNodeId));
            }

            return;
        }

        if (receiverAccess.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name is IdentifierNameSyntax fieldIdentifier)
        {
            string receiverNodeId = NodeIdFactory.FieldIdentifierReceiver(
                containingTypeName,
                ownerMethodName,
                resolvedTarget,
                callIndex);
            ISymbol? symbol = frontendContext.SemanticModel.GetSymbolInfo(memberAccess).Symbol;
            if (symbol is IFieldSymbol fieldSymbol)
            {
                string? containingTypeFullName = RoslynSymbolNameFormatter.GetFullName(fieldSymbol.ContainingType);
                string memberNodeId = $"member:{containingTypeFullName}:{fieldIdentifier.Identifier.ValueText}";
                if (Context.Cpg.FindNodeById<StoredNode>(receiverNodeId) is not null &&
                    Context.Cpg.FindNodeById<StoredNode>(memberNodeId) is not null)
                {
                    diff.AddEdge(new CpgEdge("REF", receiverNodeId, memberNodeId));
                }
            }
        }
    }
}
