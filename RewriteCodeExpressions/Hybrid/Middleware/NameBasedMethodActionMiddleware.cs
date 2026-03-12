using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;
using TerrariaTools.RewriteCodeExpressions.Pipeline;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 基于方法名正则执行方法级重写。
/// </summary>
public sealed class NameBasedMethodActionMiddleware : IMiddleware<MethodDeclarationSyntax>
{
    public SyntaxNode Invoke(MethodDeclarationSyntax node, IRewriteContext context, MiddlewareDelegate<MethodDeclarationSyntax> next)
    {
        var pattern = context.GetState<string>(HybridInputStateKeys.NamePattern) ?? ".*DummyPattern.*";
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return next(node, context);
        }

        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        if (!regex.IsMatch(node.Identifier.Text))
        {
            return next(node, context);
        }

        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(node);
        if (methodSymbol is null)
        {
            return next(node, context);
        }

        if (methodSymbol.IsAbstract
            || methodSymbol.ContainingType.TypeKind == TypeKind.Interface
            || (methodSymbol.IsStatic && methodSymbol.Name == "Main"))
        {
            return next(node, context);
        }

        var markedNodes = context.GetState<HashSet<SyntaxNode>>(HybridInputStateKeys.MarkedNodes);
        var isReferenced = markedNodes is not null && markedNodes.Contains(node);
        var shouldStub = isReferenced || IsPotentiallyPolymorphicContainer(node);

        var clearBodyMatched = context.GetState<bool>(HybridInputStateKeys.ClearBodyMatched);
        var deleteMatched = context.GetState<bool>(HybridInputStateKeys.DeleteMatched);
        if (shouldStub || clearBodyMatched)
        {
            var stubbed = Stub(node, context);
            if (stubbed is MethodDeclarationSyntax stubMethod)
            {
                return next(stubMethod, context);
            }

            return stubbed;
        }

        if (deleteMatched)
        {
            return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
        }

        return next(node, context);
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

    private static bool IsPotentiallyPolymorphicContainer(MethodDeclarationSyntax method)
    {
        var classDecl = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl is not null)
        {
            var isSealed = classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword));
            var hasBase = classDecl.BaseList is not null && classDecl.BaseList.Types.Any();
            return !isSealed || hasBase;
        }

        var recordDecl = method.Ancestors().OfType<RecordDeclarationSyntax>().FirstOrDefault();
        if (recordDecl is not null)
        {
            var isSealed = recordDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword));
            return !isSealed;
        }

        var structDecl = method.Ancestors().OfType<StructDeclarationSyntax>().FirstOrDefault();
        if (structDecl is not null)
        {
            return structDecl.BaseList is not null && structDecl.BaseList.Types.Any();
        }

        return false;
    }
}
