using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerrariaTools.Dome.Analysis.Roslyn;

using TerrariaTools.Dome.Core;

/// <summary>
/// 指令读取器，用于从代码语句的前导琐碎内容（trivia）中解析指令。
/// </summary>
internal static class DirectiveReader
{
    /// <summary>
    /// 读取语句中的指令。
    /// </summary>
    /// <param name="statement">要分析的语法语句。</param>
    /// <returns>解析出的指令列表。</returns>
    public static IReadOnlyList<DirectiveAction> Read(StatementSyntax statement)
    {
        var trivia = statement.GetLeadingTrivia();
        if (trivia.Count == 0 || !trivia.Any(static item =>
            item.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.SingleLineCommentTrivia ||
            item.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.MultiLineCommentTrivia))
        {
            return Array.Empty<DirectiveAction>();
        }

        var text = trivia.ToFullString();
        var directives = new List<DirectiveAction>();

        AddDirective(directives, text, "dome:delete", PlanActionKind.Delete);
        AddDirective(directives, text, "dome:comment", PlanActionKind.CommentOut);
        AddDirective(directives, text, "dome:default", PlanActionKind.ReplaceWithDefault, "default");

        return directives;
    }

    /// <summary>
    /// 添加指令到集合中。
    /// </summary>
    /// <param name="directives">指令集合。</param>
    /// <param name="triviaText">包含指令的文本。</param>
    /// <param name="token">指令标记。</param>
    /// <param name="actionKind">计划操作类型。</param>
    /// <param name="payload">指令负载数据（可选）。</param>
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
