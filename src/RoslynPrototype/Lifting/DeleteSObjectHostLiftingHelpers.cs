using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynPrototype.Analysis;
using RoslynPrototype.Marking;
using RoslynPrototype.Propagation;
using Rules;

namespace RoslynPrototype.Lifting;

internal static class DeleteSObjectHostLiftingHelpers
{
    internal static IEnumerable<LiftedMarkRecord> BuildHostLiftedMarks(RuleContext context, string ruleId, IReadOnlyList<MarkRecord> seedMarks, IReadOnlyList<PropagatedMarkRecord> propagatedMarks)
    {
        var liftedMarks = new List<LiftedMarkRecord>();
        var knownKeys = seedMarks
          .Select(mark => DeleteSObjectLiftingCommon.BuildNodeKey(mark.SyntaxNode))
          .Concat(propagatedMarks.Select(mark =>
            DeleteSObjectLiftingCommon.BuildNodeKey(mark.Mark.SyntaxNode)))
          .ToHashSet();
        var worklist = seedMarks
          .Select(mark => (Current: mark, Source: mark, Depth: 0))
          .Concat(propagatedMarks.Select(mark =>
            (Current: mark.Mark, Source: mark.SourceMark, Depth: mark.Depth)))
          .ToList();

        for (var index = 0; index < worklist.Count; index++)
        {
            var item = worklist[index];
            if (DeleteSObjectLiftingCommon.IsSymbolReferencePropagation(item.Current))
            {
                continue;
            }

            var liftedNode = TryLift(item.Current.SyntaxNode, context.AnalysisContext);
            if (liftedNode is null)
            {
                continue;
            }

            var key = DeleteSObjectLiftingCommon.BuildNodeKey(liftedNode);
            if (!knownKeys.Add(key))
            {
                continue;
            }

            var liftedMark = new LiftedMarkRecord(
              ruleId,
              RuleAnalysisHelpers.CreateMark(
                ruleId,
                liftedNode,
                $"Lifted mark from {item.Current.SyntaxNode.Kind()} to {liftedNode.Kind()}."),
              item.Source,
              item.Depth + 1);
            liftedMarks.Add(liftedMark);
            worklist.Add((liftedMark.Mark, liftedMark.SourceMark, liftedMark.Depth));
        }

        return liftedMarks;
    }

    private static SyntaxNode? TryLift(SyntaxNode markedNode, CpgAnalysisContext context)
    {
        if (markedNode is ExpressionSyntax expression)
        {
            return TryLiftExpression(expression, context);
        }

        if (markedNode is VariableDeclaratorSyntax variableDeclarator)
        {
            return TryLiftVariableDeclarator(variableDeclarator);
        }

        if (markedNode is StatementSyntax statement)
        {
            return TryLiftStatement(statement);
        }

        return TryLiftNonExpressionNode(markedNode);
    }

    private static SyntaxNode? TryLiftNonExpressionNode(SyntaxNode markedNode)
    {
        switch (markedNode)
        {
            case ArgumentSyntax argument:
                return argument.Parent;
            case ArgumentListSyntax argumentList:
                return argumentList.Parent;
            case BracketedArgumentListSyntax bracketedArgumentList:
                return bracketedArgumentList.Parent;
            case InterpolationSyntax interpolation:
                return interpolation.Parent;
            case SwitchExpressionArmSyntax switchArm:
                return switchArm.Parent;
        }

        return null;
    }

    private static SyntaxNode? TryLiftExpression(ExpressionSyntax expression, CpgAnalysisContext context)
    {
        if (expression is BinaryExpressionSyntax logicalExpression &&
            (logicalExpression.IsKind(SyntaxKind.LogicalAndExpression) ||
             logicalExpression.IsKind(SyntaxKind.LogicalOrExpression)))
        {
            return TryLiftLogicalExpression(logicalExpression, context);
        }

        foreach (var ancestor in expression.Ancestors())
        {
            if (TryLiftTransparentExpression(expression, ancestor, out var transparentHost))
            {
                return transparentHost;
            }

            if (ancestor is ArgumentSyntax argument &&
                argument.Expression.Span.Contains(expression.Span))
            {
                return argument;
            }

            if (ancestor is InterpolationSyntax interpolation &&
                interpolation.Expression.Span.Contains(expression.Span))
            {
                return interpolation;
            }

            if (ancestor is MemberAccessExpressionSyntax memberAccess &&
                (memberAccess.Expression.Span.Contains(expression.Span) ||
                 memberAccess.Name.Span.Contains(expression.Span)))
            {
                return memberAccess;
            }

            if (ancestor is ConditionalAccessExpressionSyntax conditionalAccess &&
                (conditionalAccess.Expression.Span.Contains(expression.Span) ||
                 conditionalAccess.WhenNotNull.Span.Contains(expression.Span)))
            {
                return conditionalAccess;
            }

            if (ancestor is ElementAccessExpressionSyntax elementAccess &&
                (elementAccess.Expression.Span.Contains(expression.Span) ||
                 elementAccess.ArgumentList.Span.Contains(expression.Span)))
            {
                return elementAccess;
            }

            if (ancestor is InvocationExpressionSyntax invocation &&
                (invocation.Expression.Span.Contains(expression.Span) ||
                 invocation.ArgumentList.Span.Contains(expression.Span)))
            {
                return invocation;
            }

            if (ancestor is ObjectCreationExpressionSyntax objectCreation &&
                ((objectCreation.ArgumentList is not null &&
                  objectCreation.ArgumentList.Span.Contains(expression.Span)) ||
                 (objectCreation.Initializer is not null &&
                  objectCreation.Initializer.Span.Contains(expression.Span))))
            {
                return objectCreation;
            }

            if (ancestor is ImplicitObjectCreationExpressionSyntax implicitObjectCreation &&
                (implicitObjectCreation.ArgumentList.Span.Contains(expression.Span) ||
                 (implicitObjectCreation.Initializer is not null &&
                  implicitObjectCreation.Initializer.Span.Contains(expression.Span))))
            {
                return implicitObjectCreation;
            }

            if (ancestor is InitializerExpressionSyntax initializer &&
                initializer.Expressions.Any(node => node.Span.Contains(expression.Span)))
            {
                return initializer;
            }

            if (ancestor is ConditionalExpressionSyntax conditionalExpression &&
                (conditionalExpression.Condition.Span.Contains(expression.Span) ||
                 conditionalExpression.WhenTrue.Span.Contains(expression.Span) ||
                 conditionalExpression.WhenFalse.Span.Contains(expression.Span)))
            {
                return conditionalExpression;
            }

            if (ancestor is SwitchExpressionArmSyntax switchArm &&
                (switchArm.Expression.Span.Contains(expression.Span) ||
                 switchArm.Pattern.Span.Contains(expression.Span) ||
                 switchArm.WhenClause?.Condition.Span.Contains(expression.Span) == true))
            {
                return switchArm;
            }

            if (ancestor is SwitchExpressionSyntax switchExpression &&
                (switchExpression.GoverningExpression.Span.Contains(expression.Span) ||
                 switchExpression.Arms.Any(arm => arm.Span.Contains(expression.Span))))
            {
                return switchExpression;
            }

            if (ancestor is AssignmentExpressionSyntax assignmentExpression &&
                (assignmentExpression.Left.Span.Contains(expression.Span) ||
                 assignmentExpression.Right.Span.Contains(expression.Span)))
            {
                return assignmentExpression.Parent is ExpressionStatementSyntax expressionStatement
                  ? expressionStatement
                  : assignmentExpression;
            }

            if (ancestor is EqualsValueClauseSyntax equalsValueClause &&
                equalsValueClause.Value.Span.Contains(expression.Span) &&
                equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator)
            {
                var definitionHost = TryLiftVariableDeclarator(variableDeclarator);
                if (definitionHost is not null)
                {
                    return definitionHost;
                }
            }

            if (ancestor is BinaryExpressionSyntax binaryExpression &&
                (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) ||
                 binaryExpression.IsKind(SyntaxKind.LogicalOrExpression)))
            {
                var logicalHost = RuleAnalysisHelpers.FindLogicalHost(expression, context);
                return logicalHost ?? binaryExpression;
            }

            if (TryLiftControlHeaderExpression(expression, ancestor, context, out var controlHost))
            {
                return controlHost;
            }

            if (TryLiftAdditionalStatementExpression(expression, ancestor, out var statementHost))
            {
                return statementHost;
            }

            if (ancestor is StatementSyntax statement)
            {
                return statement;
            }
        }

        return null;
    }

    private static bool TryLiftTransparentExpression(ExpressionSyntax expression, SyntaxNode ancestor, out SyntaxNode? host)
    {
        host = ancestor switch
        {
            ParenthesizedExpressionSyntax parenthesized
              when parenthesized.Expression.Span.Contains(expression.Span) => parenthesized,
            PrefixUnaryExpressionSyntax prefix
              when prefix.Operand.Span.Contains(expression.Span) => prefix,
            PostfixUnaryExpressionSyntax postfix
              when postfix.Operand.Span.Contains(expression.Span) => postfix,
            CastExpressionSyntax cast
              when cast.Expression.Span.Contains(expression.Span) => cast,
            AwaitExpressionSyntax awaitExpression
              when awaitExpression.Expression.Span.Contains(expression.Span) => awaitExpression,
            CheckedExpressionSyntax checkedExpression
              when checkedExpression.Expression.Span.Contains(expression.Span) => checkedExpression,
            RefExpressionSyntax refExpression
              when refExpression.Expression.Span.Contains(expression.Span) => refExpression,
            PrefixUnaryExpressionSyntax addressOfExpression
              when addressOfExpression.IsKind(SyntaxKind.AddressOfExpression) &&
                   addressOfExpression.Operand.Span.Contains(expression.Span) => addressOfExpression,
            _ => null
        };

        return host is not null;
    }

    private static bool TryLiftAdditionalStatementExpression(ExpressionSyntax expression, SyntaxNode ancestor, out SyntaxNode? host)
    {
        host = ancestor switch
        {
            YieldStatementSyntax yieldStatement
              when yieldStatement.Expression?.Span.Contains(expression.Span) == true => yieldStatement,
            ThrowStatementSyntax throwStatement
              when throwStatement.Expression?.Span.Contains(expression.Span) == true => throwStatement,
            ArrowExpressionClauseSyntax arrowExpression
              when arrowExpression.Expression.Span.Contains(expression.Span) => arrowExpression,
            LockStatementSyntax lockStatement
              when lockStatement.Expression.Span.Contains(expression.Span) => lockStatement,
            UsingStatementSyntax usingStatement
              when usingStatement.Expression?.Span.Contains(expression.Span) == true ||
                   usingStatement.Declaration?.Span.Contains(expression.Span) == true => usingStatement,
            FixedStatementSyntax fixedStatement
              when fixedStatement.Declaration.Span.Contains(expression.Span) => fixedStatement,
            ForEachStatementSyntax forEachStatement
              when forEachStatement.Expression.Span.Contains(expression.Span) => forEachStatement,
            _ => null
        };

        return host is not null;
    }

    private static SyntaxNode? TryLiftLogicalExpression(BinaryExpressionSyntax expression, CpgAnalysisContext context)
    {
        var logicalHost = RuleAnalysisHelpers.FindLogicalHost(expression, context);
        if (logicalHost is not null &&
            !ReferenceEquals(logicalHost, expression))
        {
            return logicalHost;
        }

        return null;
    }

    private static SyntaxNode? TryLiftVariableDeclarator(VariableDeclaratorSyntax variableDeclarator)
    {
        if (variableDeclarator.Parent?.Parent is LocalDeclarationStatementSyntax localDeclarationStatement)
        {
            return localDeclarationStatement;
        }

        if (variableDeclarator.Parent is VariableDeclarationSyntax variableDeclaration &&
            variableDeclaration.Parent is ForStatementSyntax forStatement)
        {
            return forStatement;
        }

        return null;
    }

    private static SyntaxNode? TryLiftStatement(StatementSyntax statement)
    {
        if (statement.Parent is BlockSyntax block &&
            block.Statements.Count == 1)
        {
            if (block.Parent is WhileStatementSyntax whileStatement &&
                ReferenceEquals(whileStatement.Statement, block))
            {
                return whileStatement;
            }

            if (block.Parent is DoStatementSyntax doStatement &&
                ReferenceEquals(doStatement.Statement, block))
            {
                return doStatement;
            }
        }

        return null;
    }

    private static bool TryLiftControlHeaderExpression(ExpressionSyntax expression, SyntaxNode ancestor, CpgAnalysisContext context, out SyntaxNode? host)
    {
        host = null;

        switch (ancestor)
        {
            case IfStatementSyntax ifStatement when ifStatement.Condition.Span.Contains(expression.Span):
                host = ifStatement;
                return true;
            case ForStatementSyntax forStatement:
                var forAnalysis = new LoopStructureAnalyzer().Analyze(forStatement, context);
                if (forAnalysis.AffectedSyntaxTree.Any(node => node.Span.Contains(expression.Span)))
                {
                    host = forStatement;
                    return true;
                }

                return false;
            case WhileStatementSyntax whileStatement when whileStatement.Condition.Span.Contains(expression.Span):
                host = whileStatement;
                return true;
            case DoStatementSyntax doStatement when doStatement.Condition.Span.Contains(expression.Span):
                host = doStatement;
                return true;
            case SwitchStatementSyntax switchStatement when switchStatement.Expression.Span.Contains(expression.Span):
                host = switchStatement;
                return true;
            case ReturnStatementSyntax returnStatement when returnStatement.Expression?.Span.Contains(expression.Span) == true:
                host = returnStatement;
                return true;
            default:
                return false;
        }
    }
}
