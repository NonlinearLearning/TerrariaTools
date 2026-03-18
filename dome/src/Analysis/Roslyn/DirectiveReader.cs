using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelPrimitives = TerrariaTools.Dome.Model.Primitives;
using ModelRules = TerrariaTools.Dome.Model.Rules;

namespace TerrariaTools.Dome.Analysis.Roslyn;

internal static class DirectiveReader
{
    public static IReadOnlyList<ModelRules.DirectiveAction> Read(StatementSyntax statement)
    {
        var trivia = statement.GetLeadingTrivia();
        if (trivia.Count == 0 || !trivia.Any(static item =>
            item.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia ||
            item.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineCommentTrivia))
        {
            return Array.Empty<ModelRules.DirectiveAction>();
        }

        var text = trivia.ToFullString();
        var directives = new List<ModelRules.DirectiveAction>();

        AddDirective(directives, text, "dome:delete", ModelPrimitives.PlanActionKind.Delete);
        AddDirective(directives, text, "dome:comment", ModelPrimitives.PlanActionKind.CommentOut);
        AddDirective(directives, text, "dome:default", ModelPrimitives.PlanActionKind.ReplaceWithDefault, "default");

        return directives;
    }

    private static void AddDirective(
        ICollection<ModelRules.DirectiveAction> directives,
        string triviaText,
        string token,
        ModelPrimitives.PlanActionKind actionKind,
        string? payload = null)
    {
        if (!triviaText.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        directives.Add(new ModelRules.DirectiveAction(actionKind, payload, token, $"Directive '{token}' matched."));
    }
}
