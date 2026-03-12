using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 简化常量条件的 if 语句。
/// </summary>
public sealed class IfConstantConditionMiddleware : IMiddleware<IfStatementSyntax>
{
    public SyntaxNode Invoke(IfStatementSyntax node, IRewriteContext context, MiddlewareDelegate<IfStatementSyntax> next)
    {
        if (node.Condition.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            return node.Statement.WithTriviaFrom(node);
        }

        if (node.Condition.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            return node.Else?.Statement.WithTriviaFrom(node) ?? SyntaxFactory.EmptyStatement().WithTriviaFrom(node);
        }

        return next(node, context);
    }
}

/// <summary>
/// 简化常量条件的 while 语句。
/// </summary>
public sealed class WhileConstantConditionMiddleware : IMiddleware<WhileStatementSyntax>
{
    public SyntaxNode Invoke(WhileStatementSyntax node, IRewriteContext context, MiddlewareDelegate<WhileStatementSyntax> next)
    {
        if (node.Condition.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            return SyntaxFactory.EmptyStatement().WithTriviaFrom(node);
        }

        return next(node, context);
    }
}

/// <summary>
/// 简化常量条件的三元表达式。
/// </summary>
public sealed class ConditionalConstantConditionMiddleware : IMiddleware<ConditionalExpressionSyntax>
{
    public SyntaxNode Invoke(ConditionalExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<ConditionalExpressionSyntax> next)
    {
        if (node.Condition.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            return node.WhenTrue.WithTriviaFrom(node);
        }

        if (node.Condition.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            return node.WhenFalse.WithTriviaFrom(node);
        }

        return next(node, context);
    }
}
