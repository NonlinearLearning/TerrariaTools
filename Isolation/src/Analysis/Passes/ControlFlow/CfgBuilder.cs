using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Analysis.Passes.ControlFlow;

/// <summary>
/// 从方法体语法结构推导最小可用控制流。
///
/// 当前先覆盖阶段二最需要的主干语句。
/// 未覆盖语句会被当作普通语句处理，避免整条分析链断掉。
/// </summary>
public sealed class CfgBuilder
{
    private readonly IReadOnlyDictionary<SyntaxNode, long> nodeIdsBySyntax;

    /// <summary>
    /// 使用语法节点到图节点编号的映射初始化构建器。
    /// </summary>
    public CfgBuilder(IReadOnlyDictionary<SyntaxNode, long> nodeIdsBySyntax)
    {
        this.nodeIdsBySyntax = nodeIdsBySyntax ?? throw new ArgumentNullException(nameof(nodeIdsBySyntax));
    }

    /// <summary>
    /// 为方法体构建控制流片段。
    /// </summary>
    public CfgModel Build(BlockSyntax methodBody)
    {
        return BuildBlock(methodBody);
    }

    private CfgModel BuildBlock(BlockSyntax block)
    {
        CfgModel model = new();
        CfgModel? previous = null;

        foreach (StatementSyntax statement in block.Statements)
        {
            CfgModel current = BuildStatement(statement);
            if (current.EntryNodeId is null)
            {
                continue;
            }

            if (model.EntryNodeId is null)
            {
                model.EntryNodeId = current.EntryNodeId;
            }

            Append(model, current);

            if (previous is not null)
            {
                foreach (long exitNodeId in previous.ExitNodeIds)
                {
                    model.Edges.Add((exitNodeId, current.EntryNodeId.Value));
                }
            }

            previous = current;
        }

        if (previous is not null)
        {
            foreach (long exitNodeId in previous.ExitNodeIds)
            {
                model.ExitNodeIds.Add(exitNodeId);
            }
        }

        return model;
    }

    private CfgModel BuildStatement(StatementSyntax statement)
    {
        return statement switch
        {
            BlockSyntax blockSyntax => BuildBlock(blockSyntax),
            IfStatementSyntax ifStatement => BuildIf(ifStatement),
            WhileStatementSyntax whileStatement => BuildLoop(whileStatement, whileStatement.Statement),
            DoStatementSyntax doStatement => BuildDoLoop(doStatement),
            ForStatementSyntax forStatement => BuildLoop(forStatement, forStatement.Statement),
            ForEachStatementSyntax forEachStatement => BuildLoop(forEachStatement, forEachStatement.Statement),
            UsingStatementSyntax usingStatement => BuildUsing(usingStatement),
            SwitchStatementSyntax switchStatement => BuildSwitch(switchStatement),
            TryStatementSyntax tryStatement => BuildTry(tryStatement),
            LocalFunctionStatementSyntax => new CfgModel(),
            ReturnStatementSyntax returnStatement => BuildTerminal(returnStatement, "return"),
            ThrowStatementSyntax throwStatement => BuildTerminal(throwStatement, "throw"),
            BreakStatementSyntax breakStatement => BuildTerminal(breakStatement, "break"),
            ContinueStatementSyntax continueStatement => BuildTerminal(continueStatement, "continue"),
            _ => BuildSimple(statement),
        };
    }

    private CfgModel BuildIf(IfStatementSyntax statement)
    {
        long ifNodeId = GetNodeId(statement);
        CfgModel model = new() { EntryNodeId = ifNodeId };
        CfgModel thenModel = BuildStatement(statement.Statement);

        Append(model, thenModel);
        model.Edges.Add((ifNodeId, thenModel.EntryNodeId ?? ifNodeId));

        if (statement.Else is not null)
        {
            CfgModel elseModel = BuildStatement(statement.Else.Statement);
            Append(model, elseModel);
            model.Edges.Add((ifNodeId, elseModel.EntryNodeId ?? ifNodeId));
        }
        else
        {
            model.ExitNodeIds.Add(ifNodeId);
        }

        return model;
    }

    private CfgModel BuildSwitch(SwitchStatementSyntax statement)
    {
        long switchNodeId = GetNodeId(statement);
        CfgModel model = new() { EntryNodeId = switchNodeId };

        foreach (SwitchSectionSyntax section in statement.Sections)
        {
            CfgModel sectionModel = BuildSwitchSection(section);
            Append(model, sectionModel);

            if (sectionModel.EntryNodeId is not null)
            {
                model.Edges.Add((switchNodeId, sectionModel.EntryNodeId.Value));
            }
        }

        model.ExitNodeIds.Add(switchNodeId);
        foreach (long breakNodeId in model.PendingBreaks.ToArray())
        {
            model.ExitNodeIds.Add(breakNodeId);
        }

        model.PendingBreaks.Clear();
        return model;
    }

    private CfgModel BuildSwitchSection(SwitchSectionSyntax section)
    {
        if (nodeIdsBySyntax.TryGetValue(section, out long sectionNodeId))
        {
            CfgModel model = new() { EntryNodeId = sectionNodeId };
            CfgModel? previous = null;

            foreach (StatementSyntax statement in section.Statements)
            {
                CfgModel current = BuildStatement(statement);
                Append(model, current);

                if (current.EntryNodeId is not null)
                {
                    if (previous is null)
                    {
                        model.Edges.Add((sectionNodeId, current.EntryNodeId.Value));
                    }
                    else
                    {
                        foreach (long exitNodeId in previous.ExitNodeIds)
                        {
                            model.Edges.Add((exitNodeId, current.EntryNodeId.Value));
                        }
                    }
                }

                previous = current;
            }

            if (previous is null)
            {
                model.ExitNodeIds.Add(sectionNodeId);
            }
            else
            {
                model.ExitNodeIds.UnionWith(previous.ExitNodeIds);
            }

            return model;
        }

        return new CfgModel();
    }

    private CfgModel BuildTry(TryStatementSyntax statement)
    {
        long tryNodeId = GetNodeId(statement);
        CfgModel model = new() { EntryNodeId = tryNodeId };
        CfgModel tryBlockModel = BuildBlock(statement.Block);
        Append(model, tryBlockModel);

        if (tryBlockModel.EntryNodeId is not null)
        {
            model.Edges.Add((tryNodeId, tryBlockModel.EntryNodeId.Value));
        }

        List<CfgModel> catchModels = new();
        foreach (CatchClauseSyntax catchClause in statement.Catches)
        {
            CfgModel catchModel = BuildCatch(catchClause);
            catchModels.Add(catchModel);
            Append(model, catchModel);

            if (catchModel.EntryNodeId is not null)
            {
                model.Edges.Add((tryNodeId, catchModel.EntryNodeId.Value));
            }
        }

        CfgModel? finallyModel = statement.Finally is null ? null : BuildFinally(statement.Finally);
        if (finallyModel is not null)
        {
            Append(model, finallyModel);
            if (finallyModel.EntryNodeId is not null)
            {
                foreach (long exitNodeId in tryBlockModel.ExitNodeIds)
                {
                    model.Edges.Add((exitNodeId, finallyModel.EntryNodeId.Value));
                }

                foreach (CfgModel catchModel in catchModels)
                {
                    foreach (long exitNodeId in catchModel.ExitNodeIds)
                    {
                        model.Edges.Add((exitNodeId, finallyModel.EntryNodeId.Value));
                    }
                }

                model.ExitNodeIds.UnionWith(finallyModel.ExitNodeIds);
            }
        }
        else
        {
            model.ExitNodeIds.UnionWith(tryBlockModel.ExitNodeIds);
            foreach (CfgModel catchModel in catchModels)
            {
                model.ExitNodeIds.UnionWith(catchModel.ExitNodeIds);
            }
        }

        return model;
    }

    private CfgModel BuildCatch(CatchClauseSyntax catchClause)
    {
        long catchNodeId = GetNodeId(catchClause);
        CfgModel blockModel = BuildBlock(catchClause.Block);
        CfgModel model = new() { EntryNodeId = catchNodeId };
        Append(model, blockModel);

        if (blockModel.EntryNodeId is not null)
        {
            model.Edges.Add((catchNodeId, blockModel.EntryNodeId.Value));
        }
        else
        {
            model.ExitNodeIds.Add(catchNodeId);
        }

        return model;
    }

    private CfgModel BuildFinally(FinallyClauseSyntax finallyClause)
    {
        long finallyNodeId = GetNodeId(finallyClause);
        CfgModel blockModel = BuildBlock(finallyClause.Block);
        CfgModel model = new() { EntryNodeId = finallyNodeId };
        Append(model, blockModel);

        if (blockModel.EntryNodeId is not null)
        {
            model.Edges.Add((finallyNodeId, blockModel.EntryNodeId.Value));
        }
        else
        {
            model.ExitNodeIds.Add(finallyNodeId);
        }

        return model;
    }

    private CfgModel BuildLoop(SyntaxNode loopNode, StatementSyntax bodyStatement)
    {
        long loopNodeId = GetNodeId(loopNode);
        CfgModel bodyModel = BuildStatement(bodyStatement);
        CfgModel model = new() { EntryNodeId = loopNodeId };

        Append(model, bodyModel);
        model.Edges.Add((loopNodeId, bodyModel.EntryNodeId ?? loopNodeId));

        foreach (long exitNodeId in bodyModel.ExitNodeIds)
        {
            model.Edges.Add((exitNodeId, loopNodeId));
        }

        foreach (long continueNodeId in bodyModel.PendingContinues)
        {
            model.Edges.Add((continueNodeId, loopNodeId));
        }

        model.ExitNodeIds.Add(loopNodeId);
        foreach (long breakNodeId in bodyModel.PendingBreaks)
        {
            model.ExitNodeIds.Add(breakNodeId);
        }

        foreach (long returnNodeId in bodyModel.PendingReturns)
        {
            model.PendingReturns.Add(returnNodeId);
        }

        return model;
    }

    private CfgModel BuildDoLoop(DoStatementSyntax statement)
    {
        long doNodeId = GetNodeId(statement);
        CfgModel bodyModel = BuildStatement(statement.Statement);
        CfgModel model = new() { EntryNodeId = doNodeId };

        Append(model, bodyModel);
        model.Edges.Add((doNodeId, bodyModel.EntryNodeId ?? doNodeId));

        foreach (long exitNodeId in bodyModel.ExitNodeIds)
        {
            model.Edges.Add((exitNodeId, doNodeId));
        }

        foreach (long continueNodeId in bodyModel.PendingContinues)
        {
            model.Edges.Add((continueNodeId, doNodeId));
        }

        model.ExitNodeIds.Add(doNodeId);
        foreach (long breakNodeId in bodyModel.PendingBreaks)
        {
            model.ExitNodeIds.Add(breakNodeId);
        }

        foreach (long returnNodeId in bodyModel.PendingReturns)
        {
            model.PendingReturns.Add(returnNodeId);
        }

        return model;
    }

    private CfgModel BuildUsing(UsingStatementSyntax statement)
    {
        long tryNodeId = GetNodeId(statement);
        CfgModel bodyModel = BuildStatement(statement.Statement);
        CfgModel model = new() { EntryNodeId = tryNodeId };

        Append(model, bodyModel);
        model.Edges.Add((tryNodeId, bodyModel.EntryNodeId ?? tryNodeId));

        return model;
    }

    private CfgModel BuildTerminal(StatementSyntax statement, string terminalKind)
    {
        long nodeId = GetNodeId(statement);
        CfgModel model = new() { EntryNodeId = nodeId };

        if (string.Equals(terminalKind, "return", StringComparison.Ordinal))
        {
            model.PendingReturns.Add(nodeId);
        }
        else if (string.Equals(terminalKind, "break", StringComparison.Ordinal))
        {
            model.PendingBreaks.Add(nodeId);
        }
        else if (string.Equals(terminalKind, "continue", StringComparison.Ordinal))
        {
            model.PendingContinues.Add(nodeId);
        }

        return model;
    }

    private CfgModel BuildSimple(SyntaxNode statement)
    {
        long nodeId = GetNodeId(statement);
        CfgModel model = new() { EntryNodeId = nodeId };
        model.ExitNodeIds.Add(nodeId);
        return model;
    }

    private static void Append(CfgModel target, CfgModel source)
    {
        target.Edges.AddRange(source.Edges);
        target.ExitNodeIds.UnionWith(source.ExitNodeIds);
        target.PendingBreaks.UnionWith(source.PendingBreaks);
        target.PendingContinues.UnionWith(source.PendingContinues);
        target.PendingReturns.UnionWith(source.PendingReturns);
    }

    private long GetNodeId(SyntaxNode syntaxNode)
    {
        if (nodeIdsBySyntax.TryGetValue(syntaxNode, out long nodeId))
        {
            return nodeId;
        }

        throw new InvalidOperationException($"语法节点 '{syntaxNode.Kind()}' 没有对应的图节点。");
    }
}
