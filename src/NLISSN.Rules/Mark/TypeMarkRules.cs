using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Deletion.Core.Marking;

namespace Deletion.Rules;

public sealed class ClassDeclarationMarkRule : RuleDefinitionMark
{
    public override string CapabilityId { get; } = "mark.type.declaration";

    public override string RuleId { get; } = "DEL-CLASS-MARK-DECL-001";

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Match class declarations by delete-class option";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds { get; } =
      new[] { SyntaxKind.ClassDeclaration };

    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteClassMarkRuleHelpers.BuildDeclarationMarks(context, root, RuleId);
    }
}

public sealed class ClassExpressionMarkRule : RuleDefinitionMark
{
    private static readonly IReadOnlyList<SyntaxKind> SupportedKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.SimpleMemberAccessExpression,
        SyntaxKind.MemberBindingExpression,
        SyntaxKind.InvocationExpression,
        SyntaxKind.ElementAccessExpression,
        SyntaxKind.ConditionalAccessExpression,
        SyntaxKind.ObjectCreationExpression,
        SyntaxKind.ImplicitObjectCreationExpression
      };

    public override string CapabilityId { get; } = "mark.type.expression";

    public override string RuleId { get; } = "DEL-CLASS-MARK-EXPR-001";

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Match expressions that reference the delete-class target";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => SupportedKinds;

    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteClassMarkRuleHelpers.BuildExpressionMarks(
          context,
          root,
          RuleId,
          SupportedKinds);
    }
}

public sealed class ClassTypeSyntaxMarkRule : RuleDefinitionMark
{
    private static readonly IReadOnlyList<SyntaxKind> SupportedKinds =
      new[]
      {
        SyntaxKind.IdentifierName,
        SyntaxKind.QualifiedName,
        SyntaxKind.AliasQualifiedName,
        SyntaxKind.GenericName
      };

    public override string CapabilityId { get; } = "mark.type.type-syntax";

    public override string RuleId { get; } = "DEL-CLASS-MARK-TYPE-001";

    public override string GroupKey { get; } = "DEL-CLASS";

    public override string Name { get; } = "Match type syntax that references the delete-class target";

    public override IReadOnlyList<SyntaxKind> AllowedMarkNodeKinds => SupportedKinds;

    public override IEnumerable<MarkRecord> Mark(RuleContext context, SyntaxNode root)
    {
        return DeleteClassMarkRuleHelpers.BuildTypeSyntaxMarks(
          context,
          root,
          RuleId,
          SupportedKinds);
    }
}
