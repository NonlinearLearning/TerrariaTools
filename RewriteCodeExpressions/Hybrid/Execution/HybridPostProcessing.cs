using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Execution;

/// <summary>
/// Final post-processing pass for Hybrid output.
/// </summary>
public static class HybridPostProcessing
{
    public static SyntaxNode Process(SyntaxNode root, SemanticModel? model)
    {
        var cleaned = new EmptyStatementCleanupRewriter().Visit(root) ?? root;
        if (model is null)
        {
            return cleaned.NormalizeWhitespace();
        }

        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("HybridPostProcess", LanguageNames.CSharp);
        var document = workspace.AddDocument(project.Id, "Hybrid.cs", Microsoft.CodeAnalysis.Text.SourceText.From(string.Empty))
            .WithSyntaxRoot(cleaned);

        var simplifiedDoc = Simplifier.ReduceAsync(document).GetAwaiter().GetResult();
        var formattedDoc = Formatter.FormatAsync(simplifiedDoc).GetAwaiter().GetResult();
        var finalRoot = formattedDoc.GetSyntaxRootAsync().GetAwaiter().GetResult();
        return finalRoot ?? cleaned;
    }

    private sealed class EmptyStatementCleanupRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return null;
            }

            return base.VisitExpressionStatement(node);
        }

        public override SyntaxNode? VisitEmptyStatement(EmptyStatementSyntax node)
        {
            return null;
        }
    }
}

