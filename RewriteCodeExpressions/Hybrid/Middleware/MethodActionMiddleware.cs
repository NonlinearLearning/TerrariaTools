using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using TerrariaTools.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 基于全局方法动作映射执行方法级重写。
/// </summary>
public sealed class MethodActionMiddleware : IMiddleware<MethodDeclarationSyntax>
{
    public SyntaxNode Invoke(MethodDeclarationSyntax node, IRewriteContext context, MiddlewareDelegate<MethodDeclarationSyntax> next)
    {
        var actions = context.GetState<Dictionary<IMethodSymbol, FunctionBuildGraph.GraphMethodAction>>(HybridInputStateKeys.GlobalMethodActions);
        if (actions is null || actions.Count == 0)
        {
            return next(node, context);
        }

        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(node) as IMethodSymbol;
        if (methodSymbol is null)
        {
            return next(node, context);
        }

        var originalDefinition = methodSymbol.OriginalDefinition as IMethodSymbol;
        if ((originalDefinition is null || !actions.TryGetValue(originalDefinition, out var action))
            && !actions.TryGetValue(methodSymbol, out action))
        {
            return next(node, context);
        }

        var rewritten = ApplyAction(node, action, context);
        if (rewritten is MethodDeclarationSyntax methodDecl)
        {
            return next(methodDecl, context);
        }

        return rewritten;
    }

    private static SyntaxNode ApplyAction(
        MethodDeclarationSyntax node,
        FunctionBuildGraph.GraphMethodAction action,
        IRewriteContext context)
    {
        return action switch
        {
            FunctionBuildGraph.GraphMethodAction.Privatize
                => Privatize(node),

            FunctionBuildGraph.GraphMethodAction.Delete
                => node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode),

            FunctionBuildGraph.GraphMethodAction.Stub
                => Stub(node, context),

            FunctionBuildGraph.GraphMethodAction.ClearBody
                => Stub(node, context),

            _ => node
        };
    }

    private static MethodDeclarationSyntax Privatize(MethodDeclarationSyntax method)
    {
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

    private static MethodDeclarationSyntax Stub(MethodDeclarationSyntax method, IRewriteContext context)
    {
        var symbol = context.SemanticModel.GetDeclaredSymbol(method);
        var statements = new List<StatementSyntax>();
        if (symbol is not null)
        {
            var generator = SyntaxGenerator.GetGenerator(new AdhocWorkspace(), LanguageNames.CSharp);
            foreach (var param in symbol.Parameters.Where(p => p.RefKind == RefKind.Out))
            {
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
                var ret = PlaceholderFactory.CreatePlaceholderForType(symbol.ReturnType, generator);
                statements.Add(SyntaxFactory.ReturnStatement(ret));
            }
        }

        return method.WithBody(SyntaxFactory.Block(statements))
            .WithExpressionBody(null)
            .WithSemicolonToken(default);
    }
}
