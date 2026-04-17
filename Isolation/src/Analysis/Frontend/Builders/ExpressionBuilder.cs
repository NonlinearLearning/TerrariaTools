using Analysis.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Analysis.Frontend.Builders;

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
        identifierNode.SetProperty("Name", name);
        identifierNode.SetProperty("Code", expression.ToString());
        identifierNode.SetProperty("TypeFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        identifierNode.SetProperty("TypeDeclFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        identifierNode.SetProperty("ReferencedSymbolId", RoslynSymbolFormatter.GetSymbolId(symbol)?.Value);
        identifierNode.SetProperty("AstParentId", astParentId);
        identifierNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(identifierNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, identifierNode);
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
    }

    /// <summary>
    /// 构建字面量。
    /// </summary>
    public void BuildLiteral(LiteralExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;

        CpgNode literalNode = state.GraphBuilder.CreateNode(CpgNodeKind.Literal);
        literalNode.SetProperty("Name", expression.Token.ValueText);
        literalNode.SetProperty("Code", expression.Token.Text);
        literalNode.SetProperty("TypeFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        literalNode.SetProperty("TypeDeclFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        literalNode.SetProperty("AstParentId", astParentId);
        literalNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(literalNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, literalNode);
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
    }

    /// <summary>
    /// 构建方法调用。
    /// </summary>
    public void BuildInvocation(InvocationExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        IMethodSymbol? methodSymbol = GetBestMethodSymbol(state.Context.GetSymbolInfo(expression));
        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;

        CpgNode callNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        callNode.SetProperty("Name", methodSymbol?.Name ?? expression.Expression.ToString());
        callNode.SetProperty("Code", expression.ToString());
        callNode.SetProperty("MethodFullName", RoslynSymbolFormatter.GetMethodFullName(methodSymbol));
        callNode.SetProperty("Signature", RoslynSymbolFormatter.GetMethodSignature(methodSymbol));
        callNode.SetProperty("TypeFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        callNode.SetProperty("DispatchType", IsStaticDispatch(methodSymbol) ? "STATIC_DISPATCH" : "DYNAMIC_DISPATCH");
        callNode.SetProperty("ReferencedSymbolId", RoslynSymbolFormatter.GetSymbolId(methodSymbol)?.Value);
        callNode.SetProperty("OperationId", primitiveBuilder.CreateOperationId(expression).Value);
        callNode.SetProperty("AstParentId", astParentId);
        callNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, callNode);
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
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

        CpgNode callNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        callNode.SetProperty("Name", methodSymbol?.Name ?? ".ctor");
        callNode.SetProperty("Code", expression.ToString());
        callNode.SetProperty("MethodFullName", RoslynSymbolFormatter.GetMethodFullName(methodSymbol));
        callNode.SetProperty("Signature", RoslynSymbolFormatter.GetMethodSignature(methodSymbol));
        callNode.SetProperty("TypeFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        callNode.SetProperty("DispatchType", "STATIC_DISPATCH");
        callNode.SetProperty("ReferencedSymbolId", RoslynSymbolFormatter.GetSymbolId(methodSymbol)?.Value);
        callNode.SetProperty("OperationId", primitiveBuilder.CreateOperationId(expression).Value);
        callNode.SetProperty("AstParentId", astParentId);
        callNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, callNode);
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
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
        callNode.SetProperty("Name", "anonymousObjectCreation");
        callNode.SetProperty("Code", expression.ToString());
        callNode.SetProperty("TypeFullName", typeFullName);
        callNode.SetProperty("OperationId", primitiveBuilder.CreateOperationId(expression).Value);
        callNode.SetProperty("AstParentId", astParentId);
        callNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, callNode);
        state.ReferencedTypeFullNames.Add(typeFullName);

        CpgNode typeDeclNode = state.GraphBuilder.CreateNode(CpgNodeKind.TypeDecl);
        typeDeclNode.SetProperty("Name", typeSymbol?.Name ?? "<anonymous>");
        typeDeclNode.SetProperty("FullName", typeFullName);
        typeDeclNode.SetProperty("TypeFullName", typeFullName);
        typeDeclNode.SetProperty("DeclaredSymbolId", RoslynSymbolFormatter.GetSymbolId(typeSymbol)?.Value);
        typeDeclNode.SetProperty("IsAnonymous", "true");
        typeDeclNode.SetProperty("AstParentId", callNode.Id);
        typeDeclNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
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

        CpgNode operatorNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        operatorNode.SetProperty("Name", expression.OperatorToken.Text);
        operatorNode.SetProperty("Code", expression.ToString());
        operatorNode.SetProperty("TypeFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        operatorNode.SetProperty("OperationId", primitiveBuilder.CreateOperationId(expression).Value);
        operatorNode.SetProperty("AstParentId", astParentId);
        operatorNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(operatorNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, operatorNode);
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(typeSymbol));

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

        CpgNode operatorNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        operatorNode.SetProperty("Name", expression.OperatorToken.Text);
        operatorNode.SetProperty("Code", expression.ToString());
        operatorNode.SetProperty("TypeFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        operatorNode.SetProperty("OperationId", primitiveBuilder.CreateOperationId(expression).Value);
        operatorNode.SetProperty("AstParentId", astParentId);
        operatorNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(operatorNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, operatorNode);
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(typeSymbol));

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

        string? operatorName = expression.Kind() switch
        {
            SyntaxKind.AddAssignmentExpression => "+",
            SyntaxKind.SubtractAssignmentExpression => "-",
            SyntaxKind.MultiplyAssignmentExpression => "*",
            SyntaxKind.DivideAssignmentExpression => "/",
            SyntaxKind.ModuloAssignmentExpression => "%",
            SyntaxKind.AndAssignmentExpression => "&",
            SyntaxKind.OrAssignmentExpression => "|",
            SyntaxKind.ExclusiveOrAssignmentExpression => "^",
            SyntaxKind.LeftShiftAssignmentExpression => "<<",
            SyntaxKind.RightShiftAssignmentExpression => ">>",
            _ => null,
        };
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
            $"set_{memberAccess.Name.Identifier.ValueText}",
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
            $"get_{memberAccess.Name.Identifier.ValueText}",
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
        CpgNode callNode = CreateOperatorCallNode(expression, "formatString", astParentId, fileNode);

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
        CpgNode callNode = CreateOperatorCallNode(expression, "arrayInitializer", astParentId, fileNode);
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
        CpgNode callNode = CreateOperatorCallNode(expression, "arrayInitializer", astParentId, fileNode);
        BuildInitializerElements(expression.Initializer, callNode.Id, fileNode);
    }

    /// <summary>
    /// 构建初始化器表达式。
    /// </summary>
    public void BuildInitializer(InitializerExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, "arrayInitializer", astParentId, fileNode);
        BuildInitializerElements(expression, callNode.Id, fileNode);
    }

    /// <summary>
    /// 构建集合表达式。
    /// </summary>
    public void BuildCollectionExpression(CollectionExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, "collectionInitializer", astParentId, fileNode);

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
        CpgNode callNode = CreateOperatorCallNode(expression, "switchExpression", astParentId, fileNode);
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
        CpgNode callNode = CreateOperatorCallNode(expression, "isPattern", astParentId, fileNode);
        BuildExpression(expression.Expression, callNode.Id, fileNode);
    }

    /// <summary>
    /// 构建强制转换表达式。
    /// </summary>
    public void BuildCast(CastExpressionSyntax expression, long astParentId, CpgNode fileNode)
    {
        CpgNode callNode = CreateOperatorCallNode(expression, "cast", astParentId, fileNode);
        callNode.SetProperty("TargetTypeFullName", RoslynSymbolFormatter.GetTypeFullName(state.Context.GetTypeInfo(expression.Type).Type));
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
        string lambdaName = $"<lambda>{ordinal}";
        string lambdaFullName = lambdaSymbol is not null
            ? RoslynSymbolFormatter.GetMethodFullName(lambdaSymbol)
            : $"{primitiveBuilder.GetStringProperty(fileNode, "FileName")}::{lambdaName}";
        string lambdaSignature = lambdaSymbol is not null
            ? RoslynSymbolFormatter.GetMethodSignature(lambdaSymbol)
            : BuildLambdaSignature(parameters, expression);
        string declaredSymbolId = lambdaSymbol is not null
            ? RoslynSymbolFormatter.GetSymbolId(lambdaSymbol)?.Value ?? lambdaFullName
            : $"lambda:{primitiveBuilder.CreateOperationId(expression).Value}";

        CpgNode methodNode = state.GraphBuilder.CreateNode(CpgNodeKind.Method);
        methodNode.SetProperty("Name", lambdaName);
        methodNode.SetProperty("FullName", lambdaFullName);
        methodNode.SetProperty("Signature", lambdaSignature);
        methodNode.SetProperty("ReturnTypeFullName", GetLambdaReturnTypeFullName(expression, lambdaSymbol));
        methodNode.SetProperty("DeclaredSymbolId", declaredSymbolId);
        methodNode.SetProperty("AstParentId", methodParentId);
        methodNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(methodNode, expression.GetLocation().GetLineSpan());

        foreach ((ParameterSyntax parameter, int index) in parameters.Select((item, index) => (item, index)))
        {
            IParameterSymbol? parameterSymbol = state.Context.GetDeclaredSymbol(parameter) as IParameterSymbol;
            CpgNode parameterNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodParameterIn);
            parameterNode.SetProperty("Name", parameter.Identifier.ValueText);
            parameterNode.SetProperty("Index", index + 1);
            parameterNode.SetProperty("Order", index + 1);
            primitiveBuilder.WriteDeclarationProperties(parameterNode, parameterSymbol, parameterSymbol?.Type, methodNode.Id, fileNode);
            state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(parameterSymbol?.Type));
        }

        CpgNode returnNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodReturn);
        returnNode.SetProperty("TypeFullName", GetLambdaReturnTypeFullName(expression, lambdaSymbol));
        returnNode.SetProperty("TypeDeclFullName", GetLambdaReturnTypeFullName(expression, lambdaSymbol));
        returnNode.SetProperty("AstParentId", methodNode.Id);
        returnNode.SetProperty("Order", parameters.Count + 1);

        if (body is BlockSyntax block)
        {
            StatementBuilder statementBuilder = new(state, primitiveBuilder, this);
            statementBuilder.BuildBlock(block, methodNode.Id, fileNode);
        }
        else if (body is ExpressionSyntax bodyExpression)
        {
            CpgNode lambdaReturnNode = primitiveBuilder.CreateControlNode(bodyExpression, methodNode.Id, "RETURN");
            BuildExpression(bodyExpression, lambdaReturnNode.Id, fileNode);
        }

        CpgNode methodRefNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodRef);
        methodRefNode.SetProperty("Name", lambdaName);
        methodRefNode.SetProperty("Code", expression.ToString());
        methodRefNode.SetProperty("MethodFullName", lambdaFullName);
        methodRefNode.SetProperty("Signature", lambdaSignature);
        methodRefNode.SetProperty("ReferencedSymbolId", declaredSymbolId);
        methodRefNode.SetProperty("AstParentId", astParentId);
        methodRefNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
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
        methodRefNode.SetProperty("Name", name);
        methodRefNode.SetProperty("Code", expression.ToString());
        methodRefNode.SetProperty("MethodFullName", RoslynSymbolFormatter.GetMethodFullName(methodSymbol));
        methodRefNode.SetProperty("Signature", RoslynSymbolFormatter.GetMethodSignature(methodSymbol));
        methodRefNode.SetProperty("ReferencedSymbolId", RoslynSymbolFormatter.GetSymbolId(methodSymbol)?.Value);
        methodRefNode.SetProperty("AstParentId", astParentId);
        methodRefNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
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
            $"set_{unresolvedMemberAccess.Name.Identifier.ValueText}",
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
        callNode.SetProperty("Name", name);
        callNode.SetProperty("Code", expression.ToString());
        callNode.SetProperty("MethodFullName", "<unknown>");
        callNode.SetProperty("Signature", "<unknown>");
        callNode.SetProperty("TypeFullName", "<unknown>");
        callNode.SetProperty("DispatchType", "DYNAMIC_DISPATCH");
        callNode.SetProperty("OperationId", primitiveBuilder.CreateOperationId(expression).Value);
        callNode.SetProperty("AstParentId", astParentId);
        callNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
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
        operatorNode.SetProperty("Name", operatorName);
        operatorNode.SetProperty("Code", expression.ToString());
        operatorNode.SetProperty("TypeFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        operatorNode.SetProperty("OperationId", primitiveBuilder.CreateOperationId(expression).Value);
        operatorNode.SetProperty("AstParentId", astParentId);
        operatorNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(operatorNode, expression.GetLocation().GetLineSpan());
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
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
        callNode.SetProperty("Name", name);
        callNode.SetProperty("Code", expression.ToString());
        callNode.SetProperty("MethodFullName", RoslynSymbolFormatter.GetMethodFullName(methodSymbol));
        callNode.SetProperty("Signature", RoslynSymbolFormatter.GetMethodSignature(methodSymbol));
        callNode.SetProperty("TypeFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        callNode.SetProperty("DispatchType", methodSymbol.IsStatic ? "STATIC_DISPATCH" : "DYNAMIC_DISPATCH");
        callNode.SetProperty("ReferencedSymbolId", RoslynSymbolFormatter.GetSymbolId(methodSymbol)?.Value);
        callNode.SetProperty("OperationId", primitiveBuilder.CreateOperationId(expression).Value);
        callNode.SetProperty("AstParentId", astParentId);
        callNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.AddExternalMethodStubIfNeeded(methodSymbol);
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        return callNode;
    }

    private void BuildAnonymousObjectMember(
        AnonymousObjectMemberDeclaratorSyntax initializer,
        CpgNode typeDeclNode,
        CpgNode fileNode)
    {
        string memberName = initializer.NameEquals?.Name.Identifier.ValueText ??
                            initializer.Expression switch
                            {
                                IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
                                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                                _ => initializer.Expression.ToString(),
                            };
        TypeInfo typeInfo = state.Context.GetTypeInfo(initializer.Expression);
        ITypeSymbol? memberType = typeInfo.Type ?? typeInfo.ConvertedType;

        CpgNode memberNode = state.GraphBuilder.CreateNode(CpgNodeKind.Member);
        memberNode.SetProperty("Name", memberName);
        memberNode.SetProperty("FullName", $"{primitiveBuilder.GetStringProperty(typeDeclNode, "FullName")}.{memberName}");
        memberNode.SetProperty("Source", "AnonymousObjectMember");
        primitiveBuilder.WriteDeclarationProperties(memberNode, null, memberType, typeDeclNode.Id, fileNode);
        primitiveBuilder.SetLocation(memberNode, initializer.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(initializer, memberNode);
    }

    private CpgNode CreateOperatorCallNode(ExpressionSyntax expression, string operatorName, long astParentId, CpgNode fileNode)
    {
        TypeInfo typeInfo = state.Context.GetTypeInfo(expression);
        ITypeSymbol? typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;

        CpgNode callNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        callNode.SetProperty("Name", operatorName);
        callNode.SetProperty("Code", expression.ToString());
        callNode.SetProperty("TypeFullName", RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
        callNode.SetProperty("OperationId", primitiveBuilder.CreateOperationId(expression).Value);
        callNode.SetProperty("AstParentId", astParentId);
        callNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(callNode, expression.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(expression, callNode);
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(typeSymbol));
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
        argumentNode.SetProperty("ArgumentIndex", argumentIndex);
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

        string invocationText = invocationTarget.ToString();
        int separatorIndex = invocationText.LastIndexOf('.');
        if (separatorIndex <= 0)
        {
            return;
        }

        string receiverCode = invocationText[..separatorIndex];
        string receiverName = receiverCode.Split('.').Last();
        CpgNode receiverNode = state.GraphBuilder.CreateNode(CpgNodeKind.Identifier);
        receiverNode.SetProperty("Name", receiverName);
        receiverNode.SetProperty("Code", receiverCode);
        receiverNode.SetProperty("AstParentId", callNode.Id);
        receiverNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
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
        literalNode.SetProperty("Name", text.TextToken.ValueText);
        literalNode.SetProperty("Code", text.TextToken.Text);
        literalNode.SetProperty("TypeFullName", "string");
        literalNode.SetProperty("TypeDeclFullName", "string");
        literalNode.SetProperty("AstParentId", astParentId);
        literalNode.SetProperty("FileName", primitiveBuilder.GetStringProperty(fileNode, "FileName"));
        primitiveBuilder.SetLocation(literalNode, text.GetLocation().GetLineSpan());
        state.NodeIdsBySyntax[text] = literalNode.Id;
        state.ReferencedTypeFullNames.Add("string");
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
        string parameterTypes = string.Join(
            ", ",
            parameters.Select(parameter =>
            {
                IParameterSymbol? parameterSymbol = state.Context.GetDeclaredSymbol(parameter) as IParameterSymbol;
                return RoslynSymbolFormatter.GetTypeFullName(parameterSymbol?.Type);
            }));
        return $"{GetLambdaReturnTypeFullName(expression, null)} ({parameterTypes})";
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

        return "<unknown>";
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
            resolvedCallNode.SetProperty(
                "FieldFullName",
                $"{RoslynSymbolFormatter.GetTypeFullName(fieldSymbol.ContainingType)}.{fieldSymbol.Name}");
            resolvedCallNode.SetProperty("ReferencedSymbolId", RoslynSymbolFormatter.GetSymbolId(fieldSymbol)?.Value);

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
        callNode.SetProperty(
            "FieldFullName",
            $"{expression.Expression}.{expression.Name.Identifier.ValueText}");

        BuildExpression(expression.Expression, callNode.Id, fileNode);
        BuildIdentifier(expression.Name, expression.Name.Identifier.ValueText, callNode.Id, fileNode);
        return true;
    }

    private static IMethodSymbol? GetBestMethodSymbol(SymbolInfo symbolInfo)
    {
        return symbolInfo.Symbol as IMethodSymbol ??
               symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    private static bool IsStaticDispatch(IMethodSymbol? methodSymbol)
    {
        return methodSymbol?.IsStatic is true || methodSymbol?.ReducedFrom is not null;
    }
}
