using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Core.Cpg;

public sealed class AliasLinkerPass(CpgContext context) : CpgPass(context)
{
    private Dictionary<string, TypeNode>? typeNodesByFullName;

    protected override void Apply(DiffGraph diff)
    {
        RoslynFrontendContext? frontendContext = Context.Cpg.FrontendContext;
        if (frontendContext is null)
        {
            return;
        }

        typeNodesByFullName = Context.Cpg.GetNodesByKind<TypeNode>(NodeKinds.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type.FullName))
            .ToDictionary(type => type.FullName!, StringComparer.Ordinal);

        LinkAliasedBaseTypes(diff, frontendContext);
        LinkAliasedMethodTypes(diff, frontendContext);
        LinkAliasedFieldTypes(diff, frontendContext);
    }

    private void LinkAliasedBaseTypes(DiffGraph diff, RoslynFrontendContext frontendContext)
    {
        foreach (ClassDeclarationSyntax declaration in frontendContext.SyntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            BaseTypeSyntax? baseTypeSyntax = declaration.BaseList?.Types.FirstOrDefault();
            if (baseTypeSyntax is null)
            {
                continue;
            }

            ITypeSymbol? baseTypeSymbol = frontendContext.SemanticModel.GetTypeInfo(baseTypeSyntax.Type).Type;
            string sourceTypeName = baseTypeSyntax.Type.ToString();
            string? resolvedTypeFullName = RoslynSymbolNameFormatter.GetTypeFullName(baseTypeSymbol, sourceTypeName);
            if (!IsAliasedReference(sourceTypeName, resolvedTypeFullName))
            {
                continue;
            }

            INamedTypeSymbol? typeSymbol = frontendContext.SemanticModel.GetDeclaredSymbol(declaration) as INamedTypeSymbol;
            string typeFullName = RoslynSymbolNameFormatter.GetFullName(typeSymbol) ?? declaration.Identifier.ValueText;
            string typeRefId = $"type-ref:type:{typeFullName}:base:{resolvedTypeFullName}";
            AddAliasEdge(diff, typeRefId, resolvedTypeFullName);
        }
    }

    private void LinkAliasedMethodTypes(DiffGraph diff, RoslynFrontendContext frontendContext)
    {
        foreach (MethodDeclarationSyntax declaration in frontendContext.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            string methodName = declaration.Identifier.ValueText;
            IMethodSymbol? methodSymbol = frontendContext.SemanticModel.GetDeclaredSymbol(declaration) as IMethodSymbol;
            string? containingTypeName = RoslynSymbolNameFormatter.GetFullName(methodSymbol?.ContainingType)
                ?? declaration.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
            string? returnTypeName = RoslynSymbolNameFormatter.GetTypeFullName(methodSymbol?.ReturnType, declaration.ReturnType.ToString());
            if (IsAliasedReference(declaration.ReturnType.ToString(), returnTypeName))
            {
                AddAliasEdge(diff, NodeIdFactory.MethodReturn(containingTypeName, methodName), returnTypeName);
            }

            for (int index = 0; index < declaration.ParameterList.Parameters.Count; index++)
            {
                ParameterSyntax parameter = declaration.ParameterList.Parameters[index];
                IParameterSymbol? parameterSymbol = frontendContext.SemanticModel.GetDeclaredSymbol(parameter) as IParameterSymbol;
                string sourceTypeName = parameter.Type?.ToString() ?? string.Empty;
                string? resolvedTypeFullName = RoslynSymbolNameFormatter.GetTypeFullName(parameterSymbol?.Type, sourceTypeName);
                if (!IsAliasedReference(sourceTypeName, resolvedTypeFullName))
                {
                    continue;
                }

                string parameterNodeId = NodeIdFactory.MethodParameterIn(
                    containingTypeName,
                    methodName,
                    parameter.Identifier.ValueText,
                    index + 1);
                AddAliasEdge(diff, parameterNodeId, resolvedTypeFullName);

                string parameterOutNodeId = NodeIdFactory.MethodParameterOut(
                    containingTypeName,
                    methodName,
                    parameter.Identifier.ValueText,
                    index + 1);
                if (Context.Cpg.FindNodeById<StoredNode>(parameterOutNodeId) is not null)
                {
                    AddAliasEdge(diff, parameterOutNodeId, resolvedTypeFullName);
                }
            }

            int localIndex = 0;
            foreach (LocalDeclarationStatementSyntax localDeclaration in declaration.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                ITypeSymbol? localTypeSymbol = frontendContext.SemanticModel.GetTypeInfo(localDeclaration.Declaration.Type).Type;
                string sourceTypeName = localDeclaration.Declaration.Type.ToString();
                string? resolvedTypeFullName = RoslynSymbolNameFormatter.GetTypeFullName(localTypeSymbol, sourceTypeName);
                foreach (VariableDeclaratorSyntax variable in localDeclaration.Declaration.Variables)
                {
                    if (IsAliasedReference(sourceTypeName, resolvedTypeFullName))
                    {
                        string localNodeId = NodeIdFactory.Local(
                            containingTypeName,
                            methodName,
                            variable.Identifier.ValueText,
                            localIndex);
                        AddAliasEdge(diff, localNodeId, resolvedTypeFullName);
                    }

                    localIndex++;
                }
            }
        }
    }

    private void LinkAliasedFieldTypes(DiffGraph diff, RoslynFrontendContext frontendContext)
    {
        foreach (FieldDeclarationSyntax declaration in frontendContext.SyntaxTree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            ITypeSymbol? typeSymbol = frontendContext.SemanticModel.GetTypeInfo(declaration.Declaration.Type).Type;
            string sourceTypeName = declaration.Declaration.Type.ToString();
            string? resolvedTypeFullName = RoslynSymbolNameFormatter.GetTypeFullName(typeSymbol, sourceTypeName);
            if (!IsAliasedReference(sourceTypeName, resolvedTypeFullName))
            {
                continue;
            }

            INamedTypeSymbol? containingTypeSymbol = declaration.Ancestors()
                .OfType<ClassDeclarationSyntax>()
                .Select(classDeclaration => frontendContext.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol)
                .FirstOrDefault(symbol => symbol is not null);
            string? containingTypeName = RoslynSymbolNameFormatter.GetFullName(containingTypeSymbol)
                ?? declaration.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(containingTypeName))
            {
                continue;
            }

            foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
            {
                AddAliasEdge(diff, $"member:{containingTypeName}:{variable.Identifier.ValueText}", resolvedTypeFullName);
            }
        }
    }

    private void AddAliasEdge(DiffGraph diff, string sourceId, string? typeFullName)
    {
        if (string.IsNullOrWhiteSpace(typeFullName))
        {
            return;
        }

        if (typeNodesByFullName is not null &&
            typeNodesByFullName.TryGetValue(typeFullName, out TypeNode? typeNode) &&
            Context.Cpg.FindNodeById<StoredNode>(sourceId) is not null)
        {
            diff.AddEdge(new CpgEdge(EdgeKinds.AliasOf, sourceId, typeNode.Id));
        }
    }

    private static bool IsAliasedReference(string? sourceTypeName, string? resolvedTypeFullName)
    {
        return !string.IsNullOrWhiteSpace(sourceTypeName) &&
               !string.IsNullOrWhiteSpace(resolvedTypeFullName) &&
               !string.Equals(sourceTypeName, resolvedTypeFullName, StringComparison.Ordinal);
    }
}
