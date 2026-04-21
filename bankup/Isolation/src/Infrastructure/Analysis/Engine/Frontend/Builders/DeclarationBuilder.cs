using Domain.Analysis.Engine.Core;
using Logic.Analysis.Engine.Passes;
using Infrastructure.Analysis.Engine.Passes.ControlFlow;
using Logic.Analysis.Engine.Passes.ControlFlow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Infrastructure.Analysis.Engine.Frontend.Builders;

/// <summary>
/// 负责把类型、成员、方法等声明节点投影成 CPG 节点。
/// </summary>
internal sealed class DeclarationBuilder
{
    private readonly BuilderState state;
    private readonly PrimitiveBuilder primitiveBuilder;
    private readonly ExpressionBuilder expressionBuilder;
    private readonly StatementBuilder statementBuilder;

    /// <summary>
    /// 初始化声明 Builder。
    /// </summary>
    public DeclarationBuilder(
        BuilderState state,
        PrimitiveBuilder primitiveBuilder,
        ExpressionBuilder expressionBuilder,
        StatementBuilder statementBuilder)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.primitiveBuilder = primitiveBuilder ?? throw new ArgumentNullException(nameof(primitiveBuilder));
        this.expressionBuilder = expressionBuilder ?? throw new ArgumentNullException(nameof(expressionBuilder));
        this.statementBuilder = statementBuilder ?? throw new ArgumentNullException(nameof(statementBuilder));
    }

    /// <summary>
    /// 构建类型声明。
    /// </summary>
    public void BuildType(BaseTypeDeclarationSyntax declaration, CpgNode fileNode)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(fileNode);

        INamedTypeSymbol? typeSymbol = state.Context.GetDeclaredSymbol(declaration) as INamedTypeSymbol;
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);
        state.ReferencedTypeFullNames.Add(typeFullName);

        CpgNode typeDeclNode = GetOrCreateTypeNode(typeFullName);
        IReadOnlyCollection<string> baseTypeNames = primitiveBuilder.GetBaseTypeNames(typeSymbol);
        DeclarationAssemblyConventions.ApplyTypeDeclProperties(
            typeDeclNode,
            fileNode.Id,
            primitiveBuilder.GetStringProperty(fileNode, "FileName"),
            RoslynSymbolFormatter.GetSymbolId(typeSymbol)?.Value,
            typeFullName,
            typeSymbol?.TypeKind.ToString() ?? declaration.Kind().ToString(),
            baseTypeNames);
        state.ReferencedTypeFullNames.UnionWith(baseTypeNames);
        primitiveBuilder.SetLocation(typeDeclNode, declaration.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(declaration, typeDeclNode);

        if (declaration is EnumDeclarationSyntax enumDeclaration)
        {
            BuildEnumMembers(enumDeclaration, typeDeclNode, fileNode, typeSymbol);
            return;
        }

        if (declaration is not TypeDeclarationSyntax typeDeclaration)
        {
            return;
        }

        BuildMembers(typeDeclaration, typeDeclNode, fileNode);
    }

    private void BuildMembers(TypeDeclarationSyntax typeDeclaration, CpgNode typeDeclNode, CpgNode fileNode)
    {
        foreach (FieldDeclarationSyntax fieldDeclaration in typeDeclaration.Members.OfType<FieldDeclarationSyntax>())
        {
            BuildFields(fieldDeclaration, typeDeclNode, fileNode);
        }

        foreach (PropertyDeclarationSyntax propertyDeclaration in typeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            BuildProperty(propertyDeclaration, typeDeclNode, fileNode);
        }

        foreach (ConstructorDeclarationSyntax constructorDeclaration in typeDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            BuildConstructor(constructorDeclaration, typeDeclNode, fileNode);
        }

        foreach (MethodDeclarationSyntax methodDeclaration in typeDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            BuildMethod(methodDeclaration, typeDeclNode, fileNode);
        }

        if (typeDeclaration is RecordDeclarationSyntax recordDeclaration &&
            recordDeclaration.ParameterList is not null)
        {
            BuildRecordPrimaryConstructorMembers(recordDeclaration, typeDeclNode, fileNode);
        }
    }

    private void BuildEnumMembers(
        EnumDeclarationSyntax declaration,
        CpgNode typeDeclNode,
        CpgNode fileNode,
        INamedTypeSymbol? typeSymbol)
    {
        string aliasTypeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol?.EnumUnderlyingType);
        DeclarationAssemblyConventions.ApplyEnumAliasType(typeDeclNode, aliasTypeFullName);
        state.ReferencedTypeFullNames.Add(aliasTypeFullName);

        foreach (EnumMemberDeclarationSyntax memberDeclaration in declaration.Members)
        {
            IFieldSymbol? fieldSymbol = state.Context.GetDeclaredSymbol(memberDeclaration) as IFieldSymbol;
            CpgNode memberNode = state.GraphBuilder.CreateNode(CpgNodeKind.Member);
            DeclarationAssemblyConventions.ApplyMemberIdentity(
                memberNode,
                memberDeclaration.Identifier.ValueText,
                fieldSymbol is null
                    ? FrontendGraphConventions.BuildMemberFullName(
                        primitiveBuilder.GetStringProperty(typeDeclNode, "FullName"),
                        memberDeclaration.Identifier.ValueText)
                    : FrontendGraphConventions.BuildMemberFullName(
                        RoslynSymbolFormatter.GetTypeFullName(fieldSymbol.ContainingType),
                        fieldSymbol.Name));
            primitiveBuilder.WriteDeclarationProperties(
                memberNode,
                fieldSymbol,
                typeSymbol?.EnumUnderlyingType ?? fieldSymbol?.Type,
                typeDeclNode.Id,
                fileNode);
            primitiveBuilder.SetLocation(memberNode, memberDeclaration.GetLocation().GetLineSpan());
            primitiveBuilder.RememberNode(memberDeclaration, memberNode);

            if (memberDeclaration.EqualsValue is not null)
            {
                expressionBuilder.BuildExpression(memberDeclaration.EqualsValue.Value, memberNode.Id, fileNode);
            }
        }
    }

    private void BuildRecordPrimaryConstructorMembers(
        RecordDeclarationSyntax declaration,
        CpgNode typeDeclNode,
        CpgNode fileNode)
    {
        HashSet<string> existingMemberNames = state.GraphBuilder.Graph
            .GetNodes(CpgNodeKind.Member)
            .Where(node => node.TryGetProperty<long>("AstParentId", out long parentId) && parentId == typeDeclNode.Id)
            .Select(node => primitiveBuilder.GetStringProperty(node, "Name"))
            .ToHashSet(StringComparer.Ordinal);

        foreach (ParameterSyntax parameter in declaration.ParameterList!.Parameters)
        {
            string name = parameter.Identifier.ValueText;
            if (!existingMemberNames.Add(name))
            {
                continue;
            }

            IParameterSymbol? parameterSymbol = state.Context.GetDeclaredSymbol(parameter) as IParameterSymbol;
            ITypeSymbol? parameterType = parameterSymbol?.Type ?? state.Context.GetTypeInfo(parameter.Type!).Type;
            CpgNode memberNode = state.GraphBuilder.CreateNode(CpgNodeKind.Member);
            DeclarationAssemblyConventions.ApplyMemberIdentity(
                memberNode,
                name,
                FrontendGraphConventions.BuildMemberFullName(
                    primitiveBuilder.GetStringProperty(typeDeclNode, "FullName"),
                    name),
                DeclarationNodeConventions.RecordPrimaryConstructorSource);
            primitiveBuilder.WriteDeclarationProperties(memberNode, parameterSymbol, parameterType, typeDeclNode.Id, fileNode);
            primitiveBuilder.SetLocation(memberNode, parameter.GetLocation().GetLineSpan());
            primitiveBuilder.RememberNode(parameter, memberNode);
        }
    }

    private void BuildFields(FieldDeclarationSyntax declaration, CpgNode typeDeclNode, CpgNode fileNode)
    {
        foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
        {
            IFieldSymbol? fieldSymbol = state.Context.GetDeclaredSymbol(variable) as IFieldSymbol;
            CpgNode memberNode = state.GraphBuilder.CreateNode(CpgNodeKind.Member);
            DeclarationAssemblyConventions.ApplyMemberIdentity(
                memberNode,
                variable.Identifier.Text,
                fieldSymbol is null
                    ? FrontendGraphConventions.BuildMemberFullName(
                        primitiveBuilder.GetStringProperty(typeDeclNode, "FullName"),
                        variable.Identifier.Text)
                    : FrontendGraphConventions.BuildMemberFullName(
                        RoslynSymbolFormatter.GetTypeFullName(fieldSymbol.ContainingType),
                        fieldSymbol.Name));
            primitiveBuilder.WriteDeclarationProperties(memberNode, fieldSymbol, fieldSymbol?.Type, typeDeclNode.Id, fileNode);
            primitiveBuilder.SetLocation(memberNode, variable.GetLocation().GetLineSpan());
            primitiveBuilder.RememberNode(variable, memberNode);
        }
    }

    private void BuildProperty(PropertyDeclarationSyntax declaration, CpgNode typeDeclNode, CpgNode fileNode)
    {
        IPropertySymbol? propertySymbol = state.Context.GetDeclaredSymbol(declaration) as IPropertySymbol;
        CpgNode memberNode = state.GraphBuilder.CreateNode(CpgNodeKind.Member);
        DeclarationAssemblyConventions.ApplyMemberIdentity(
            memberNode,
            declaration.Identifier.Text,
            propertySymbol is null
                ? FrontendGraphConventions.BuildMemberFullName(
                    primitiveBuilder.GetStringProperty(typeDeclNode, "FullName"),
                    declaration.Identifier.Text)
                : FrontendGraphConventions.BuildMemberFullName(
                    RoslynSymbolFormatter.GetTypeFullName(propertySymbol.ContainingType),
                    propertySymbol.Name));
        primitiveBuilder.WriteDeclarationProperties(memberNode, propertySymbol, propertySymbol?.Type, typeDeclNode.Id, fileNode);
        primitiveBuilder.SetLocation(memberNode, declaration.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(declaration, memberNode);

        BuildPropertyAccessors(declaration, typeDeclNode, fileNode);
    }

    private void BuildMethod(MethodDeclarationSyntax declaration, CpgNode typeDeclNode, CpgNode fileNode)
    {
        IMethodSymbol? methodSymbol = state.Context.GetDeclaredSymbol(declaration) as IMethodSymbol;
        CpgNode methodNode = CreateMethodNode(
            declaration,
            declaration.Identifier.Text,
            methodSymbol,
            typeDeclNode.Id,
            fileNode);
        BuildMethodBody(declaration.Body, declaration.ExpressionBody, methodNode, fileNode);
    }

    private void BuildParametersAndReturn(IMethodSymbol? methodSymbol, CpgNode methodNode, CpgNode fileNode)
    {
        int order = 1;
        foreach (IParameterSymbol parameterSymbol in methodSymbol?.Parameters ?? Enumerable.Empty<IParameterSymbol>())
        {
            CpgNode parameterNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodParameterIn);
            MethodNodeConventions.ApplyMethodParameterOrder(parameterNode, parameterSymbol.Name, order);
            primitiveBuilder.WriteDeclarationProperties(parameterNode, parameterSymbol, parameterSymbol.Type, methodNode.Id, fileNode);
            state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(parameterSymbol.Type));
            order++;
        }

        CpgNode returnNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodReturn);
        MethodNodeConventions.ApplyMethodReturnProperties(
            returnNode,
            RoslynSymbolFormatter.GetTypeFullName(methodSymbol?.ReturnType),
            methodNode.Id,
            order);
    }

    private void BuildConstructor(ConstructorDeclarationSyntax declaration, CpgNode typeDeclNode, CpgNode fileNode)
    {
        IMethodSymbol? methodSymbol = state.Context.GetDeclaredSymbol(declaration) as IMethodSymbol;
        CpgNode methodNode = CreateMethodNode(
            declaration,
            methodSymbol?.Name ?? declaration.Identifier.Text,
            methodSymbol,
            typeDeclNode.Id,
            fileNode);
        BuildMethodBody(declaration.Body, declaration.ExpressionBody, methodNode, fileNode);
    }

    private void BuildPropertyAccessors(PropertyDeclarationSyntax declaration, CpgNode typeDeclNode, CpgNode fileNode)
    {
        if (declaration.AccessorList is null)
        {
            return;
        }

        foreach (AccessorDeclarationSyntax accessor in declaration.AccessorList.Accessors)
        {
            IMethodSymbol? accessorSymbol = state.Context.GetDeclaredSymbol(accessor) as IMethodSymbol;
            string accessorName = FrontendGraphConventions.BuildPropertyAccessorName(
                accessor.Keyword.ValueText,
                declaration.Identifier.Text);

            CpgNode methodNode = CreateMethodNode(
                accessor,
                accessorSymbol?.Name ?? accessorName,
                accessorSymbol,
                typeDeclNode.Id,
                fileNode);
            BuildMethodBody(accessor.Body, accessor.ExpressionBody, methodNode, fileNode);
        }
    }

    private CpgNode CreateMethodNode(
        SyntaxNode declaration,
        string methodName,
        IMethodSymbol? methodSymbol,
        long astParentId,
        CpgNode fileNode)
    {
        CpgNode methodNode = state.GraphBuilder.CreateNode(CpgNodeKind.Method);
        string returnTypeFullName = RoslynSymbolFormatter.GetTypeFullName(methodSymbol?.ReturnType);
        MethodNodeConventions.ApplyMethodProperties(
            methodNode,
            new MethodNodeDescriptor(
                methodName,
                RoslynSymbolFormatter.GetMethodFullName(methodSymbol),
                RoslynSymbolFormatter.GetMethodSignature(methodSymbol),
                returnTypeFullName,
                RoslynSymbolFormatter.GetSymbolId(methodSymbol)?.Value,
                RoslynSymbolFormatter.GetTypeFullName(methodSymbol?.ContainingType),
                methodSymbol?.IsAbstract ?? false,
                methodSymbol?.IsVirtual ?? false,
                methodSymbol?.IsOverride ?? false,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(methodNode, declaration.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(declaration, methodNode);

        state.ReferencedTypeFullNames.Add(returnTypeFullName);
        BuildParametersAndReturn(methodSymbol, methodNode, fileNode);
        return methodNode;
    }

    private void BuildMethodBody(
        BlockSyntax? body,
        ArrowExpressionClauseSyntax? expressionBody,
        CpgNode methodNode,
        CpgNode fileNode)
    {
        if (body is not null)
        {
            statementBuilder.BuildBlock(body, methodNode.Id, fileNode);
            ApplyCfg(body);
            return;
        }

        if (expressionBody is not null)
        {
            CpgNode returnNode = primitiveBuilder.CreateControlNode(
                expressionBody.Expression,
                methodNode.Id,
                FrontendControlFlowConventions.Return);
            expressionBuilder.BuildExpression(expressionBody.Expression, returnNode.Id, fileNode);
        }
    }

    private CpgNode GetOrCreateTypeNode(string typeFullName)
    {
        CpgNode? existingNode = state.GraphBuilder.Graph
            .GetNodes(CpgNodeKind.TypeDecl)
            .LastOrDefault(node => primitiveBuilder.HasPropertyValue(node, "FullName", typeFullName));

        if (existingNode is not null)
        {
            return existingNode;
        }

        new BuildTypeStubPass(new[] { typeFullName }).Run(state.GraphBuilder.Graph);
        return state.GraphBuilder.Graph.GetNodes(CpgNodeKind.TypeDecl)
            .Last(node => primitiveBuilder.HasPropertyValue(node, "FullName", typeFullName));
    }

    private void ApplyCfg(BlockSyntax methodBody)
    {
        CfgBuilder cfgBuilder = new(state.NodeIdsBySyntax);
        CfgModel cfgModel = cfgBuilder.Build(methodBody);

        foreach ((long sourceId, long targetId) in cfgModel.Edges)
        {
            AppendNextCfgNodeId(sourceId, targetId);
        }
    }

    private void AppendNextCfgNodeId(long sourceId, long targetId)
    {
        CpgNode source = state.GraphBuilder.Graph.GetNode(sourceId);
        GraphNodeConventions.AppendNextCfgNodeId(source, targetId);
    }
}
