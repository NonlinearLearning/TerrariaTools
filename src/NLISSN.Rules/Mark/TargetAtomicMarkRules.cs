using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NLISSN.Core.Marking;

namespace NLISSN.Rules;

/// 为 s-object 删除规则登记允许作为最小 seed mark 的原子表达式种类。
public sealed class SObjectIdentifierNameMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.identifier-name";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-ID-001";
    public override string Name { get; } = "Match s-rooted identifier expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.IdentifierName;
}

public sealed class SObjectThisExpressionMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.this-expression";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-THIS-001";
    public override string Name { get; } = "Match s-rooted this expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ThisExpression;
}

public sealed class SObjectBaseExpressionMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.base-expression";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-BASE-001";
    public override string Name { get; } = "Match s-rooted base expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.BaseExpression;
}

public sealed class SObjectVariableDeclaratorMarkRule : RuleDefinitionMark
{
    public override RuleFactDomain FactDomain => RuleFactDomain.SObject;
    public override string CapabilityId { get; } = "mark.target.variable-declarator";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-DECL-001";
    public override string GroupKey { get; } = "DEL-SOBJ";
    public override string Name { get; } = "Match s-rooted variable declarators";
    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } = new[] { SyntaxKind.VariableDeclarator };

    // 把命中目标名称的变量定义点收束为 declarator，供后续符号传播沿定义继续扩展。
    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteSObjectMarkRuleHelpers.BuildDefinitionLeftValueMarks(context, root, RuleId);
    }
}

public sealed class SObjectNumericLiteralMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.numeric-literal";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-LIT-NUM-001";
    public override string Name { get; } = "Match s-rooted numeric literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.NumericLiteralExpression;
}

public sealed class SObjectStringLiteralMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.string-literal";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-LIT-STR-001";
    public override string Name { get; } = "Match s-rooted string literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.StringLiteralExpression;
}

public sealed class SObjectTrueLiteralMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.true-literal";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-LIT-TRUE-001";
    public override string Name { get; } = "Match s-rooted true literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.TrueLiteralExpression;
}

public sealed class SObjectFalseLiteralMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.false-literal";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-LIT-FALSE-001";
    public override string Name { get; } = "Match s-rooted false literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.FalseLiteralExpression;
}

public sealed class SObjectNullLiteralMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.null-literal";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-LIT-NULL-001";
    public override string Name { get; } = "Match s-rooted null literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.NullLiteralExpression;
}

public sealed class SObjectMemberAccessMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.member-access";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-MEMBER-001";
    public override string Name { get; } = "Match s-rooted member access expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.SimpleMemberAccessExpression;
}

public sealed class SObjectMemberBindingMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.member-binding";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-BINDING-001";
    public override string Name { get; } = "Match s-rooted member binding expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.MemberBindingExpression;
}

public sealed class SObjectInvocationMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.invocation";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-INVOKE-001";
    public override string Name { get; } = "Match s-rooted invocation expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.InvocationExpression;
}

public sealed class SObjectObjectCreationMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.object-creation";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-NEW-001";
    public override string Name { get; } = "Match s-rooted object creation expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ObjectCreationExpression;
}

public sealed class SObjectImplicitObjectCreationMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.implicit-object-creation";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-IMPLICIT-NEW-001";
    public override string Name { get; } = "Match s-rooted implicit object creation expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ImplicitObjectCreationExpression;
}

public sealed class SObjectElementAccessMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.element-access";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-ELEMENT-001";
    public override string Name { get; } = "Match s-rooted element access expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ElementAccessExpression;
}

public sealed class SObjectConditionalAccessMarkRule : SObjectAtomicExpressionMarkRuleBase
{
    public override string CapabilityId { get; } = "mark.target.conditional-access";

    public override string RuleId { get; } = "DEL-SOBJ-MARK-CONDITIONAL-001";
    public override string Name { get; } = "Match s-rooted conditional access expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ConditionalAccessExpression;
}
