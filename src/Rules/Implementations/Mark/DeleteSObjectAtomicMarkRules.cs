using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPrototype.Marking;

namespace Rules;

public abstract class DeleteSObjectAtomicExpressionMarkRuleBase : RuleDefinitionMark
{
    public override string GroupKey { get; } = DeleteSObjectRuleIds.GroupKey;

    protected abstract SyntaxKind MarkKind { get; }

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => new[] { MarkKind };

    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteSObjectMarkRuleHelpers.BuildExpressionMarks(
          context,
          root,
          RuleId,
          AllowedMarkNodeKinds);
    }
}

public sealed class DeleteSObjectIdentifierNameMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.IdentifierNameMarkRuleId;
    public override string Name { get; } = "Match s-rooted identifier expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.IdentifierName;
}

public sealed class DeleteSObjectThisExpressionMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.ThisExpressionMarkRuleId;
    public override string Name { get; } = "Match s-rooted this expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ThisExpression;
}

public sealed class DeleteSObjectBaseExpressionMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.BaseExpressionMarkRuleId;
    public override string Name { get; } = "Match s-rooted base expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.BaseExpression;
}

public sealed class DeleteSObjectVariableDeclaratorMarkRule : RuleDefinitionMark
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.VariableDeclaratorMarkRuleId;
    public override string GroupKey { get; } = DeleteSObjectRuleIds.GroupKey;
    public override string Name { get; } = "Match s-rooted variable declarators";
    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } = new[] { SyntaxKind.VariableDeclarator };

    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteSObjectMarkRuleHelpers.BuildDefinitionLeftValueMarks(context, root, RuleId);
    }
}

public sealed class DeleteSObjectNumericLiteralMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.NumericLiteralMarkRuleId;
    public override string Name { get; } = "Match s-rooted numeric literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.NumericLiteralExpression;
}

public sealed class DeleteSObjectStringLiteralMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.StringLiteralMarkRuleId;
    public override string Name { get; } = "Match s-rooted string literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.StringLiteralExpression;
}

public sealed class DeleteSObjectTrueLiteralMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.TrueLiteralMarkRuleId;
    public override string Name { get; } = "Match s-rooted true literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.TrueLiteralExpression;
}

public sealed class DeleteSObjectFalseLiteralMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.FalseLiteralMarkRuleId;
    public override string Name { get; } = "Match s-rooted false literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.FalseLiteralExpression;
}

public sealed class DeleteSObjectNullLiteralMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.NullLiteralMarkRuleId;
    public override string Name { get; } = "Match s-rooted null literal expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.NullLiteralExpression;
}

public sealed class DeleteSObjectMemberAccessMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.MemberAccessMarkRuleId;
    public override string Name { get; } = "Match s-rooted member access expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.SimpleMemberAccessExpression;
}

public sealed class DeleteSObjectMemberBindingMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.MemberBindingMarkRuleId;
    public override string Name { get; } = "Match s-rooted member binding expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.MemberBindingExpression;
}

public sealed class DeleteSObjectInvocationMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.InvocationMarkRuleId;
    public override string Name { get; } = "Match s-rooted invocation expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.InvocationExpression;
}

public sealed class DeleteSObjectObjectCreationMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.ObjectCreationMarkRuleId;
    public override string Name { get; } = "Match s-rooted object creation expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ObjectCreationExpression;
}

public sealed class DeleteSObjectImplicitObjectCreationMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.ImplicitObjectCreationMarkRuleId;
    public override string Name { get; } = "Match s-rooted implicit object creation expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ImplicitObjectCreationExpression;
}

public sealed class DeleteSObjectElementAccessMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.ElementAccessMarkRuleId;
    public override string Name { get; } = "Match s-rooted element access expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ElementAccessExpression;
}

public sealed class DeleteSObjectConditionalAccessMarkRule : DeleteSObjectAtomicExpressionMarkRuleBase
{
    public override string RuleId { get; } = DeleteSObjectRuleIds.ConditionalAccessMarkRuleId;
    public override string Name { get; } = "Match s-rooted conditional access expressions";
    protected override SyntaxKind MarkKind => SyntaxKind.ConditionalAccessExpression;
}
