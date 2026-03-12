using Microsoft.CodeAnalysis;
using TerrariaTools.RewriteCodeExpressions.Hybrid.Contracts;

namespace TerrariaTools.RewriteCodeExpressions.Hybrid.Rules;

public sealed class RuleConflictException : Exception
{
    public RuleConflictException(SyntaxNode node, IReadOnlyList<IRule> conflictingRules)
        : base(BuildMessage(node, conflictingRules))
    {
        Node = node;
        ConflictingRules = conflictingRules;
    }

    public SyntaxNode Node { get; }
    public IReadOnlyList<IRule> ConflictingRules { get; }

    private static string BuildMessage(SyntaxNode node, IReadOnlyList<IRule> rules)
    {
        var details = string.Join(", ", rules.Select(r => $"{r.NodeType.Name}(priority={r.Priority})"));
        return $"Rule conflict detected at node '{node.GetType().Name}': {details}";
    }
}
