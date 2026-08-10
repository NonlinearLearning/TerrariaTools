using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Marking;
using NLISSN.Core.Pipeline;

namespace NLISSN.Rules;

/// 为 s-object 删除规则登记允许作为最小 seed mark 的原子表达式种类。

public sealed class AtomicThisExpressionMarkRule : AtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.this-expression";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-THIS-001";
    public override string Name { get; } = "Match s-rooted this expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ThisExpression;
}

