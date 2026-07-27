using Microsoft.CodeAnalysis;

namespace NLISSN.Core.Marking;

public static class MarkRecordFactory
{
    // 用最小必需字段构造一条种子标记记录，延后图绑定和注解填充。
    public static MarkRecord Create(string ruleId, SyntaxNode syntaxNode, string reason)
    {
        return new MarkRecord(ruleId, syntaxNode, null, null, reason);
    }
}
