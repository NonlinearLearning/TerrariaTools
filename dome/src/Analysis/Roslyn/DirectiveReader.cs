using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

internal static class DirectiveReader
{
    public static IReadOnlyList<DirectiveAction> Read(StatementSyntax statement)
    {
        var text = statement.GetLeadingTrivia().ToFullString();
        var directives = new List<DirectiveAction>();

        AddDirective(directives, text, "dome:delete", PlanActionKind.Delete);
        AddDirective(directives, text, "dome:comment", PlanActionKind.CommentOut);
        AddDirective(directives, text, "dome:default", PlanActionKind.ReplaceWithDefault, "default");

        return directives;
    }

    private static void AddDirective(
        ICollection<DirectiveAction> directives,
        string triviaText,
        string token,
        PlanActionKind actionKind,
        string? payload = null)
    {
        if (!triviaText.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        directives.Add(new DirectiveAction(actionKind, payload, token, $"Directive '{token}' matched."));
    }
}
