using Domain.Analysis.Engine.Core;
using Infrastructure.Analysis.Engine.Frontend;
using Infrastructure.Analysis.Engine.Passes.ControlFlow;
using Logic.Analysis.Engine.Passes.ControlFlow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Infrastructure.Analysis.Engine.Frontend.Builders;

/// <summary>
/// 负责把语句节点投影成 CPG 节点。
/// </summary>
internal sealed class StatementBuilder
{
    private readonly BuilderState state;
    private readonly PrimitiveBuilder primitiveBuilder;
    private readonly ExpressionBuilder expressionBuilder;

    /// <summary>
    /// 初始化语句 Builder。
    /// </summary>
    public StatementBuilder(
        BuilderState state,
        PrimitiveBuilder primitiveBuilder,
        ExpressionBuilder expressionBuilder)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.primitiveBuilder = primitiveBuilder ?? throw new ArgumentNullException(nameof(primitiveBuilder));
        this.expressionBuilder = expressionBuilder ?? throw new ArgumentNullException(nameof(expressionBuilder));
    }

    /// <summary>
    /// 构建任意语句。
    /// </summary>
    public void BuildStatement(StatementSyntax statement, long astParentId, CpgNode fileNode)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(fileNode);

        switch (statement)
        {
            case LocalDeclarationStatementSyntax localDeclaration:
                BuildLocalDeclaration(localDeclaration, astParentId, fileNode);
                break;
            case ExpressionStatementSyntax expressionStatement:
                expressionBuilder.BuildExpression(expressionStatement.Expression, astParentId, fileNode);
                state.NodeIdsBySyntax[expressionStatement] = state.NodeIdsBySyntax[expressionStatement.Expression];
                break;
            case IfStatementSyntax ifStatement:
                BuildIf(ifStatement, astParentId, fileNode);
                break;
            case ReturnStatementSyntax returnStatement:
                BuildReturn(returnStatement, astParentId, fileNode);
                break;
            case WhileStatementSyntax whileStatement:
                BuildLoop(whileStatement, whileStatement.Condition, whileStatement.Statement, astParentId, fileNode, FrontendControlFlowConventions.While);
                break;
            case DoStatementSyntax doStatement:
                BuildLoop(doStatement, doStatement.Condition, doStatement.Statement, astParentId, fileNode, FrontendControlFlowConventions.Do);
                break;
            case ForStatementSyntax forStatement:
                BuildLoop(forStatement, forStatement.Condition, forStatement.Statement, astParentId, fileNode, FrontendControlFlowConventions.For);
                break;
            case ForEachStatementSyntax forEachStatement:
                BuildLoop(forEachStatement, forEachStatement.Expression, forEachStatement.Statement, astParentId, fileNode, FrontendControlFlowConventions.Foreach);
                break;
            case UsingStatementSyntax usingStatement:
                BuildUsing(usingStatement, astParentId, fileNode);
                break;
            case LocalFunctionStatementSyntax localFunction:
                BuildLocalFunction(localFunction, astParentId, fileNode);
                break;
            case SwitchStatementSyntax switchStatement:
                BuildSwitch(switchStatement, astParentId, fileNode);
                break;
            case TryStatementSyntax tryStatement:
                BuildTry(tryStatement, astParentId, fileNode);
                break;
            case ThrowStatementSyntax throwStatement:
                BuildThrow(throwStatement, astParentId, fileNode);
                break;
            case BreakStatementSyntax:
                primitiveBuilder.CreateControlNode(statement, astParentId, FrontendControlFlowConventions.Break);
                break;
            case ContinueStatementSyntax:
                primitiveBuilder.CreateControlNode(statement, astParentId, FrontendControlFlowConventions.Continue);
                break;
            case BlockSyntax block:
                BuildBlock(block, astParentId, fileNode);
                break;
            default:
                primitiveBuilder.CreateControlNode(
                    statement,
                    astParentId,
                    FrontendControlFlowConventions.BuildDefaultControlType(statement.Kind().ToString()));
                break;
        }
    }

    /// <summary>
    /// 构建语句块。
    /// </summary>
    public CpgNode BuildBlock(BlockSyntax block, long astParentId, CpgNode fileNode)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(fileNode);

        CpgNode blockNode = state.GraphBuilder.CreateNode(CpgNodeKind.Block);
        NodeAssemblyConventions.ApplyBlockNodeProperties(blockNode, FrontendControlFlowConventions.Block, astParentId);
        primitiveBuilder.SetLocation(blockNode, block.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(block, blockNode);

        foreach (StatementSyntax childStatement in block.Statements)
        {
            BuildStatement(childStatement, blockNode.Id, fileNode);
        }

        return blockNode;
    }

    private void BuildLocalDeclaration(LocalDeclarationStatementSyntax statement, long astParentId, CpgNode fileNode)
    {
        BuildVariableDeclaration(statement.Declaration, astParentId, fileNode);
        if (statement.Declaration.Variables.Count > 0)
        {
            state.NodeIdsBySyntax[statement] = state.NodeIdsBySyntax[statement.Declaration.Variables[0]];
        }
    }

    private void BuildVariableDeclaration(VariableDeclarationSyntax declaration, long astParentId, CpgNode fileNode)
    {
        foreach (VariableDeclaratorSyntax variable in declaration.Variables)
        {
            ILocalSymbol? localSymbol = state.Context.GetDeclaredSymbol(variable) as ILocalSymbol;
            CpgNode localNode = state.GraphBuilder.CreateNode(CpgNodeKind.Local);
            DeclarationAssemblyConventions.ApplyMemberIdentity(localNode, variable.Identifier.Text, variable.Identifier.Text);
            primitiveBuilder.WriteDeclarationProperties(localNode, localSymbol, localSymbol?.Type, astParentId, fileNode);
            primitiveBuilder.SetLocation(localNode, variable.GetLocation().GetLineSpan());
            state.NodeIdsBySyntax[variable] = localNode.Id;

            if (variable.Initializer is not null)
            {
                expressionBuilder.BuildExpression(variable.Initializer.Value, localNode.Id, fileNode);
            }
        }
    }

    private void BuildLocalFunction(LocalFunctionStatementSyntax statement, long astParentId, CpgNode fileNode)
    {
        IMethodSymbol? methodSymbol = state.Context.GetDeclaredSymbol(statement) as IMethodSymbol;
        CpgNode methodNode = state.GraphBuilder.CreateNode(CpgNodeKind.Method);
        string returnTypeFullName = RoslynSymbolFormatter.GetTypeFullName(methodSymbol?.ReturnType);
        MethodNodeConventions.ApplyMethodProperties(
            methodNode,
            new MethodNodeDescriptor(
                statement.Identifier.ValueText,
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
        primitiveBuilder.SetLocation(methodNode, statement.GetLocation().GetLineSpan());
        primitiveBuilder.RememberNode(statement, methodNode);
        state.ReferencedTypeFullNames.Add(returnTypeFullName);

        int order = 1;
        foreach (IParameterSymbol parameterSymbol in methodSymbol?.Parameters ?? Enumerable.Empty<IParameterSymbol>())
        {
            CpgNode parameterNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodParameterIn);
            MethodNodeConventions.ApplyMethodParameterOrder(parameterNode, parameterSymbol.Name, order);
            primitiveBuilder.WriteDeclarationProperties(parameterNode, parameterSymbol, parameterSymbol.Type, methodNode.Id, fileNode);
            order++;
        }

        CpgNode returnNode = state.GraphBuilder.CreateNode(CpgNodeKind.MethodReturn);
        MethodNodeConventions.ApplyMethodReturnProperties(returnNode, returnTypeFullName, methodNode.Id, order);

        if (statement.Body is not null)
        {
            BuildBlock(statement.Body, methodNode.Id, fileNode);
            ApplyCfg(statement.Body);
        }
        else if (statement.ExpressionBody is not null)
        {
            CpgNode localReturnNode = primitiveBuilder.CreateControlNode(
                statement.ExpressionBody.Expression,
                methodNode.Id,
                FrontendControlFlowConventions.Return);
            expressionBuilder.BuildExpression(statement.ExpressionBody.Expression, localReturnNode.Id, fileNode);
        }
    }

    private void BuildIf(IfStatementSyntax statement, long astParentId, CpgNode fileNode)
    {
        CpgNode ifNode = primitiveBuilder.CreateControlNode(statement, astParentId, FrontendControlFlowConventions.If);
        expressionBuilder.BuildExpression(statement.Condition, ifNode.Id, fileNode);
        BuildStatement(statement.Statement, ifNode.Id, fileNode);

        if (statement.Else is not null)
        {
            BuildStatement(statement.Else.Statement, ifNode.Id, fileNode);
        }
    }

    private void BuildReturn(ReturnStatementSyntax statement, long astParentId, CpgNode fileNode)
    {
        CpgNode returnNode = primitiveBuilder.CreateControlNode(statement, astParentId, FrontendControlFlowConventions.Return);
        if (statement.Expression is not null)
        {
            expressionBuilder.BuildExpression(statement.Expression, returnNode.Id, fileNode);
        }
    }

    private void BuildLoop(
        SyntaxNode statement,
        ExpressionSyntax? condition,
        StatementSyntax body,
        long astParentId,
        CpgNode fileNode,
        string controlType)
    {
        CpgNode loopNode = primitiveBuilder.CreateControlNode(statement, astParentId, controlType);

        if (condition is not null)
        {
            expressionBuilder.BuildExpression(condition, loopNode.Id, fileNode);
        }

        BuildStatement(body, loopNode.Id, fileNode);
        if (state.NodeIdsBySyntax.TryGetValue(body, out long bodyNodeId))
        {
            AppendNextCfgNodeId(loopNode, bodyNodeId);
        }
    }

    private void BuildSwitch(SwitchStatementSyntax statement, long astParentId, CpgNode fileNode)
    {
        CpgNode switchNode = primitiveBuilder.CreateControlNode(statement, astParentId, FrontendControlFlowConventions.Switch);
        expressionBuilder.BuildExpression(statement.Expression, switchNode.Id, fileNode);

        foreach (SwitchSectionSyntax section in statement.Sections)
        {
            CpgNode blockNode = state.GraphBuilder.CreateNode(CpgNodeKind.Block);
            NodeAssemblyConventions.ApplyBlockNodeProperties(blockNode, FrontendControlFlowConventions.SwitchSection, switchNode.Id);
            primitiveBuilder.SetLocation(blockNode, section.GetLocation().GetLineSpan());
            state.NodeIdsBySyntax[section] = blockNode.Id;

            foreach (StatementSyntax childStatement in section.Statements)
            {
                BuildStatement(childStatement, blockNode.Id, fileNode);
            }
        }
    }

    private void BuildTry(TryStatementSyntax statement, long astParentId, CpgNode fileNode)
    {
        CpgNode tryNode = primitiveBuilder.CreateControlNode(statement, astParentId, FrontendControlFlowConventions.Try);
        BuildBlock(statement.Block, tryNode.Id, fileNode);

        foreach (CatchClauseSyntax catchClause in statement.Catches)
        {
            CpgNode catchNode = primitiveBuilder.CreateControlNode(catchClause, tryNode.Id, FrontendControlFlowConventions.Catch);
            if (catchClause.Declaration is not null)
            {
                CpgNode localNode = state.GraphBuilder.CreateNode(CpgNodeKind.Local);
                DeclarationAssemblyConventions.ApplyMemberIdentity(
                    localNode,
                    catchClause.Declaration.Identifier.ValueText,
                    catchClause.Declaration.Identifier.ValueText);
                ITypeSymbol? catchType = state.Context.GetTypeInfo(catchClause.Declaration.Type).Type;
                primitiveBuilder.WriteDeclarationProperties(localNode, null, catchType, catchNode.Id, fileNode);
                primitiveBuilder.SetLocation(localNode, catchClause.Declaration.GetLocation().GetLineSpan());
                primitiveBuilder.RememberNode(catchClause.Declaration, localNode);
            }

            BuildBlock(catchClause.Block, catchNode.Id, fileNode);
        }

        if (statement.Finally is not null)
        {
            CpgNode finallyNode = primitiveBuilder.CreateControlNode(statement.Finally, tryNode.Id, FrontendControlFlowConventions.Finally);
            BuildBlock(statement.Finally.Block, finallyNode.Id, fileNode);
        }
    }

    private void BuildUsing(UsingStatementSyntax statement, long astParentId, CpgNode fileNode)
    {
        if (statement.Declaration is not null)
        {
            BuildVariableDeclaration(statement.Declaration, astParentId, fileNode);
        }
        else if (statement.Expression is not null)
        {
            expressionBuilder.BuildExpression(statement.Expression, astParentId, fileNode);
        }

        CpgNode tryNode = primitiveBuilder.CreateControlNode(statement, astParentId, FrontendControlFlowConventions.Try);
        BuildStatement(statement.Statement, tryNode.Id, fileNode);
        if (state.NodeIdsBySyntax.TryGetValue(statement.Statement, out long bodyNodeId))
        {
            AppendNextCfgNodeId(tryNode, bodyNodeId);
        }

        CpgNode finallyNode = primitiveBuilder.CreateControlNode(statement, tryNode.Id, FrontendControlFlowConventions.Finally);
        CpgNode finallyBlock = state.GraphBuilder.CreateNode(CpgNodeKind.Block);
        NodeAssemblyConventions.ApplyBlockNodeProperties(finallyBlock, FrontendControlFlowConventions.Block, finallyNode.Id);
        primitiveBuilder.SetLocation(finallyBlock, statement.GetLocation().GetLineSpan());

        CpgNode disposeCall = CreateDisposeCall(statement, finallyBlock.Id, fileNode);
        AppendNextCfgNodeId(finallyNode, disposeCall.Id);
    }

    private CpgNode CreateDisposeCall(UsingStatementSyntax statement, long astParentId, CpgNode fileNode)
    {
        ILocalSymbol? localSymbol = statement.Declaration?.Variables
            .Select(variable => state.Context.GetDeclaredSymbol(variable) as ILocalSymbol)
            .FirstOrDefault(symbol => symbol is not null);
        IMethodSymbol? disposeSymbol = localSymbol?.Type
            .GetMembers(FrontendGraphConventions.DisposeMethodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.Parameters.Length == 0);

        CpgNode callNode = state.GraphBuilder.CreateNode(CpgNodeKind.Call);
        NodeAssemblyConventions.ApplyCallNodeProperties(
            callNode,
            new CallNodeDescriptor(
                FrontendGraphConventions.DisposeMethodName,
                FrontendGraphConventions.BuildDisposeCallCode(localSymbol?.Name),
                RoslynSymbolFormatter.GetMethodFullName(disposeSymbol),
                RoslynSymbolFormatter.GetMethodSignature(disposeSymbol),
                RoslynSymbolFormatter.GetTypeFullName(disposeSymbol?.ReturnType),
                FrontendGraphConventions.DynamicDispatch,
                RoslynSymbolFormatter.GetSymbolId(disposeSymbol)?.Value,
                primitiveBuilder.CreateOperationId(statement).Value,
                astParentId,
                primitiveBuilder.GetStringProperty(fileNode, "FileName")));
        primitiveBuilder.SetLocation(callNode, statement.GetLocation().GetLineSpan());
        primitiveBuilder.AddExternalMethodStubIfNeeded(disposeSymbol);
        state.ReferencedTypeFullNames.Add(RoslynSymbolFormatter.GetTypeFullName(disposeSymbol?.ReturnType));
        return callNode;
    }

    private void AppendNextCfgNodeId(CpgNode source, long targetId)
    {
        GraphNodeConventions.AppendNextCfgNodeId(source, targetId);
    }

    private void BuildThrow(ThrowStatementSyntax statement, long astParentId, CpgNode fileNode)
    {
        CpgNode throwNode = primitiveBuilder.CreateControlNode(statement, astParentId, FrontendControlFlowConventions.Throw);
        if (statement.Expression is not null)
        {
            expressionBuilder.BuildExpression(statement.Expression, throwNode.Id, fileNode);
        }
    }

    private void ApplyCfg(BlockSyntax methodBody)
    {
        CfgBuilder cfgBuilder = new(state.NodeIdsBySyntax);
        CfgModel cfgModel = cfgBuilder.Build(methodBody);

        foreach ((long sourceId, long targetId) in cfgModel.Edges)
        {
            CpgNode source = state.GraphBuilder.Graph.GetNode(sourceId);
            AppendNextCfgNodeId(source, targetId);
        }
    }
}
