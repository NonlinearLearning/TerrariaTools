using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NLISSN.Core.Analysis;
//傻逼helper怎么还在
public static class RuleSyntaxAnalysisHelpers
{
    // 枚举当前根节点里属于允许种类的原子候选表达式。
    public static IEnumerable<ExpressionSyntax> EnumerateAllowedExpressions(SyntaxNode root, IReadOnlyCollection<SyntaxKind> allowedKinds, CpgAnalysisContext context, IReadOnlyList<ExpressionSyntax>? atomicCandidates = null)
    {
        _ = context;

        foreach (var expression in atomicCandidates ?? new AtomicExpressionAnalyzer().Analyze(root))
        {
            if (allowedKinds.Contains(expression.Kind()))
            {
                yield return expression;
            }
        }
    }

    // 枚举当前根节点下的方法声明。
    public static IEnumerable<MethodDeclarationSyntax> EnumerateMethodDeclarations(SyntaxNode root, CpgAnalysisContext context)
    {
        _ = context;
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>();
    }

    // 沿表达式父链查找同一逻辑链上的最外层逻辑宿主。
    public static ExpressionSyntax? FindLogicalHost(ExpressionSyntax expression, CpgAnalysisContext context)
    {
        ExpressionSyntax? logicalHost = null;

        for (var current = expression.Parent as ExpressionSyntax;
             current is not null;
             current = current.Parent as ExpressionSyntax)
        {
            if (!current.IsKind(SyntaxKind.LogicalAndExpression) &&
                !current.IsKind(SyntaxKind.LogicalOrExpression))
            {
                break;
            }

            var analysis = new BinaryExpressionAnalyzer().Analyze(
              (BinaryExpressionSyntax)current,
              expression,
              context);
            if (!analysis.AffectedSyntaxTree.Any(node => ReferenceEquals(node, expression)))
            {
                break;
            }

            logicalHost = current;
        }

        return logicalHost;
    }

    // 按结构优先级向上寻找最贴近当前表达式的语句或控制结构宿主。
    public static SyntaxNode? FindStructuralHost(ExpressionSyntax expression, CpgAnalysisContext context)
    {
        foreach (var ancestor in expression.Ancestors())
        {
            if (TryResolveStructuralHost(ancestor, expression, context, out var host))
            {
                return host;
            }

            if (ancestor is StatementSyntax statement)
            {
                return statement;
            }
        }

        return expression.FirstAncestorOrSelf<StatementSyntax>();
    }

    // 向上定位当前表达式所在的赋值或定义宿主，供传播阶段收口局部结构。
    public static SyntaxNode? FindAssignmentOrDefinitionHost(ExpressionSyntax expression, CpgAnalysisContext context)
    {
        _ = context;

        foreach (var ancestor in expression.Ancestors())
        {
            if (ancestor is AssignmentExpressionSyntax assignmentExpression &&
                (assignmentExpression.Right.Span.Contains(expression.Span) ||
                 assignmentExpression.Left.Span.Contains(expression.Span)))
            {
                return assignmentExpression;
            }

            if (ancestor is EqualsValueClauseSyntax equalsValueClause &&
                equalsValueClause.Value.Span.Contains(expression.Span) &&
                equalsValueClause.Parent is VariableDeclaratorSyntax variableDeclarator)
            {
                return variableDeclarator;
            }
        }

        return null;
    }

    private static bool TryResolveStructuralHost(SyntaxNode ancestor, ExpressionSyntax expression, CpgAnalysisContext context, out SyntaxNode? host)
    {
        host = null;

        switch (ancestor)
        {
            case IfStatementSyntax ifStatement when ifStatement.Condition == expression:
                host = ifStatement;
                return true;
            case ReturnStatementSyntax returnStatement when returnStatement.Expression == expression:
                host = returnStatement;
                return true;
            case ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax:
                var loopAnalysis = new LoopStructureAnalyzer().Analyze((StatementSyntax)ancestor, context);
                if (loopAnalysis.AffectedSyntaxTree.Any(node => ReferenceEquals(node, expression)))
                {
                    host = ancestor;
                    return true;
                }

                return false;
            case SwitchStatementSyntax switchStatement when ReferenceEquals(switchStatement.Expression, expression):
                var switchAnalysis = new SwitchStructureAnalyzer().Analyze(switchStatement, context);
                if (switchAnalysis.AffectedSyntaxTree.Any(node => ReferenceEquals(node, expression)))
                {
                    host = switchStatement;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }
}
