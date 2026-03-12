using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// Simplifies boolean binary expressions such as true && x / false || x.
/// </summary>
public sealed class BooleanBinarySimplificationMiddleware : IMiddleware<BinaryExpressionSyntax>
{
    public SyntaxNode Invoke(BinaryExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<BinaryExpressionSyntax> next)
    {
        var rewritten = next(node, context) as BinaryExpressionSyntax ?? node;
        var left = rewritten.Left;
        var right = rewritten.Right;

        switch (rewritten.Kind())
        {
            case SyntaxKind.LogicalAndExpression:
                if (left.IsKind(SyntaxKind.TrueLiteralExpression)) return right.WithLeadingTrivia(rewritten.GetLeadingTrivia());
                if (right.IsKind(SyntaxKind.TrueLiteralExpression)) return left.WithTrailingTrivia(rewritten.GetTrailingTrivia());
                if (left.IsKind(SyntaxKind.FalseLiteralExpression)) return left.WithLeadingTrivia(rewritten.GetLeadingTrivia());
                if (right.IsKind(SyntaxKind.FalseLiteralExpression)) return right.WithTrailingTrivia(rewritten.GetTrailingTrivia());
                break;

            case SyntaxKind.LogicalOrExpression:
                if (left.IsKind(SyntaxKind.TrueLiteralExpression)) return left.WithLeadingTrivia(rewritten.GetLeadingTrivia());
                if (right.IsKind(SyntaxKind.TrueLiteralExpression)) return right.WithTrailingTrivia(rewritten.GetTrailingTrivia());
                if (left.IsKind(SyntaxKind.FalseLiteralExpression)) return right.WithLeadingTrivia(rewritten.GetLeadingTrivia());
                if (right.IsKind(SyntaxKind.FalseLiteralExpression)) return left.WithTrailingTrivia(rewritten.GetTrailingTrivia());
                break;
        }

        return rewritten;
    }
}

/// <summary>
/// Simplifies boolean unary expressions such as !!x and !true/!false.
/// </summary>
public sealed class BooleanUnarySimplificationMiddleware : IMiddleware<PrefixUnaryExpressionSyntax>
{
    public SyntaxNode Invoke(PrefixUnaryExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<PrefixUnaryExpressionSyntax> next)
    {
        var rewritten = next(node, context) as PrefixUnaryExpressionSyntax ?? node;
        if (!rewritten.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return rewritten;
        }

        var operand = rewritten.Operand;
        if (operand.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression).WithTriviaFrom(rewritten);
        }

        if (operand.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression).WithTriviaFrom(rewritten);
        }

        if (operand is PrefixUnaryExpressionSyntax inner && inner.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return inner.Operand.WithTriviaFrom(rewritten);
        }

        return rewritten;
    }
}

/// <summary>
/// Removes redundant parentheses around trivial expressions.
/// </summary>
public sealed class BooleanParenthesizedSimplificationMiddleware : IMiddleware<ParenthesizedExpressionSyntax>
{
    public SyntaxNode Invoke(ParenthesizedExpressionSyntax node, IRewriteContext context, MiddlewareDelegate<ParenthesizedExpressionSyntax> next)
    {
        var rewritten = next(node, context) as ParenthesizedExpressionSyntax ?? node;
        var expression = rewritten.Expression;

        if (expression is LiteralExpressionSyntax || expression is IdentifierNameSyntax)
        {
            return expression.WithTriviaFrom(rewritten);
        }

        return rewritten;
    }
}

