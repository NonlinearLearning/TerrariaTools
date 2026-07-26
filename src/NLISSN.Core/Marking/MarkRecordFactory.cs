using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Marking;

public static class MarkRecordFactory
{
    public static MarkRecord Create(string ruleId, SyntaxNode syntaxNode, string reason)
    {
        return new MarkRecord(ruleId, syntaxNode, null, null, reason);
    }
}
