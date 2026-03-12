using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

public enum LegacyOperationKind
{
    Keep,
    MergeLeft,
    MergeRight,
    Remove,
    Privatize,
    ReplaceWithPlaceholder,
    Stub
}

/// <summary>
/// Adapts legacy operation intent to Hybrid middleware without depending on legacy operation classes.
/// </summary>
public abstract class LegacyOperationMiddleware<TNode> : IMiddleware<TNode> where TNode : SyntaxNode
{
    public SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next)
    {
        var rewritten = Apply(node, context);
        if (rewritten.HasAnnotation(ExecutionAnnotations.DeleteNode))
        {
            return rewritten;
        }

        if (rewritten is TNode typed)
        {
            return next(typed, context);
        }

        return rewritten;
    }

    protected abstract LegacyOperationKind OperationKind { get; }

    private SyntaxNode Apply(TNode node, IRewriteContext context)
    {
        return OperationKind switch
        {
            LegacyOperationKind.Keep => node,
            LegacyOperationKind.MergeLeft => MergeLeft(node) ?? node,
            LegacyOperationKind.MergeRight => MergeRight(node) ?? node,
            LegacyOperationKind.Remove => node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode),
            LegacyOperationKind.Privatize => Privatize(node) ?? node,
            LegacyOperationKind.ReplaceWithPlaceholder => ReplaceWithPlaceholder(node, context) ?? node,
            LegacyOperationKind.Stub => Stub(node, context) ?? node,
            _ => node
        };
    }

    private static SyntaxNode? MergeLeft(SyntaxNode node)
    {
        return node switch
        {
            BinaryExpressionSyntax b => b.Left.WithTriviaFrom(node),
            ParenthesizedExpressionSyntax p => p.Expression.WithTriviaFrom(node),
            PostfixUnaryExpressionSyntax p => p.Operand.WithTriviaFrom(node),
            _ => null
        };
    }

    private static SyntaxNode? MergeRight(SyntaxNode node)
    {
        return node switch
        {
            BinaryExpressionSyntax b => b.Right.WithTriviaFrom(node),
            AssignmentExpressionSyntax a => a.Right.WithTriviaFrom(node),
            CastExpressionSyntax c => c.Expression.WithTriviaFrom(node),
            AwaitExpressionSyntax a => a.Expression.WithTriviaFrom(node),
            ConditionalAccessExpressionSyntax c => c.WhenNotNull.WithTriviaFrom(node),
            PrefixUnaryExpressionSyntax p => p.Operand.WithTriviaFrom(node),
            _ => null
        };
    }

    private static SyntaxNode? Privatize(SyntaxNode node)
    {
        if (node is not MethodDeclarationSyntax method)
        {
            return null;
        }

        var modifiers = new SyntaxTokenList();
        var replaced = false;
        foreach (var token in method.Modifiers)
        {
            if (token.IsKind(SyntaxKind.PublicKeyword)
                || token.IsKind(SyntaxKind.PrivateKeyword)
                || token.IsKind(SyntaxKind.InternalKeyword)
                || token.IsKind(SyntaxKind.ProtectedKeyword))
            {
                if (!replaced)
                {
                    modifiers = modifiers.Add(SyntaxFactory.Token(token.LeadingTrivia, SyntaxKind.PrivateKeyword, token.TrailingTrivia));
                    replaced = true;
                }
            }
            else
            {
                modifiers = modifiers.Add(token);
            }
        }

        if (!replaced)
        {
            modifiers = modifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTrailingTrivia(SyntaxFactory.Space));
        }

        return method.WithModifiers(modifiers);
    }

    private static SyntaxNode? ReplaceWithPlaceholder(SyntaxNode node, IRewriteContext context)
    {
        var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
        return PlaceholderFactory.CreatePlaceholder(node, context.SemanticModel, generator);
    }

    private static SyntaxNode? Stub(SyntaxNode node, IRewriteContext context)
    {
        if (node is not MethodDeclarationSyntax method)
        {
            return null;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(method);
        var statements = new List<StatementSyntax>();
        if (symbol is not null)
        {
            foreach (var param in symbol.Parameters.Where(p => p.RefKind == RefKind.Out))
            {
                var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
                var placeholder = PlaceholderFactory.CreatePlaceholderForType(param.Type, generator);
                if (placeholder is ExpressionSyntax expr)
                {
                    statements.Add(SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName(param.Name),
                            expr)));
                }
            }

            if (!symbol.ReturnsVoid)
            {
                var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
                var ret = PlaceholderFactory.CreatePlaceholderForType(symbol.ReturnType, generator);
                statements.Add(SyntaxFactory.ReturnStatement(ret));
            }
        }

        return method.WithBody(SyntaxFactory.Block(statements))
            .WithExpressionBody(null)
            .WithSemicolonToken(default);
    }
}

public sealed class KeepOperationMiddleware<TNode> : LegacyOperationMiddleware<TNode> where TNode : SyntaxNode
{
    protected override LegacyOperationKind OperationKind => LegacyOperationKind.Keep;
}

public sealed class MergeLeftOperationMiddleware<TNode> : LegacyOperationMiddleware<TNode> where TNode : SyntaxNode
{
    protected override LegacyOperationKind OperationKind => LegacyOperationKind.MergeLeft;
}

public sealed class MergeRightOperationMiddleware<TNode> : LegacyOperationMiddleware<TNode> where TNode : SyntaxNode
{
    protected override LegacyOperationKind OperationKind => LegacyOperationKind.MergeRight;
}

public sealed class RemoveOperationMiddleware<TNode> : LegacyOperationMiddleware<TNode> where TNode : SyntaxNode
{
    protected override LegacyOperationKind OperationKind => LegacyOperationKind.Remove;
}

public sealed class PrivatizeOperationMiddleware<TNode> : LegacyOperationMiddleware<TNode> where TNode : SyntaxNode
{
    protected override LegacyOperationKind OperationKind => LegacyOperationKind.Privatize;
}

public sealed class ReplaceWithPlaceholderOperationMiddleware<TNode> : LegacyOperationMiddleware<TNode> where TNode : SyntaxNode
{
    protected override LegacyOperationKind OperationKind => LegacyOperationKind.ReplaceWithPlaceholder;
}

public sealed class StubOperationMiddleware<TNode> : LegacyOperationMiddleware<TNode> where TNode : SyntaxNode
{
    protected override LegacyOperationKind OperationKind => LegacyOperationKind.Stub;
}

