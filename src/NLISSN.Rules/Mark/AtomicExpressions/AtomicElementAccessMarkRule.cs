using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Rules;

/// 为 s-object 删除规则登记允许作为最小 seed mark 的原子表达式种类。

[global::NLISSN.Core.Pipeline.RuleRegistration]
public sealed class AtomicElementAccessMarkRule : AtomicExpressionMarkRuleBase
{

    public override string RuleId { get; } = "mark.target.element-access";
    public override string Name { get; } = "Match s-rooted element access expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ElementAccessExpression;
}

