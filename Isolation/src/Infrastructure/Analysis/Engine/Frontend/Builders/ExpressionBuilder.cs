using Domain.Analysis.Engine.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Infrastructure.Analysis.Engine.Frontend.Builders;

/// <summary>
/// 负责把表达式节点投影成 CPG 节点。
///
/// 这里直接对齐 Joern 里表达式构建的职责边界：
/// - 声明留给声明 Builder；
/// - 语句留给语句 Builder；
/// - 表达式内部细节集中在这里。
/// </summary>
internal sealed class ExpressionBuilder
{
    private readonly BuilderState state;
    private readonly PrimitiveBuilder primitiveBuilder;

    /// <summary>
    /// 初始化表达式 Builder。
    /// </summary>
    public ExpressionBuilder(BuilderState state, PrimitiveBuilder primitiveBuilder)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.primitiveBuilder = primitiveBuilder ?? throw new ArgumentNullException(nameof(primitiveBuilder));
    }

    /// <summary>
    /// 构建任意表达式。
    /// </summary>
    public void BuildExpression(ExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(fileNode);

        switch (expression)
        {
            case SimpleLambdaExpressionSyntax simpleLambdaExpression:
                BuildSimpleLambda(simpleLambdaExpression, astParentId, fileNode);
                break;
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpression:
                BuildParenthesizedLambda(parenthesizedLambdaExpression, astParentId, fileNode);
                break;
            case ThisExpressionSyntax thisExpression:
                BuildThis(thisExpression, astParentId, fileNode);
                break;
            case ConditionalAccessExpressionSyntax conditionalAccessExpression:
                BuildConditionalAccess(conditionalAccessExpression, astParentId, fileNode);
                break;
            case SwitchExpressionSyntax switchExpression:
                BuildSwitchExpression(switchExpression, astParentId, fileNode);
                break;
            case IsPatternExpressionSyntax isPatternExpression:
                BuildIsPatternExpression(isPatternExpression, astParentId, fileNode);
                break;
            case InterpolatedStringExpressionSyntax interpolatedStringExpression:
                BuildInterpolatedString(interpolatedStringExpression, astParentId, fileNode);
                break;
            case ArrayCreationExpressionSyntax arrayCreationExpression:
                BuildArrayCreation(arrayCreationExpression, astParentId, fileNode);
                break;
            case ImplicitArrayCreationExpressionSyntax implicitArrayCreationExpression:
                BuildImplicitArrayCreation(implicitArrayCreationExpression, astParentId, fileNode);
                break;
            case InitializerExpressionSyntax initializerExpression:
                BuildInitializer(initializerExpression, astParentId, fileNode);
                break;
            case CollectionExpressionSyntax collectionExpression:
                BuildCollectionExpression(collectionExpression, astParentId, fileNode);
                break;
            case PrefixUnaryExpressionSyntax prefixUnaryExpression:
                BuildUnary(prefixUnaryExpression, prefixUnaryExpression.OperatorToken.Text, astParentId, fileNode);
                break;
            case PostfixUnaryExpressionSyntax postfixUnaryExpression:
                BuildUnary(postfixUnaryExpression, postfixUnaryExpression.OperatorToken.Text, astParentId, fileNode);
                break;
            case IdentifierNameSyntax identifierName:
                if (TryBuildMethodReference(identifierName, identifierName.Identifier.Text, astParentId, fileNode))
                {
                    break;
                }

                if (TryBuildPropertyGetterCall(identifierName, astParentId, fileNode))
                {
                    break;
                }

                BuildIdentifier(identifierName, identifierName.Identifier.Text, astParentId, fileNode);
                break;
            case MemberAccessExpressionSyntax memberAccess:
                if (TryBuildMethodReference(memberAccess, memberAccess.Name.Identifier.Text, astParentId, fileNode))
                {
                    break;
                }

                if (TryBuildPropertyGetterCall(memberAccess, astParentId, fileNode))
                {
                    break;
                }

                if (TryBuildFieldAccess(memberAccess, astParentId, fileNode))
                {
                    break;
                }

                BuildIdentifier(memberAccess, memberAccess.Name.Identifier.Text, astParentId, fileNode);
                BuildExpression(memberAccess.Expression, astParentId, fileNode);
                break;
            case MemberBindingExpressionSyntax memberBinding:
                if (TryBuildPropertyGetterCall(memberBinding, astParentId, fileNode))
                {
                    break;
                }

                BuildIdentifier(memberBinding, memberBinding.Name.Identifier.Text, astParentId, fileNode);
                break;
            case LiteralExpressionSyntax literalExpression:
                BuildLiteral(literalExpression, astParentId, fileNode);
                break;
            case InvocationExpressionSyntax invocationExpression:
                BuildInvocation(invocationExpression, astParentId, fileNode);
                break;
            case ObjectCreationExpressionSyntax objectCreationExpression:
                BuildObjectCreation(objectCreationExpression, astParentId, fileNode);
                break;
            case AnonymousObjectCreationExpressionSyntax anonymousObjectCreationExpression:
                BuildAnonymousObjectCreation(anonymousObjectCreationExpression, astParentId, fileNode);
                break;
            case BinaryExpressionSyntax binaryExpression:
                BuildBinary(binaryExpression, astParentId, fileNode);
                break;
            case AssignmentExpressionSyntax assignmentExpression:
                BuildAssignment(assignmentExpression, astParentId, fileNode);
                break;
            case AwaitExpressionSyntax awaitExpression:
                BuildAwait(awaitExpression, astParentId, fileNode);
                break;
            case ConditionalExpressionSyntax conditionalExpression:
                BuildConditional(conditionalExpression, astParentId, fileNode);
                break;
            case CastExpressionSyntax castExpression:
                BuildCast(castExpression, astParentId, fileNode);
                break;
            case ElementAccessExpressionSyntax elementAccessExpression:
                BuildElementAccess(elementAccessExpression, astParentId, fileNode);
                break;
            case ParenthesizedExpressionSyntax parenthesizedExpression:
                BuildExpression(parenthesizedExpression.Expression, astParentId, fileNode);
                state.NodeIdsBySyntax[expression] = state.NodeIdsBySyntax[parenthesizedExpression.Expression];
                break;
            default:
                List<ExpressionSyntax> childExpressions = expression.ChildNodes().OfType<ExpressionSyntax>().ToList();
                foreach (ExpressionSyntax childExpression in childExpressions)
                {
                    BuildExpression(childExpression, astParentId, fileNode);
                }

                if (childExpressions.Count > 0 &&
                    state.NodeIdsBySyntax.TryGetValue(childExpressions[0], out long childNodeId))
                {
                    state.NodeIdsBySyntax[expression] = childNodeId;
                }
                break;
        }
    }

    /// <summary>
    /// 构建标识符类表达式。
    /// </summary>
    public void BuildIdentifier(ExpressionSyntax expression, string name, long astParentId, CpgNode fileNode)
    {
        ISymbol? symbol = state.Context.GetSymbolInfo(expression).Symbol;
        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;

        CpgNode identifierNode = state.GraphBuilder.CreateNode(CpgNodeKind.Identifier);
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);
        ExpressionAssemblyConventions.ApplyIdentifierProperties(
            identifierNode,
            name,
            expression.ToString(),
            typeFullName,
            RoslynSymbolFormatter.GetSymbolId(symbol)?.Value,
            astParentId,
            primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(identifierNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, identifierNode);
        state.ReferencedTypeFullNames.Add(typeFullName);
    }

    /// <summary>
    /// 构建字面量。
    /// </summary>
    public void BuildLiteral(LiteralExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;

        CpgNode literalNode = state.GraphBuilder.CreateNode(CpgNodeKind.Literal);
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);
        ExpressionAssemblyConventions.ApplyLiteralProperties(
            literalNode,
            expression.Token.ValueText,
            expression.Token.Text,
            typeFullName,
            astParentId,
            primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(literalNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, literalNode);
        state.ReferencedTypeFullNames.Add(typeFullName);
    }

    /// <summary>
    /// 构建方法调用。
    /// </summary>
    public void BuildInvocation(InvocationExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        IMethodSymbol? methodSymbol = GetBestMethodSymbol(state.Context.GetSymbolInfo(expression));
        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);

        CpgNode callNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        NodeAssemblyConventions.ApplyCallNodeProperties(
            callNode,
            new CallNodeDescriptor(
                methodSymbol?.Name ?? expression.Expression.ToString(),
                expression.ToString(),
                RoslynSymbolFormatter.GetMethodFullName(methodSymbol),
                RoslynSymbolFormatter.GetMethodSignature(methodSymbol),
                typeFullName,
                FrontendGraphConventions.GetDispatchType(
                    methodSymbol?.IsStatic is true,
                    methodSymbol?.ReducedFrom is not null),
                RoslynSymbolFormatter.GetSymbolId(methodSymbol)?.Value,
                primitiveBuilder.CreateOperationId(expression).Value,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, callNode);
        state.ReferencedTypeFullNames.Add(typeFullName);
        primitiveBuilder.AddExternalMethodStubIfNeeded(methodSymbol);

        BuildExpression(expression.Expression, callNode.Id, fileNode);
        AddReceiverEdge(expression.Expression, callNode, fileNode);
        AddReceiverEdgeFromInvocationText(expression.Expression, callNode, fileNode);

        int argumentIndex = 1;
        foreach (ArgumentSyntax argument in expression.ArgumentList.Arguments)
        {
            BuildExpression(argument.Expression, callNode.Id, fileNode);
            AddArgumentEdge(argument.Expression, callNode, argumentIndex);
            argumentIndex++;
        }
    }

    /// <summary>
    /// 构建对象创建表达式。
    /// </summary>
    public void BuildObjectCreation(ObjectCreationExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        IMethodSymbol? methodSymbol = GetBestMethodSymbol(state.Context.GetSymbolInfo(expression));
        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);

        CpgNode callNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        NodeAssemblyConventions.ApplyCallNodeProperties(
            callNode,
            new CallNodeDescriptor(
                methodSymbol?.Name ?? ".ctor",
                expression.ToString(),
                RoslynSymbolFormatter.GetMethodFullName(methodSymbol),
                RoslynSymbolFormatter.GetMethodSignature(methodSymbol),
                typeFullName,
                FrontendGraphConventions.StaticDispatch,
                RoslynSymbolFormatter.GetSymbolId(methodSymbol)?.Value,
                primitiveBuilder.CreateOperationId(expression).Value,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, callNode);
        state.ReferencedTypeFullNames.Add(typeFullName);
        primitiveBuilder.AddExternalMethodStubIfNeeded(methodSymbol);

        int argumentIndex = 1;
        foreach (ArgumentSyntax argument in expression.ArgumentList?.Arguments ?? Enumerable.Empty<ArgumentSyntax>())
        {
            BuildExpression(argument.Expression, callNode.Id, fileNode);
            AddArgumentEdge(argument.Expression, callNode, argumentIndex);
            argumentIndex++;
        }
    }

    /// <summary>
    /// 构建匿名对象创建表达式。
    /// </summary>
    public void BuildAnonymousObjectCreation(
        AnonymousObjectCreationExpressionSyntax expression,
        long astParentId,
        CpgNode fileNode)
    {
        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);

        CpgNode callNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        NodeAssemblyConventions.ApplyCallNodeProperties(
            callNode,
            new CallNodeDescriptor(
                "anonymousObjectCreation",
                expression.ToString(),
                FrontendGraphConventions.Unknown,
                FrontendGraphConventions.Unknown,
                typeFullName,
                FrontendGraphConventions.DynamicDispatch,
                null,
                primitiveBuilder.CreateOperationId(expression).Value,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, callNode);
        state.ReferencedTypeFullNames.Add(typeFullName);

        CpgNode typeDeclNode = state.GraphBuilder.CreateNode(CpgNodeKind.TypeDecl);
        ExpressionAssemblyConventions.ApplyAnonymousTypeDeclProperties(
            typeDeclNode,
            typeSymbol?.Name ?? "<anonymous>",
            typeFullName,
            RoslynSymbolFormatter.GetSymbolId(typeSymbol)?.Value,
            callNode.Id,
            primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(typeDeclNode, expression.GetLocation().GetLineSpan());

        foreach (AnonymousObjectMemberDeclaratorSyntax initializer in expression.Initializers)
        {
            BuildAnonymousObjectMember(initializer, typeDeclNode, fileNode);
            BuildExpression(initializer.Expression, callNode.Id, fileNode);
        }
    }

    /// <summary>
    /// 构建二元表达式。
    /// </summary>
    public void BuildBinary(BinaryExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);

        CpgNode operatorNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        NodeAssemblyConventions.ApplyCallNodeProperties(
            operatorNode,
            new CallNodeDescriptor(
                expression.OperatorToken.Text,
                expression.ToString(),
                FrontendGraphConventions.Unknown,
                FrontendGraphConventions.Unknown,
                typeFullName,
                FrontendGraphConventions.DynamicDispatch,
                null,
                primitiveBuilder.CreateOperationId(expression).Value,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(operatorNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, operatorNode);
        state.ReferencedTypeFullNames.Add(typeFullName);

        BuildExpression(expression.Left, operatorNode.Id, fileNode);
        BuildExpression(expression.Right, operatorNode.Id, fileNode);
    }

    /// <summary>
    /// 构建赋值表达式。
    /// </summary>
    public void BuildAssignment(AssignmentExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        if (TryBuildPropertyCompoundAssignmentCall(expression, astParentId, fileNode))
        {
            return;
        }

        if (TryBuildPropertySetterCall(expression, astParentId, fileNode))
        {
            return;
        }

        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);

        CpgNode operatorNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        NodeAssemblyConventions.ApplyCallNodeProperties(
            operatorNode,
            new CallNodeDescriptor(
                expression.OperatorToken.Text,
                expression.ToString(),
                FrontendGraphConventions.Unknown,
                FrontendGraphConventions.Unknown,
                typeFullName,
                FrontendGraphConventions.DynamicDispatch,
                null,
                primitiveBuilder.CreateOperationId(expression).Value,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(operatorNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, operatorNode);
        state.ReferencedTypeFullNames.Add(typeFullName);

        BuildExpression(expression.Left, operatorNode.Id, fileNode);
        BuildExpression(expression.Right, operatorNode.Id, fileNode);
    }

    private bool TryBuildPropertyCompoundAssignmentCall(
        AssignmentExpressionSyntax expression,
        long astParentId,
        CpgNode fileNode)
    {
        if (expression.Kind() == SyntaxKind.SimpleAssignmentExpression)
        {
            return false;
        }

        string? operatorName = FrontendGraphConventions.TryGetCompoundAssignmentOperator(expression.Kind().ToString());
        if (string.IsNullOrWhiteSpace(operatorName))
        {
            return false;
        }

        ISymbol? symbol = state.Context.GetSymbolInfo(expression.Left).Symbol;
        if (symbol is IPropertySymbol propertySymbol)
        {
            return BuildResolvedPropertyCompoundAssignment(
                expression,
                propertySymbol,
                operatorName,
                astParentId,
                fileNode);
        }

        if (expression.Left is MemberAccessExpressionSyntax unresolvedMemberAccess)
        {
            return BuildFallbackPropertyCompoundAssignment(
                expression,
                unresolvedMemberAccess,
                operatorName,
                astParentId,
                fileNode);
        }

        return false;
    }

    private bool BuildResolvedPropertyCompoundAssignment(
        AssignmentExpressionSyntax expression,
        IPropertySymbol propertySymbol,
        string operatorName,
        long astParentId,
        CpgNode fileNode)
    {
        if (propertySymbol.GetMethod is null || propertySymbol.SetMethod is null)
        {
            return false;
        }

        CpgNode setterCallNode = CreateCallNode(
            expression,
            propertySymbol.SetMethod.Name,
            propertySymbol.SetMethod,
            astParentId,
            fileNode,
            propertySymbol.SetMethod.ReturnType);
        primitiveBuilder.RememberNode(expression, setterCallNode);

        if (expression.Left is MemberAccessExpressionSyntax memberAccess)
        {
            BuildExpression(memberAccess.Expression, setterCallNode.Id, fileNode);
        }

        CpgNode operatorNode = CreateSyntheticOperatorCall(
            expression,
            operatorName,
            setterCallNode.Id,
            fileNode,
            propertySymbol.Type);

        CpgNode getterCallNode = CreateCallNode(
            expression.Left,
            propertySymbol.GetMethod.Name,
            propertySymbol.GetMethod,
            operatorNode.Id,
            fileNode,
            propertySymbol.Type);
        if (expression.Left is MemberAccessExpressionSyntax getterMemberAccess)
        {
            BuildExpression(getterMemberAccess.Expression, getterCallNode.Id, fileNode);
        }

        BuildExpression(expression.Right, operatorNode.Id, fileNode);
        return true;
    }

    private bool BuildFallbackPropertyCompoundAssignment(
        AssignmentExpressionSyntax expression,
        MemberAccessExpressionSyntax memberAccess,
        string operatorName,
        long astParentId,
        CpgNode fileNode)
    {
        CpgNode setterCallNode = CreateFallbackPropertyCall(
            expression,
            FrontendGraphConventions.BuildPropertyAccessorName("set", memberAccess.Name.Identifier.ValueText),
            astParentId,
            fileNode);
        primitiveBuilder.RememberNode(expression, setterCallNode);
        BuildExpression(memberAccess.Expression, setterCallNode.Id, fileNode);

        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        CpgNode operatorNode = CreateSyntheticOperatorCall(
            expression,
            operatorName,
            setterCallNode.Id,
            fileNode,
            typeSymbol);

        CpgNode getterCallNode = CreateFallbackPropertyCall(
            expression.Left,
            FrontendGraphConventions.BuildPropertyAccessorName("get", memberAccess.Name.Identifier.ValueText),
            operatorNode.Id,
            fileNode);
        BuildExpression(memberAccess.Expression, getterCallNode.Id, fileNode);

        BuildExpression(expression.Right, operatorNode.Id, fileNode);
        return true;
    }

    /// <summary>
    /// 构建 this 表达式。
    /// </summary>
    public void BuildThis(ThisExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        BuildIdentifier(expression, "this", astParentId, fileNode);
    }

    /// <summary>
    /// 构建条件访问表达式。
    /// </summary>
    public void BuildConditionalAccess(ConditionalAccessExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, "?.", astParentId, fileNode);
        BuildExpression(expression.Expression, callNode.Id, fileNode);
        BuildExpression(expression.WhenNotNull, callNode.Id, fileNode);
    }

    /// <summary>
    /// 构建插值字符串表达式。
    /// </summary>
    public void BuildInterpolatedString(InterpolatedStringExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, FrontendGraphConventions.FormatStringOperator, astParentId, fileNode);

        foreach (InterpolatedStringContentSyntax content in expression.Contents)
        {
            if (content is InterpolationSyntax interpolation)
            {
                BuildExpression(interpolation.Expression, callNode.Id, fileNode);
            }
            else if (content is InterpolatedStringTextSyntax text)
            {
                CreateTextLiteral(text, callNode.Id, fileNode);
            }
        }
    }

    /// <summary>
    /// 构建显式数组创建表达式。
    /// </summary>
    public void BuildArrayCreation(ArrayCreationExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, FrontendGraphConventions.ArrayInitializerOperator, astParentId, fileNode);
        if (expression.Initializer is not null)
        {
            BuildInitializerElements(expression.Initializer, callNode.Id, fileNode);
        }
    }

    /// <summary>
    /// 构建隐式数组创建表达式。
    /// </summary>
    public void BuildImplicitArrayCreation(ImplicitArrayCreationExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, FrontendGraphConventions.ArrayInitializerOperator, astParentId, fileNode);
        BuildInitializerElements(expression.Initializer, callNode.Id, fileNode);
    }

    /// <summary>
    /// 构建初始化器表达式。
    /// </summary>
    public void BuildInitializer(InitializerExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, FrontendGraphConventions.ArrayInitializerOperator, astParentId, fileNode);
        BuildInitializerElements(expression, callNode.Id, fileNode);
    }

    /// <summary>
    /// 构建集合表达式。
    /// </summary>
    public void BuildCollectionExpression(CollectionExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, FrontendGraphConventions.CollectionInitializerOperator, astParentId, fileNode);

        foreach (CollectionElementSyntax element in expression.Elements)
        {
            if (element is ExpressionElementSyntax expressionElement)
            {
                BuildExpression(expressionElement.Expression, callNode.Id, fileNode);
            }
            else if (element is SpreadElementSyntax spreadElement)
            {
                BuildExpression(spreadElement.Expression, callNode.Id, fileNode);
            }
        }
    }

    /// <summary>
    /// 构建一元表达式。
    /// </summary>
    public void BuildUnary(ExpressionSyntax expression, string operatorName, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, operatorName, astParentId, fileNode);
        foreach (ExpressionSyntax childExpression in expression.ChildNodes().OfType<ExpressionSyntax>())
        {
            BuildExpression(childExpression, callNode.Id, fileNode);
        }
    }

    /// <summary>
    /// 构建 await 表达式。
    /// </summary>
    public void BuildAwait(AwaitExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, "await", astParentId, fileNode);
        BuildExpression(expression.Expression, callNode.Id, fileNode);
    }

    /// <summary>
    /// 构建三元条件表达式。
    /// </summary>
    public void BuildConditional(ConditionalExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, "?:", astParentId, fileNode);
        BuildExpression(expression.Condition, callNode.Id, fileNode);
        BuildExpression(expression.WhenTrue, callNode.Id, fileNode);
        BuildExpression(expression.WhenFalse, callNode.Id, fileNode);
    }

    /// <summary>
    /// 构建 switch 表达式。
    /// </summary>
    public void BuildSwitchExpression(SwitchExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, FrontendGraphConventions.SwitchExpressionOperator, astParentId, fileNode);
        BuildExpression(expression.GoverningExpression, callNode.Id, fileNode);

        foreach (SwitchExpressionArmSyntax arm in expression.Arms)
        {
            if (arm.WhenClause is not null)
            {
                BuildExpression(arm.WhenClause.Condition, callNode.Id, fileNode);
            }

            BuildExpression(arm.Expression, callNode.Id, fileNode);
        }
    }

    /// <summary>
    /// 构建模式匹配表达式。
    /// </summary>
    public void BuildIsPatternExpression(IsPatternExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, FrontendGraphConventions.IsPatternOperator, astParentId, fileNode);
        BuildExpression(expression.Expression, callNode.Id, fileNode);
    }

    /// <summary>
    /// 构建强制转换表达式。
    /// </summary>
    public void BuildCast(CastExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, FrontendGraphConventions.CastOperator, astParentId, fileNode);
        ExpressionAssemblyConventions.ApplyTargetTypeFullName(
            callNode,
            RoslynSymbolFormatter.GetTypeFullName(state.Context.GetTypeInfo(expression.Type).Type));
        BuildExpression(expression.Expression, callNode.Id, fileNode);
    }

    /// <summary>
    /// 构建下标访问表达式。
    /// </summary>
    public void BuildElementAccess(ElementAccessExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, "[]", astParentId, fileNode);
        BuildExpression(expression.Expression, callNode.Id, fileNode);

        int argumentIndex = 1;
        foreach (ArgumentSyntax argument in expression.ArgumentList.Arguments)
        {
            BuildExpression(argument.Expression, callNode.Id, fileNode);
            AddArgumentEdge(argument.Expression, callNode, argumentIndex);
            argumentIndex++;
        }
    }

    private void BuildSimpleLambda(SimpleLambdaExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        BuildLambdaCore(
            expression,
            new[] { expression.Parameter },
            expression.Body,
            astParentId,
            fileNode);
    }

    private void BuildParenthesizedLambda(ParenthesizedLambdaExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        BuildLambdaCore(
            expression,
            expression.ParameterList.Parameters,
            expression.Body,
            astParentId,
            fileNode);
    }

    private void BuildLambdaCore(
        LambdaExpressionSyntax expression,
        IReadOnlyList<ParameterSyntax> parameters,
        CSharpSyntaxNode body,
        long astParentId,
        CpgNode fileNode)
    {
        IMethodSymbol? lambdaSymbol = state.Context.GetSymbolInfo(expression).Symbol as IMethodSymbol;
        int ordinal = state.AllocateSyntheticMethodOrdinal();
        long methodParentId = FindMethodAstParentId(expression, astParentId);
        string returnTypeFullName = GetLambdaReturnTypeFullName(expression, lambdaSymbol);
        LambdaIdentity lambdaIdentity = ExpressionNodeConventions.BuildLambdaIdentity(
            ordinal,
            primitiveBuilder.GetStringProperty(fileNode, "FileName"),
            lambdaSymbol is not null ? RoslynSymbolFormatter.GetMethodFullName(lambdaSymbol) : null,
            lambdaSymbol is not null ? RoslynSymbolFormatter.GetMethodSignature(lambdaSymbol) : BuildLambdaSignature(parameters, expression),
            lambdaSymbol is not null ? RoslynSymbolFormatter.GetSymbolId(lambdaSymbol)?.Value : null,
            primitiveBuilder.CreateOperationId(expression).Value);

        CpgNode methodNode = state.GraphBuilder.CreateNode(CpgNodeKind.Method);
        MethodNodeConventions.ApplyMethodProperties(
            methodNode,
            new MethodNodeDescriptor(
                lambdaIdentity.Name,
                lambdaIdentity.FullName,
                lambdaIdentity.Signature,
                returnTypeFullName,
                lambdaIdentity.DeclaredSymbolId,
                FrontendGraphConventions.Unknown,
                false,
                false,
                false,
                methodParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(methodNode, expression.GetLocation().GetLineSpan());

        foreach ((ParameterSyntax parameter, int index) in parameters.Select((item, index) => (item, index)))
        {
            IParameterSymbol? parameterSymbol = state.Context.GetDeclaredSymbol(parameter) as IParameterSymbol;
            CpgNode parameterNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodParameterIn);
            MethodNodeConventions.ApplyMethodParameterOrder(parameterNode, parameter.Identifier.ValueText, index + 1);
            primitiveBuilder.WriteDeclarationProperties(parameterNode, parameterSymbol, parameterSymbol?.Type, methodNode.Id, fileNode);
            state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(parameterSymbol?.Type));
        }

        CpgNode returnNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodReturn);
        MethodNodeConventions.ApplyMethodReturnProperties(returnNode, returnTypeFullName, methodNode.Id, parameters.Count + 1);

        if (body is BlockSyntax block)
        {
            StatementBuilder statementBuilder = new(state, primitiveBuilder, this);
            statementBuilder.BuildBlock(block, methodNode.Id, fileNode);
        }
        else if (body is ExpressionSyntax bodyExpression)
        {
            CpgNode lambdaReturnNode = primitiveBuilder.CreateControlNode(
                bodyExpression,
                methodNode.Id,
                FrontendControlFlowConventions.Return);
            BuildExpression(bodyExpression, lambdaReturnNode.Id, fileNode);
        }

        CpgNode methodRefNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodRef);
        ExpressionAssemblyConventions.ApplyMethodRefProperties(
            methodRefNode,
            lambdaIdentity.Name,
            expression.ToString(),
            lambdaIdentity.FullName,
            lambdaIdentity.Signature,
            lambdaIdentity.DeclaredSymbolId,
            astParentId,
            primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(methodRefNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, methodRefNode);
    }

    private bool TryBuildMethodReference(ExpressionSyntax expression, string name, long astParentId, CpgNode fileNode)
    {
        if (IsInvocationTarget(expression))
        {
            return false;
        }

        IMethodSymbol? methodSymbol = GetBestMethodSymbol(state.Context.GetSymbolInfo(expression));
        if (methodSymbol is null)
        {
            return false;
        }

        CpgNode methodRefNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodRef);
        ExpressionAssemblyConventions.ApplyMethodRefProperties(
            methodRefNode,
            name,
            expression.ToString(),
            RoslynSymbolFormatter.GetMethodFullName(methodSymbol),
            RoslynSymbolFormatter.GetMethodSignature(methodSymbol),
            RoslynSymbolFormatter.GetSymbolId(methodSymbol)?.Value,
            astParentId,
            primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(methodRefNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, methodRefNode);
        primitiveBuilder.AddExternalMethodStubIfNeeded(methodSymbol);
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(methodSymbol.ReturnType));
        return true;
    }

    private bool TryBuildPropertyGetterCall(ExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        if (IsAssignmentLeft(expression))
        {
            return false;
        }

        ISymbol? symbol = state.Context.GetSymbolInfo(expression).Symbol;
        if (symbol is IPropertySymbol propertySymbol && propertySymbol.GetMethod is not null)
        {
            CpgNode callNode = CreateCallNode(
                expression,
                propertySymbol.GetMethod.Name,
                propertySymbol.GetMethod,
                astParentId,
                fileNode,
                propertySymbol.Type);
            primitiveBuilder.RememberNode(expression, callNode);

            if (expression is MemberAccessExpressionSyntax memberAccess)
            {
                BuildExpression(memberAccess.Expression, callNode.Id, fileNode);
            }

            return true;
        }

        return false;
    }

    private bool TryBuildPropertySetterCall(AssignmentExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        ISymbol? symbol = state.Context.GetSymbolInfo(expression.Left).Symbol;
        if (symbol is IPropertySymbol propertySymbol && propertySymbol.SetMethod is not null)
        {
            CpgNode callNode = CreateCallNode(
                expression,
                propertySymbol.SetMethod.Name,
                propertySymbol.SetMethod,
                astParentId,
                fileNode,
                propertySymbol.SetMethod.ReturnType);
            primitiveBuilder.RememberNode(expression, callNode);

            if (expression.Left is MemberAccessExpressionSyntax memberAccess)
            {
                BuildExpression(memberAccess.Expression, callNode.Id, fileNode);
            }

            BuildExpression(expression.Right, callNode.Id, fileNode);
            return true;
        }

        if (expression.Left is not MemberAccessExpressionSyntax unresolvedMemberAccess ||
            symbol is not null)
        {
            return false;
        }

        CpgNode fallbackCallNode = CreateFallbackPropertyCall(
            expression,
            FrontendGraphConventions.BuildPropertyAccessorName("set", unresolvedMemberAccess.Name.Identifier.ValueText),
            astParentId,
            fileNode);
        primitiveBuilder.RememberNode(expression, fallbackCallNode);

        BuildExpression(unresolvedMemberAccess.Expression, fallbackCallNode.Id, fileNode);
        BuildExpression(expression.Right, fallbackCallNode.Id, fileNode);
        return true;
    }

    private CpgNode CreateFallbackPropertyCall(
        SyntaxNode expression,
        string name,
        long astParentId,
        CpgNode fileNode)
    {
        CpgNode callNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        FallbackCallMetadata metadata = ExpressionNodeConventions.BuildFallbackCallMetadata();
        NodeAssemblyConventions.ApplyCallNodeProperties(
            callNode,
            new CallNodeDescriptor(
                name,
                expression.ToString(),
                metadata.MethodFullName,
                metadata.Signature,
                metadata.TypeFullName,
                metadata.DispatchType,
                null,
                primitiveBuilder.CreateOperationId(expression).Value,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        return callNode;
    }

    private CpgNode CreateSyntheticOperatorCall(
        AssignmentExpressionSyntax expression,
        string operatorName,
        long astParentId,
        CpgNode fileNode,
        ITypeSymbol? typeSymbol)
    {
        CpgNode operatorNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);
        NodeAssemblyConventions.ApplyCallNodeProperties(
            operatorNode,
            new CallNodeDescriptor(
                operatorName,
                expression.ToString(),
                FrontendGraphConventions.Unknown,
                FrontendGraphConventions.Unknown,
                typeFullName,
                FrontendGraphConventions.DynamicDispatch,
                null,
                primitiveBuilder.CreateOperationId(expression).Value,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(operatorNode, expression.GetLocation().GetLineSpan());
        state.ReferencedTypeFullNames.Add(typeFullName);
        return operatorNode;
    }

    private CpgNode CreateCallNode(
        SyntaxNode expression,
        string name,
        IMethodSymbol methodSymbol,
        long astParentId,
        CpgNode fileNode,
        ITypeSymbol? typeSymbol)
    {
        CpgNode callNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);
        NodeAssemblyConventions.ApplyCallNodeProperties(
            callNode,
            new CallNodeDescriptor(
                name,
                expression.ToString(),
                RoslynSymbolFormatter.GetMethodFullName(methodSymbol),
                RoslynSymbolFormatter.GetMethodSignature(methodSymbol),
                typeFullName,
                FrontendGraphConventions.GetDispatchType(methodSymbol.IsStatic, methodSymbol.ReducedFrom is not null),
                RoslynSymbolFormatter.GetSymbolId(methodSymbol)?.Value,
                primitiveBuilder.CreateOperationId(expression).Value,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.AddExternalMethodStubIfNeeded(methodSymbol);
        state.ReferencedTypeFullNames.Add(typeFullName);
        return callNode;
    }

    private void BuildAnonymousObjectMember(
        AnonymousObjectMemberDeclaratorSyntax initializer,
        CpgNode typeDeclNode,
        CpgNode fileNode)
    {
        string memberName = AnonymousObjectConventions.BuildMemberName(
            initializer.NameEquals?.Name.Identifier.ValueText,
            initializer.Expression is IdentifierNameSyntax identifierName ? identifierName.Identifier.ValueText : null,
            initializer.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Name.Identifier.ValueText : null,
            initializer.Expression.ToString());
        TypeInfo typeInfo = state.Context.GetTypeInfo(initializer.Expression);
        ITypeSymbol? memberType = typeInfo.Type ?? typeInfo.ConvertedType;

        CpgNode memberNode = state.GraphBuilder.CreateNode(CpgNodeKind.Member);
        DeclarationAssemblyConventions.ApplyMemberIdentity(
            memberNode,
            memberName,
            FrontendGraphConventions.BuildMemberFullName(
                primitiveBuilder.GetStringProperty(typeDeclNode, "FullName"),
                memberName),
            AnonymousObjectConventions.AnonymousObjectMemberSource);
        primitiveBuilder.WriteDeclarationProperties(memberNode, null, memberType, typeDeclNode.Id, fileNode);
        primitiveBuilder.SetLocation(memberNode, initializer.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(initializer, memberNode);
    }

    private CpgNode CreateOperatorCallNode(ExpressionSyntax expression, string operatorName, long astParentId, CpgNode fileNode)
    {
        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;

        CpgNode callNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        string typeFullName = RoslynSymbolFormatter.GetTypeFullName(typeSymbol);
        NodeAssemblyConventions.ApplyCallNodeProperties(
            callNode,
            new CallNodeDescriptor(
                operatorName,
                expression.ToString(),
                FrontendGraphConventions.Unknown,
                FrontendGraphConventions.Unknown,
                typeFullName,
                FrontendGraphConventions.DynamicDispatch,
                null,
                primitiveBuilder.CreateOperationId(expression).Value,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, callNode);
        state.ReferencedTypeFullNames.Add(typeFullName);
        return callNode;
    }

    private void BuildInitializerElements(InitializerExpressionSyntax initializer, long astParentId, CpgNode fileNode)
    {
        foreach (ExpressionSyntax childExpression in initializer.Expressions)
        {
            BuildExpression(childExpression, astParentId, fileNode);
        }
    }

    private void AddArgumentEdge(ExpressionSyntax argumentExpression, CpgNode callNode, int argumentIndex)
    {
        if (!state.NodeIdsBySyntax.TryGetValue(argumentExpression, out long argumentNodeId))
        {
            return;
        }

        CpgNode argumentNode = state.GraphBuilder.Graph.GetNode(argumentNodeId);
        ExpressionAssemblyConventions.ApplyArgumentIndex(argumentNode, argumentIndex);
        EnsureEdge(callNode.Id, argumentNode.Id, CpgEdgeKind.Argument);
    }

    private void AddReceiverEdge(ExpressionSyntax invocationTarget, CpgNode callNode, CpgNode fileNode)
    {
        ExpressionSyntax? receiverExpression = invocationTarget switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            ConditionalAccessExpressionSyntax conditionalAccess => conditionalAccess.Expression,
            MemberBindingExpressionSyntax => null,
            _ => null,
        };

        if (receiverExpression is null)
        {
            return;
        }

        if (!state.NodeIdsBySyntax.TryGetValue(receiverExpression, out long receiverNodeId))
        {
            BuildExpression(receiverExpression, callNode.Id, fileNode);
        }

        if (!state.NodeIdsBySyntax.TryGetValue(receiverExpression, out receiverNodeId))
        {
            return;
        }

        EnsureEdge(callNode.Id, receiverNodeId, CpgEdgeKind.Receiver);
    }

    private void AddReceiverEdgeFromInvocationText(ExpressionSyntax invocationTarget, CpgNode callNode, CpgNode fileNode)
    {
        if (state.GraphBuilder.Graph.GetOutgoingEdges(callNode.Id, CpgEdgeKind.Receiver).Any())
        {
            return;
        }

        InvocationReceiverInfo? receiverInfo =
            FrontendGraphConventions.TryParseReceiverFromInvocationText(invocationTarget.ToString());
        if (receiverInfo is null)
        {
            return;
        }

        CpgNode receiverNode = state.GraphBuilder.CreateNode(CpgNodeKind.Identifier);
        ExpressionAssemblyConventions.ApplyReceiverIdentifierProperties(
            receiverNode,
            receiverInfo.Name,
            receiverInfo.Code,
            callNode.Id,
            primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(receiverNode, invocationTarget.GetLocation().GetLineSpan());
        EnsureEdge(callNode.Id, receiverNode.Id, CpgEdgeKind.Receiver);
    }

    private void EnsureEdge(long sourceId, long targetId, CpgEdgeKind edgeKind)
    {
        bool exists = state.GraphBuilder.Graph.GetOutgoingEdges(sourceId, edgeKind)
            .Any(edge => edge.TargetId == targetId);
        if (!exists)
        {
            state.GraphBuilder.AddEdge(sourceId, targetId, edgeKind);
        }
    }

    private void CreateTextLiteral(InterpolatedStringTextSyntax text, long astParentId, CpgNode fileNode)
    {
        CpgNode literalNode = state.GraphBuilder.CreateNode(CpgNodeKind.Literal);
        ExpressionAssemblyConventions.ApplyLiteralProperties(
            literalNode,
            text.TextToken.ValueText,
            text.TextToken.Text,
            FrontendGraphConventions.StringTypeFullName,
            astParentId,
            primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(literalNode, text.GetLocation().GetLineSpan());
        state.NodeIdsBySyntax[text] = literalNode.Id;
        state.ReferencedTypeFullNames.Add(FrontendGraphConventions.StringTypeFullName);
    }

    private bool IsInvocationTarget(ExpressionSyntax expression)
    {
        return expression.Parent is InvocationExpressionSyntax invocation &&
               ReferenceEquals(invocation.Expression, expression);
    }

    private bool IsAssignmentLeft(ExpressionSyntax expression)
    {
        return expression.Parent is AssignmentExpressionSyntax assignment &&
               ReferenceEquals(assignment.Left, expression);
    }

    private long FindMethodAstParentId(SyntaxNode syntaxNode, long fallbackAstParentId)
    {
        foreach (SyntaxNode ancestor in syntaxNode.Ancestors())
        {
            if (state.NodeIdsBySyntax.TryGetValue(ancestor, out long nodeId))
            {
                CpgNode parentNode = state.GraphBuilder.Graph.GetNode(nodeId);
                if (parentNode.Kind == CpgNodeKind.Method)
                {
                    return nodeId;
                }
            }
        }

        return fallbackAstParentId;
    }

    private string BuildLambdaSignature(IReadOnlyList<ParameterSyntax> parameters, LambdaExpressionSyntax expression)
    {
        return ExpressionNodeConventions.BuildLambdaFallbackSignature(
            GetLambdaReturnTypeFullName(expression, null),
            parameters.Select(parameter =>
            {
                IParameterSymbol? parameterSymbol = state.Context.GetDeclaredSymbol(parameter) as IParameterSymbol;
                return RoslynSymbolFormatter.GetTypeFullName(parameterSymbol?.Type);
            }));
    }

    private string GetLambdaReturnTypeFullName(LambdaExpressionSyntax expression, IMethodSymbol? lambdaSymbol)
    {
        if (lambdaSymbol is not null)
        {
            return RoslynSymbolFormatter.GetTypeFullName(lambdaSymbol.ReturnType);
        }

        ITypeSymbol? convertedType = state.Context.GetTypeInfo(expression).ConvertedType;
        if (convertedType is INamedTypeSymbol delegateType &&
            delegateType.DelegateInvokeMethod is IMethodSymbol invokeMethod)
        {
            return RoslynSymbolFormatter.GetTypeFullName(invokeMethod.ReturnType);
        }

        return FrontendGraphConventions.Unknown;
    }

    private bool TryBuildFieldAccess(
        MemberAccessExpressionSyntax expression,
        long astParentId,
        CpgNode fileNode)
    {
        ISymbol? symbol = state.Context.GetSymbolInfo(expression).Symbol;
        if (symbol is IFieldSymbol fieldSymbol)
        {
            CpgNode resolvedCallNode = CreateOperatorCallNode(expression, ".", astParentId, fileNode);
            ExpressionAssemblyConventions.ApplyFieldAccessProperties(
                resolvedCallNode,
                FrontendGraphConventions.BuildMemberFullName(
                    RoslynSymbolFormatter.GetTypeFullName(fieldSymbol.ContainingType),
                    fieldSymbol.Name),
                RoslynSymbolFormatter.GetSymbolId(fieldSymbol)?.Value);

            BuildExpression(expression.Expression, resolvedCallNode.Id, fileNode);
            BuildIdentifier(expression.Name, fieldSymbol.Name, resolvedCallNode.Id, fileNode);
            return true;
        }

        // 这里为 dynamic 或语义暂时拿不到成员符号的场景保底建模。
        // 如果此处直接退化成普通标识符，后续类型恢复阶段就失去“字段访问”抓手，
        // 无法按接收者类型去反查成员类型。
        if (symbol is not null || IsInvocationTarget(expression) || IsAssignmentLeft(expression))
        {
            return false;
        }

        CpgNode callNode = CreateOperatorCallNode(expression, ".", astParentId, fileNode);
        ExpressionAssemblyConventions.ApplyFieldAccessProperties(
            callNode,
            FrontendGraphConventions.BuildFallbackFieldFullName(
                expression.Expression.ToString(),
                expression.Name.Identifier.ValueText));

        BuildExpression(expression.Expression, callNode.Id, fileNode);
        BuildIdentifier(expression.Name, expression.Name.Identifier.ValueText, callNode.Id, fileNode);
        return true;
    }

    private static IMethodSymbol? GetBestMethodSymbol(SymbolInfo symbolInfo)
    {
        return symbolInfo.Symbol as IMethodSymbol ??
               symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

}
