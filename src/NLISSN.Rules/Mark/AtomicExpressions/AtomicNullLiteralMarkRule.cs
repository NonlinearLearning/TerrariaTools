using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Rules;

/// 为 s-object 删除规则登记允许作为最小 seed mark 的原子表达式种类。

[NLISSN.Core.Pipeline.RuleRegistration(NLISSN.Core.Pipeline.RuleFeature.Core)]
public sealed class AtomicNullLiteralMarkRule : AtomicExpressionMarkRuleBase
{

    public override string RuleId { get; } = "mark.target.null-literal";
    public override string Name { get; } = "Match s-rooted null literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.NullLiteralExpression;
}

