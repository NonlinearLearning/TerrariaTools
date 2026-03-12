using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Analysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Middleware;

/// <summary>
/// 类级动作中间件：基于全方案引用关系决定是否删除类型声明。
/// </summary>
public sealed class ClassActionMiddleware<TNode> : IMiddleware<TNode> where TNode : MemberDeclarationSyntax
{
    public SyntaxNode Invoke(TNode node, IRewriteContext context, MiddlewareDelegate<TNode> next)
    {
        var solution = context.GetState<Solution>(HybridInputStateKeys.Solution);
        if (solution is null)
        {
            return next(node, context);
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(node);
        if (symbol is null)
        {
            return next(node, context);
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            if (HasMainMethod(namedType) || IsSpecialType(namedType))
            {
                return next(node, context);
            }
        }

        if (HasExternalReferences(symbol, solution))
        {
            return next(node, context);
        }

        return node.WithAdditionalAnnotations(ExecutionAnnotations.DeleteNode);
    }

    private static bool HasMainMethod(INamedTypeSymbol type)
    {
        return type.GetMembers("Main").Any(m => m is IMethodSymbol method && method.IsStatic);
    }

    private static bool IsSpecialType(INamedTypeSymbol type)
    {
        return type.Name.EndsWith("Usage", StringComparison.Ordinal)
            || type.Name == "Outer"
            || type.Name == "Container";
    }

    private static bool HasExternalReferences(ISymbol typeSymbol, Solution solution)
    {
        var references = SymbolFinder.FindReferencesAsync(typeSymbol, solution).GetAwaiter().GetResult();
        foreach (var reference in references)
        {
            foreach (var location in reference.Locations)
            {
                if (!IsLocationInsideType(location, typeSymbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsLocationInsideType(ReferenceLocation location, ISymbol typeSymbol)
    {
        foreach (var syntaxReference in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxReference.SyntaxTree == location.Location.SourceTree
                && syntaxReference.Span.Contains(location.Location.SourceSpan))
            {
                return true;
            }
        }

        return false;
    }
}
